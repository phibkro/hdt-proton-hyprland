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
import path from "node:path";

import { InputJournal } from "../src/diagnostics/input-journal";

const fixture = async (run: (root: string) => Promise<void>) => {
  const base = path.resolve(import.meta.dir, "../evidence");
  await mkdir(base, { recursive: true });
  const root = await mkdtemp(path.join(base, "journal-test-"));

  try {
    await run(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
};

interface JournalRow {
  kind: string;
  data: {
    reason?: string;
    continuity?: string;
    from?: string;
    source?: string;
    generation?: number;
    base64?: string;
  };
}

interface ByteRow extends JournalRow {
  data: { source: string; generation: number; base64: string };
}

// This is the journal JSON I/O boundary: validate fields before they enter test assertions.
/* oxlint-disable anti-slop/no-runtime-typeof, anti-slop/no-unknown-parameters -- No schema dependency is shipped; these parsers establish the journal payload contract at the file boundary. */
const optionalString = (value: unknown): string | undefined => {
  if (value === undefined || typeof value === "string") {
    return value;
  }

  throw new Error("Invalid journal string field");
};

const optionalGeneration = (value: unknown): number | undefined => {
  if (value === undefined) {
    return value;
  }

  if (typeof value === "number" && Number.isSafeInteger(value) && value >= 0) {
    return value;
  }

  throw new Error("Invalid journal generation");
};

const parseRow = (line: string): JournalRow => {
  const value: unknown = JSON.parse(line);

  if (
    typeof value !== "object" ||
    value === null ||
    !("kind" in value) ||
    typeof value.kind !== "string" ||
    !("data" in value) ||
    typeof value.data !== "object" ||
    value.data === null ||
    Array.isArray(value.data)
  ) {
    throw new Error("Invalid journal record envelope");
  }

  const { data } = value;

  return {
    kind: value.kind,
    data: {
      continuity: optionalString(
        "continuity" in data ? data.continuity : undefined
      ),
      from: optionalString("from" in data ? data.from : undefined),
      source: optionalString("source" in data ? data.source : undefined),
      base64: optionalString("base64" in data ? data.base64 : undefined),
      generation: optionalGeneration(
        "generation" in data ? data.generation : undefined
      ),
      reason: optionalString("reason" in data ? data.reason : undefined),
    },
  };
};

/* oxlint-enable anti-slop/no-runtime-typeof, anti-slop/no-unknown-parameters */
const records = async (root: string) => {
  const text = await readFile(path.join(root, "journal.jsonl"), "utf-8");

  return text.trim().split("\n").map(parseRow);
};

const byteRows = (rows: JournalRow[]): ByteRow[] =>
  rows.flatMap((row) => {
    if (row.kind !== "source.bytes") {
      return [];
    }

    const { source, generation, base64 } = row.data;

    if (
      source === undefined ||
      generation === undefined ||
      !Number.isSafeInteger(generation) ||
      generation < 0 ||
      base64 === undefined ||
      Buffer.from(base64, "base64").toString("base64") !== base64
    ) {
      throw new Error("Invalid source.bytes journal payload");
    }

    return [{ kind: row.kind, data: { source, generation, base64 } }];
  });

const byteRecords = async (root: string) => byteRows(await records(root));

const recovered = (rows: JournalRow[], generation: number) =>
  Buffer.concat(
    byteRows(rows).flatMap((row) =>
      row.data.generation === generation
        ? [Buffer.from(row.data.base64, "base64")]
        : []
    )
  );

/* oxlint-disable no-await-in-loop -- Polling must observe each attempt before waiting; parallel attempts would remove the process-liveness boundary. */
const waitFor = async (predicate: () => Promise<boolean>) => {
  for (let i = 0; i < 60; i += 1) {
    if (await predicate()) {
      return;
    }

    await Bun.sleep(50);
  }

  throw new Error("Timed out waiting for real process evidence.");
};

/* oxlint-enable no-await-in-loop */

const expectRejection = async (operation: Promise<void>, message: string) => {
  try {
    await operation;
  } catch (error) {
    if (!(error instanceof Error)) {
      throw new TypeError("Expected an Error rejection", { cause: error });
    }

    expect(error.message).toContain(message);

    return;
  }

  throw new Error(`Expected rejection containing: ${message}`);
};

for (const replacement of [false, true]) {
  test(`collector survives a source ${replacement ? "replacement" : "disappearance"} between stat and open`, async () => {
    await fixture(async (root) => {
      const source = path.join(root, "state.json");
      const output = path.join(root, "journal");

      await writeFile(source, "old");
      let raced = false;

      class RacingJournal extends InputJournal {
        protected override async openSource(sourcePath: string) {
          if (!raced) {
            raced = true;
            await rename(sourcePath, `${sourcePath}.old`);

            if (replacement) {
              await writeFile(sourcePath, "new");
            }
          }

          return await super.openSource(sourcePath);
        }
      }

      const journal = new RacingJournal(
        output,
        [{ name: "state", path: source }],
        "end",
        1_048_576
      );

      await journal.start();

      try {
        await journal.poll();
        expect(raced).toBe(true);

        if (!replacement) {
          await writeFile(source, "new");
        }

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
    });
  });
}

test("journal preserves exact bytes across partial UTF-8 writes and rotation", async () => {
  await fixture(async (root) => {
    const source = path.join(root, "hdt.log");
    const output = path.join(root, "journal");

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
    expect(rows.find((r) => r.kind === "source.replaced")).toMatchObject({
      data: { continuity: "unknown" },
    });
    const directoryStat = await stat(output);
    const journalStat = await stat(path.join(output, "journal.jsonl"));

    // oxlint-disable-next-line no-bitwise -- POSIX mode mask verifies no group/other access to evidence.
    expect(directoryStat.mode & 0o077).toBe(0);
    // oxlint-disable-next-line no-bitwise -- POSIX mode mask verifies no group/other access to evidence.
    expect(journalStat.mode & 0o077).toBe(0);
  });
});

test("same-inode rewrite and truncation start explicit generations", async () => {
  await fixture(async (root) => {
    const source = path.join(root, "hdt.log");
    const output = path.join(root, "journal");

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
  });
});

test("capacity failure is explicit, bounded, and preserves acquired evidence", async () => {
  await fixture(async (root) => {
    const source = path.join(root, "hdt.log");
    const output = path.join(root, "journal");

    await writeFile(source, Buffer.alloc(100_000, 65));

    const journal = new InputJournal(
      output,
      [{ name: "hdt", path: source }],
      "start",
      65_536
    );

    await journal.start();
    await journal.poll();
    await expectRejection(journal.poll(), "capacity exhausted");
    await journal.close("capacity exhausted");
    const rows = await records(output);
    expect(rows.at(-1)).toMatchObject({
      kind: "journal.stop",
      data: { reason: "capacity exhausted" },
    });
    const journalStat = await stat(path.join(output, "journal.jsonl"));
    expect(journalStat.size).toBeLessThanOrEqual(65_536);
    expect(recovered(rows, 0)).toEqual(Buffer.alloc(32_768, 65));
  });
});

test("collector refuses existing evidence and nonregular sources", async () => {
  await fixture(async (root) => {
    const output = path.join(root, "journal");
    await mkdir(output);
    await expectRejection(
      new InputJournal(
        output,
        [{ name: "hdt", path: path.join(root, "missing") }],
        "start",
        65_536
      ).start(),
      "EEXIST"
    );

    const target = path.join(root, "file");
    const source = path.join(root, "link");

    await writeFile(target, "private");
    await symlink(target, source);

    const journal = new InputJournal(
      path.join(root, "second"),
      [{ name: "hdt", path: source }],
      "start",
      65_536
    );

    await journal.start();
    await expectRejection(journal.poll(), "regular file");
    await journal.close("rejected source");
  });
});

test("end starts after existing bytes but captures a source created after observation began", async () => {
  await fixture(async (root) => {
    const existing = path.join(root, "existing");
    const created = path.join(root, "created");
    const output = path.join(root, "journal");

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

    const captured = byteRows(rows).map((r) => [
      r.data.source,
      Buffer.from(r.data.base64, "base64").toString(),
    ]);

    expect(captured).toEqual([
      ["old", "next"],
      ["new", "first"],
    ]);
  });
});

test("native collector survives its observed producer stopping, dying and restarting", async () => {
  await fixture(async (root) => {
    const source = path.join(root, "game.log");
    const hdt = path.join(root, "hdt.log");
    const output = path.join(root, "journal");

    const recorderRoot = path.join(root, "recorders");
    await mkdir(recorderRoot);
    await writeFile(source, "");
    await writeFile(hdt, "");

    const collector = Bun.spawn(
      [
        process.execPath,
        path.resolve(import.meta.dir, "../src/diagnostics/input-journal.ts"),
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

    try {
      await waitFor(
        async () => await Bun.file(path.join(output, "journal.jsonl")).exists()
      );

      const first = path.join(
        recorderRoot,
        `flight-recorder-12-${"a".repeat(32)}`
      );

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
      await waitFor(async () => {
        const text = await readFile(hdt, "utf-8");

        return text.includes("running");
      });
      producer.kill("SIGSTOP");
      await appendFile(source, "game after tracker stall\n");
      await waitFor(async () => {
        const observed = await byteRecords(output);

        return observed.some(
          (r) =>
            r.kind === "source.bytes" &&
            Buffer.from(r.data.base64, "base64").includes(
              Buffer.from("game after tracker stall")
            )
        );
      });
      producer.kill("SIGKILL");
      await producer.exited;
      producer = undefined;
      await appendFile(source, "game after tracker exit\n");

      const second = path.join(
        recorderRoot,
        `flight-recorder-12-${"b".repeat(32)}`
      );

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
      await waitFor(async () => {
        const observed = await byteRecords(output);

        return observed.some(
          (r) =>
            r.kind === "source.bytes" &&
            Buffer.from(r.data.base64, "base64").includes(
              Buffer.from("restarted")
            )
        );
      });
      await waitFor(async () => {
        const observed = await byteRecords(output);

        return (
          observed.filter(
            (r) =>
              r.kind === "source.bytes" && r.data.source.startsWith("recorder-")
          ).length === 2
        );
      });
      collector.kill("SIGTERM");
      expect(await collector.exited).toBe(0);
      const rows = await records(output);
      expect(
        byteRows(rows)
          .filter((r) => r.data.source === "game")
          .map((r) => Buffer.from(r.data.base64, "base64").toString())
          .join("")
      ).toContain("game after tracker exit");
      expect(rows.at(-1)).toMatchObject({
        kind: "journal.stop",
        data: { reason: "signal" },
      });
      expect(
        byteRows(rows)
          .filter((r) => r.data.source.startsWith("recorder-"))
          .map((r) => Buffer.from(r.data.base64, "base64").toString())
          .toSorted()
      ).toEqual(["first recorder\n", "second recorder\n"]);
    } finally {
      if (producer !== undefined) {
        producer.kill("SIGKILL");
        await producer.exited;
      }

      if (collector.exitCode === null) {
        collector.kill("SIGKILL");
        await collector.exited;
      }
    }
  });
});

test("end policy skips existing recorder history and captures every new session from byte zero", async () => {
  await fixture(async (root) => {
    const recorders = path.join(root, "recorders");
    const output = path.join(root, "journal");

    await mkdir(recorders);

    const session = (id: string) =>
      path.join(recorders, `flight-recorder-12-${id.repeat(32)}`);

    await mkdir(session("a"));
    await writeFile(path.join(session("a"), "events.jsonl"), "history\n");
    const journal = new InputJournal(output, [], "end", 1_048_576, recorders);
    await journal.start();
    await journal.poll();
    await appendFile(
      path.join(session("a"), "events.jsonl"),
      "old writer still alive\n"
    );

    // oxlint-disable no-await-in-loop -- Create each recorder directory before publishing its file, in deterministic discovery order.
    for (const id of ["b", "c"]) {
      await mkdir(session(id));
      await writeFile(
        path.join(session(id), "events.jsonl"),
        `${id} first offer\n`
      );
    }

    // oxlint-enable no-await-in-loop
    await journal.poll();
    await journal.close("test");
    const rows = await records(output);
    expect(
      rows
        .filter((r) => r.kind === "recorder.discovered")
        .map((r) => r.data.from)
    ).toEqual(["end", "start", "start"]);

    const bytes = byteRows(rows).map((r) =>
      Buffer.from(r.data.base64, "base64").toString()
    );

    expect(bytes).toEqual([
      "old writer still alive\n",
      "b first offer\n",
      "c first offer\n",
    ]);
    expect(new Set(byteRows(rows).map((r) => r.data.source)).size).toBe(3);
  });
});

test("recorder root and events can appear after capture starts", async () => {
  await fixture(async (root) => {
    const recorders = path.join(root, "recorders");
    const output = path.join(root, "journal");

    const journal = new InputJournal(output, [], "end", 1_048_576, recorders);
    await journal.start();
    await journal.poll();

    const session = path.join(
      recorders,
      `flight-recorder-99-${"d".repeat(32)}`
    );

    await mkdir(session, { recursive: true });
    await journal.poll();
    await writeFile(path.join(session, "events.jsonl"), "first event\n");
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
      byteRows(rows).map((r) => Buffer.from(r.data.base64, "base64").toString())
    ).toEqual(["first event\n"]);
  });
});

test("recorder discovery refuses aliases and symlinks", async () => {
  await fixture(async (root) => {
    const recorders = path.join(root, "recorders");
    await mkdir(recorders);
    expect(
      () =>
        new InputJournal(
          path.join(root, "reserved"),
          [{ name: "recorder-alias", path: path.join(root, "other") }],
          "start",
          65_536,
          recorders
        )
    ).toThrow("reserved");

    const session = path.join(
      recorders,
      `flight-recorder-42-${"e".repeat(32)}`
    );

    await mkdir(session);

    const alias = new InputJournal(
      path.join(root, "alias"),
      [{ name: "alias", path: path.join(session, "events.jsonl") }],
      "start",
      65_536,
      recorders
    );

    try {
      await expectRejection(alias.start(), "duplicates");
    } finally {
      await alias.close("test");
    }

    await rm(session, { recursive: true });
    await symlink(root, session);

    const linked = new InputJournal(
      path.join(root, "linked"),
      [],
      "start",
      65_536,
      recorders
    );

    try {
      await expectRejection(linked.start(), "not a symlink");
    } finally {
      await linked.close("test");
    }
  });
});

test("recorder session capacity fails explicitly without discarding captured sessions", async () => {
  await fixture(async (root) => {
    const recorders = path.join(root, "recorders");
    await mkdir(recorders);

    // oxlint-disable no-await-in-loop -- Bound fixture filesystem pressure while constructing all 128 real sessions.
    for (let i = 1; i <= 128; i += 1) {
      await mkdir(
        path.join(recorders, `flight-recorder-${i}-${"f".repeat(32)}`)
      );
    }

    // oxlint-enable no-await-in-loop
    const output = path.join(root, "journal");
    const journal = new InputJournal(output, [], "start", 1_048_576, recorders);

    await journal.start();
    await journal.poll();
    await mkdir(path.join(recorders, `flight-recorder-129-${"f".repeat(32)}`));
    await expectRejection(journal.poll(), "session capacity exhausted");
    await journal.close("session capacity exhausted");
    const rows = await records(output);
    expect(rows.filter((r) => r.kind === "recorder.discovered").length).toBe(
      128
    );
    expect(rows.at(-1)).toMatchObject({
      kind: "journal.stop",
      data: { reason: "session capacity exhausted" },
    });
  });
});
