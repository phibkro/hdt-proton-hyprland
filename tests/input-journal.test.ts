import { test, expect } from "bun:test";
import {
  mkdtemp,
  writeFile,
  appendFile,
  readFile,
  rename,
  rm,
  stat,
  symlink,
  mkdir,
} from "node:fs/promises";
import { join, resolve } from "node:path";

import { InputJournal } from "../src/diagnostics/input-journal";

async function fixture(run: (root: string) => Promise<void>) {
  const base = resolve(import.meta.dir, "../evidence");
  await mkdir(base, { recursive: true });
  const root = await mkdtemp(join(base, "journal-test-"));

  try {
    await run(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

const records = async (root: string) =>
  (await readFile(join(root, "journal.jsonl"), "utf-8"))
    .trim()
    .split("\n")
    .map((s) => JSON.parse(s));

const recovered = (rows: any[], generation: number) =>
  Buffer.concat(
    rows
      .filter(
        (r) => r.kind === "source.bytes" && r.data.generation === generation
      )
      .map((r) => Buffer.from(r.data.base64, "base64"))
  );

for (const replacement of [false, true]) {
  test(`collector survives a source ${replacement ? "replacement" : "disappearance"} between stat and open`, async () =>
    fixture(async (root) => {
      const source = join(root, "state.json"),
        output = join(root, "journal");
      await writeFile(source, "old");
      let raced = false;
      class RacingJournal extends InputJournal {
        protected override async openSource(path: string) {
          if (!raced) {
            raced = true;
            await rename(path, path + ".old");
            if (replacement) await writeFile(path, "new");
          }
          return await super.openSource(path);
        }
      }
      const journal = new RacingJournal(
        output,
        [{ name: "state", path: source }],
        "end",
        1048576
      );
      await journal.start();
      try {
        await journal.poll();
        expect(raced).toBe(true);
        if (!replacement) await writeFile(source, "new");
        await journal.poll();
        await journal.close("test");
        const rows = await records(output);
        expect(
          rows.some(
            (r) =>
              r.kind === "source.open-raced" && r.data.continuity === "unknown"
          )
        ).toBe(true);
        expect(recovered(rows, 1).toString()).toBe("new");
        expect(rows.at(-1)).toMatchObject({
          kind: "journal.stop",
          data: { reason: "test" },
        });
      } finally {
        await journal.close("cleanup");
      }
    }));
}

test("journal preserves exact bytes across partial UTF-8 writes and rotation", async () =>
  fixture(async (root) => {
    const source = join(root, "hdt.log");
    const output = join(root, "journal");

    const bytes = Buffer.from("Info|Game ended\r\n雪\n");
    await writeFile(source, bytes.subarray(0, -2));

    const journal = new InputJournal(
      output,
      [{ name: "hdt", path: source }],
      "start",
      1_048_576
    );

    await journal.start();
    await journal.poll();
    await appendFile(source, bytes.subarray(-2));
    await journal.poll();
    await rename(source, `${source}.old`);
    await writeFile(source, "new session\n");
    await journal.poll();
    await journal.close("test");
    const rows = await records(output);
    expect(recovered(rows, 0)).toEqual(bytes);
    expect(recovered(rows, 1).toString()).toBe("new session\n");
    expect(rows.find((r) => r.kind === "source.replaced").data.continuity).toBe(
      "unknown"
    );
    expect((await stat(output)).mode & 0o077).toBe(0);
    expect((await stat(join(output, "journal.jsonl"))).mode & 0o077).toBe(0);
  }));

test("same-inode rewrite and truncation start explicit generations", async () =>
  fixture(async (root) => {
    const source = join(root, "hdt.log");
    const output = join(root, "journal");

    await writeFile(source, "old body\n");

    const journal = new InputJournal(
      output,
      [{ name: "hdt", path: source }],
      "start",
      1_048_576
    );

    await journal.start();
    await journal.poll();
    await writeFile(source, "replaced with a longer body\n");
    await journal.poll();
    await writeFile(source, "x");
    await journal.poll();
    await journal.close("test");
    const rows = await records(output);
    expect(
      rows
        .filter((r) =>
          ["source.rewritten", "source.truncated"].includes(r.kind)
        )
        .map((r) => r.kind)
    ).toEqual(["source.rewritten", "source.truncated"]);
    expect(recovered(rows, 1).toString()).toBe("replaced with a longer body\n");
    expect(recovered(rows, 2).toString()).toBe("x");
  }));

test("capacity failure is explicit, bounded, and preserves acquired evidence", async () =>
  fixture(async (root) => {
    const source = join(root, "hdt.log");
    const output = join(root, "journal");

    await writeFile(source, Buffer.alloc(100_000, 65));

    const journal = new InputJournal(
      output,
      [{ name: "hdt", path: source }],
      "start",
      65_536
    );

    await journal.start();
    await journal.poll();
    await expect(journal.poll()).rejects.toThrow("capacity exhausted");
    await journal.close("capacity exhausted");
    const rows = await records(output);
    expect(rows.at(-1)).toMatchObject({
      kind: "journal.stop",
      data: { reason: "capacity exhausted" },
    });
    expect(
      (await stat(join(output, "journal.jsonl"))).size
    ).toBeLessThanOrEqual(65_536);
    expect(recovered(rows, 0)).toEqual(Buffer.alloc(32_768, 65));
  }));

test("collector refuses existing evidence and nonregular sources", async () =>
  fixture(async (root) => {
    const output = join(root, "journal");
    await mkdir(output);
    await expect(
      new InputJournal(
        output,
        [{ name: "hdt", path: join(root, "missing") }],
        "start",
        65_536
      ).start()
    ).rejects.toThrow("EEXIST");

    const target = join(root, "file");
    const source = join(root, "link");

    await writeFile(target, "private");
    await symlink(target, source);

    const journal = new InputJournal(
      join(root, "second"),
      [{ name: "hdt", path: source }],
      "start",
      65_536
    );

    await journal.start();
    await expect(journal.poll()).rejects.toThrow("regular file");
    await journal.close("rejected source");
  }));

test("end starts after existing bytes but captures a source created after observation began", async () =>
  fixture(async (root) => {
    const existing = join(root, "existing");
    const created = join(root, "created");
    const output = join(root, "journal");

    await writeFile(existing, "history");

    const journal = new InputJournal(
      output,
      [
        { name: "old", path: existing },
        { name: "new", path: created },
      ],
      "end",
      1_048_576
    );

    await journal.start();
    await journal.poll();
    await appendFile(existing, "next");
    await writeFile(created, "first");
    await journal.poll();
    await journal.close("test");

    const rows = await records(output);
    const captured = rows
      .filter((r) => r.kind === "source.bytes")
      .map((r) => [
        r.data.source,
        Buffer.from(r.data.base64, "base64").toString(),
      ]);

    expect(captured).toEqual([
      ["old", "next"],
      ["new", "first"],
    ]);
  }));

test("native collector survives its observed producer stopping, dying and restarting", async () =>
  fixture(async (root) => {
    const source = join(root, "game.log");
    const hdt = join(root, "hdt.log");
    const output = join(root, "journal");

    const recorderRoot = join(root, "recorders");
    await mkdir(recorderRoot);
    await writeFile(source, "");
    await writeFile(hdt, "");

    const collector = Bun.spawn(
      [
        process.execPath,
        resolve(import.meta.dir, "../src/diagnostics/input-journal.ts"),
        "--output",
        output,
        "--from",
        "start",
        "--max-bytes",
        "1048576",
        "--source",
        `game=${source}`,
        "--source",
        `hdt=${hdt}`,
        "--recorder-root",
        recorderRoot,
      ],
      { stdout: "pipe", stderr: "pipe" }
    );

    let producer: ReturnType<typeof Bun.spawn> | undefined;

    const waitFor = async (predicate: () => Promise<boolean>) => {
      for (let i = 0; i < 60; i++) {
        if (await predicate()) {
          return;
        }
        await Bun.sleep(50);
      }

      throw new Error("Timed out waiting for real process evidence.");
    };

    try {
      await waitFor(
        async () => await Bun.file(join(output, "journal.jsonl")).exists()
      );
      const first = join(recorderRoot, `flight-recorder-12-${"a".repeat(32)}`);
      producer = Bun.spawn(
        [
          process.execPath,
          "-e",
          "const fs=require('node:fs'); fs.mkdirSync(process.argv[2]); fs.writeFileSync(process.argv[2]+'/events.jsonl','first recorder\\n'); fs.appendFileSync(process.argv[1],'running\\n'); setInterval(()=>{},1000)",
          hdt,
          first,
        ],
        { stdout: "pipe", stderr: "pipe" }
      );
      await waitFor(async () =>
        (await readFile(hdt, "utf-8")).includes("running")
      );
      producer.kill("SIGSTOP");
      await appendFile(source, "game after tracker stall\n");
      await waitFor(async () =>
        (await records(output)).some(
          (r) =>
            r.kind === "source.bytes" &&
            Buffer.from(r.data.base64, "base64").includes(
              Buffer.from("game after tracker stall")
            )
        )
      );
      producer.kill("SIGKILL");
      await producer.exited;
      producer = undefined;
      await appendFile(source, "game after tracker exit\n");
      const second = join(recorderRoot, `flight-recorder-12-${"b".repeat(32)}`);
      producer = Bun.spawn(
        [
          process.execPath,
          "-e",
          "const fs=require('node:fs'); fs.mkdirSync(process.argv[2]); fs.writeFileSync(process.argv[2]+'/events.jsonl','second recorder\\n'); fs.appendFileSync(process.argv[1],'restarted\\n')",
          hdt,
          second,
        ],
        { stdout: "pipe", stderr: "pipe" }
      );
      expect(await producer.exited).toBe(0);
      producer = undefined;
      await waitFor(async () =>
        (await records(output)).some(
          (r) =>
            r.kind === "source.bytes" &&
            Buffer.from(r.data.base64, "base64").includes(
              Buffer.from("restarted")
            )
        )
      );
      await waitFor(
        async () =>
          (await records(output)).filter(
            (r) =>
              r.kind === "source.bytes" && r.data.source.startsWith("recorder-")
          ).length === 2
      );
      collector.kill("SIGTERM");
      expect(await collector.exited).toBe(0);
      const rows = await records(output);
      expect(
        rows
          .filter((r) => r.kind === "source.bytes" && r.data.source === "game")
          .map((r) => Buffer.from(r.data.base64, "base64").toString())
          .join("")
      ).toContain("game after tracker exit");
      expect(rows.at(-1)).toMatchObject({
        kind: "journal.stop",
        data: { reason: "signal" },
      });
      expect(
        rows
          .filter(
            (r) =>
              r.kind === "source.bytes" && r.data.source.startsWith("recorder-")
          )
          .map((r) => Buffer.from(r.data.base64, "base64").toString())
          .sort()
      ).toEqual(["first recorder\n", "second recorder\n"]);
    } finally {
      if (producer) {
        producer.kill("SIGKILL");
        await producer.exited;
      }

      if (collector.exitCode === null) {
        collector.kill("SIGKILL");
        await collector.exited;
      }
    }
  }));

test("end policy skips existing recorder history and captures every new session from byte zero", async () =>
  fixture(async (root) => {
    const recorders = join(root, "recorders");
    const output = join(root, "journal");

    await mkdir(recorders);

    const session = (id: string) =>
      join(recorders, `flight-recorder-12-${id.repeat(32)}`);

    await mkdir(session("a"));
    await writeFile(join(session("a"), "events.jsonl"), "history\n");
    const journal = new InputJournal(output, [], "end", 1_048_576, recorders);
    await journal.start();
    await journal.poll();
    await appendFile(
      join(session("a"), "events.jsonl"),
      "old writer still alive\n"
    );

    for (const id of ["b", "c"]) {
      await mkdir(session(id));
      await writeFile(join(session(id), "events.jsonl"), `${id} first offer\n`);
    }

    await journal.poll();
    await journal.close("test");
    const rows = await records(output);
    expect(
      rows
        .filter((r) => r.kind === "recorder.discovered")
        .map((r) => r.data.from)
    ).toEqual(["end", "start", "start"]);

    const bytes = rows
      .filter((r) => r.kind === "source.bytes")
      .map((r) => Buffer.from(r.data.base64, "base64").toString());

    expect(bytes).toEqual([
      "old writer still alive\n",
      "b first offer\n",
      "c first offer\n",
    ]);
    expect(
      new Set(
        rows.filter((r) => r.kind === "source.bytes").map((r) => r.data.source)
      ).size
    ).toBe(3);
  }));

test("recorder root and events can appear after capture starts", async () =>
  fixture(async (root) => {
    const recorders = join(root, "recorders");
    const output = join(root, "journal");

    const journal = new InputJournal(output, [], "end", 1_048_576, recorders);
    await journal.start();
    await journal.poll();
    const session = join(recorders, `flight-recorder-99-${"d".repeat(32)}`);
    await mkdir(session, { recursive: true });
    await journal.poll();
    await writeFile(join(session, "events.jsonl"), "first event\n");
    await journal.poll();
    await journal.close("test");
    const rows = await records(output);
    expect(
      rows.filter((r) => r.kind === "recorder.root.unavailable").length
    ).toBe(1);
    expect(
      rows.filter((r) => r.kind === "recorder.root.available").length
    ).toBe(1);
    expect(
      rows
        .filter((r) => r.kind === "source.bytes")
        .map((r) => Buffer.from(r.data.base64, "base64").toString())
    ).toEqual(["first event\n"]);
  }));

test("recorder discovery refuses aliases and symlinks", async () =>
  fixture(async (root) => {
    const recorders = join(root, "recorders");
    await mkdir(recorders);
    expect(
      () =>
        new InputJournal(
          join(root, "reserved"),
          [{ name: "recorder-alias", path: join(root, "other") }],
          "start",
          65_536,
          recorders
        )
    ).toThrow("reserved");
    const session = join(recorders, `flight-recorder-42-${"e".repeat(32)}`);
    await mkdir(session);

    const alias = new InputJournal(
      join(root, "alias"),
      [{ name: "alias", path: join(session, "events.jsonl") }],
      "start",
      65_536,
      recorders
    );

    try {
      await expect(alias.start()).rejects.toThrow("duplicates");
    } finally {
      await alias.close("test");
    }

    await rm(session, { recursive: true });
    await symlink(root, session);

    const linked = new InputJournal(
      join(root, "linked"),
      [],
      "start",
      65_536,
      recorders
    );

    try {
      await expect(linked.start()).rejects.toThrow("not a symlink");
    } finally {
      await linked.close("test");
    }
  }));

test("recorder session capacity fails explicitly without discarding captured sessions", async () =>
  fixture(async (root) => {
    const recorders = join(root, "recorders");
    await mkdir(recorders);

    for (let i = 1; i <= 128; i++) {
      await mkdir(join(recorders, `flight-recorder-${i}-` + "f".repeat(32)));
    }
    const output = join(root, "journal");
    const journal = new InputJournal(output, [], "start", 1_048_576, recorders);

    await journal.start();
    await journal.poll();
    await mkdir(join(recorders, `flight-recorder-129-${"f".repeat(32)}`));
    await expect(journal.poll()).rejects.toThrow("session capacity exhausted");
    await journal.close("session capacity exhausted");
    const rows = await records(output);
    expect(rows.filter((r) => r.kind === "recorder.discovered").length).toBe(
      128
    );
    expect(rows.at(-1)).toMatchObject({
      kind: "journal.stop",
      data: { reason: "session capacity exhausted" },
    });
  }));
