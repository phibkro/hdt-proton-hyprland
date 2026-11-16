/* eslint-disable no-await-in-loop -- Fixture setup, polling, and cleanup serialize shared files and owned processes. */
import { test, expect } from "bun:test";
import {
  mkdtemp,
  mkdir,
  writeFile,
  readFile,
  chmod,
  rm,
  rename,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

// Only compositor snapshots and time are simulated. The watcher, launcher,
// process identity checks, lock, and child exit statuses are real processes.
const streamText = async (stream: ReturnType<typeof Bun.spawn>["stderr"]) => {
  if (!(stream instanceof ReadableStream)) {
    throw new TypeError("Expected a piped subprocess stream");
  }

  return await new Response(stream).text();
};

interface Fixture {
  dir: string;
  clients: string;
  start: () => ReturnType<typeof Bun.spawn>;
  launches: () => Promise<Launch[]>;
  wait: (f: () => Promise<boolean>) => Promise<void>;
  processes: ReturnType<typeof Bun.spawn>[];
  publish: (snapshot: Client[]) => Promise<void>;
}

type Launch = readonly [pid: number, at: number];

interface Client {
  class: string;
  title: string;
  pid: number;
}

const launchCount = async (fixture: Fixture) => {
  const entries = await fixture.launches();

  return entries.length;
};

const fixture = async (mode: string, run: (f: Fixture) => Promise<void>) => {
  const dir = await mkdtemp(path.join(tmpdir(), "hdt-watch-"));
  const processes: ReturnType<typeof Bun.spawn>[] = [];

  const clients = path.join(dir, "clients");
  const calls = path.join(dir, "calls");

  await mkdir(path.join(dir, "runtime"));
  await writeFile(clients, "[]");
  await writeFile(path.join(dir, "clock"), "100");

  const sources = new Map([
    ["bun", 'printf "%s\\n" steam_app_123'],
    ["hyprctl", 'cat "$TEST_DIR/clients"'],
    ["date", 'cat "$TEST_DIR/clock"'],
    [
      "sleep",
      `clock=$(cat "$TEST_DIR/clock"); printf '%s\\n' "$((clock+2))" > "$TEST_DIR/clock.next"; mv "$TEST_DIR/clock.next" "$TEST_DIR/clock"; exec '${Bun.which("sleep")}' 0.03`,
    ],
    [
      "hdt-overlay-launch",
      `printf '%s %s\\n' "$$" "$(cat "$TEST_DIR/clock")" >> "$TEST_DIR/calls"
case "$TEST_MODE" in
  crash) exit 1 ;;
  normal) exit 0 ;;
  recover) if [ "$(wc -l < "$TEST_DIR/calls")" -lt 3 ]; then exit 1; fi; exec "$TEST_BUN" -e 'setInterval(()=>{},1000)' ;;
  hold) exec "$TEST_BUN" -e 'setInterval(()=>{},1000)' ;;
esac`,
    ],
  ]);

  if (mode === "lock-error") {
    sources.set("flock", "exit 73");
  }

  for (const [name, source] of sources) {
    const file = path.join(dir, name);
    await writeFile(file, `#!/bin/sh\n${source}\n`);
    await chmod(file, 0o700);
  }

  const env = {
    ...process.env,
    PATH: `${dir}:${process.env.PATH}`,
    XDG_RUNTIME_DIR: path.join(dir, "runtime"),
    TEST_DIR: dir,
    TEST_MODE: mode,
    TEST_BUN: process.execPath,
    HS_APP_ID: "123",
  };

  const start = () => {
    const p = Bun.spawn(
      ["bash", path.resolve(import.meta.dir, "../../scripts/autostart.sh")],
      { env, stdin: "ignore", stdout: "pipe", stderr: "pipe" }
    );

    processes.push(p);

    return p;
  };

  const launches = async () => {
    try {
      const contents = await readFile(calls, "utf-8");
      const lines = contents.split("\n");
      // The writer can still own the final fragment; only newline commits a record.
      lines.pop();
      const entries: Launch[] = [];

      for (const line of lines) {
        const [processText, timeText, ...extra] = line.split(" ");
        const id = Number(processText);
        const at = Number(timeText);

        if (
          extra.length !== 0 ||
          !Number.isSafeInteger(id) ||
          id <= 0 ||
          !Number.isSafeInteger(at) ||
          at < 0
        ) {
          throw new Error(
            `Malformed complete launch record: ${JSON.stringify(line)}`
          );
        }

        entries.push([id, at]);
      }

      return entries;
    } catch (error) {
      if (
        error instanceof Error &&
        "code" in error &&
        error.code === "ENOENT"
      ) {
        return [];
      }

      throw error;
    }
  };

  const publish = async (snapshot: Client[]) => {
    await writeFile(`${clients}.next`, JSON.stringify(snapshot));
    await rename(`${clients}.next`, clients);
  };

  const wait = async (f: () => Promise<boolean>) => {
    for (let i = 0; i < 150; i += 1) {
      if (await f()) {
        return;
      }

      if (
        processes[0]?.exitCode !== null &&
        processes[0]?.exitCode !== undefined
      ) {
        throw new Error(
          `Fixture process exited (${processes[0].exitCode}): ${await streamText(processes[0].stderr)}`
        );
      }

      await Bun.sleep(20);
    }

    throw new Error("Watcher fixture timed out");
  };

  try {
    await run({ dir, clients, start, launches, wait, processes, publish });
  } finally {
    for (const p of processes) {
      if (p.exitCode === null) {
        p.kill("SIGTERM");
        await p.exited;
      }
    }

    // Launcher stand-ins belong to this fixture. Never signal a snapshot's PID.
    for (const [pid] of await launches()) {
      try {
        if (/\) [ZX] /u.test(await readFile(`/proc/${pid}/stat`, "utf-8"))) {
          continue;
        }

        const contents = await readFile(`/proc/${pid}/environ`, "utf-8");
        const environment = contents.split("\0");

        if (environment.includes(`TEST_DIR=${dir}`)) {
          process.kill(pid, "SIGTERM");
        }
      } catch (error) {
        if (
          !(
            error instanceof Error &&
            "code" in error &&
            (error.code === "ESRCH" || error.code === "ENOENT")
          )
        ) {
          // eslint-disable-next-line no-unsafe-finally -- Unexpected cleanup errors must fail the fixture rather than leak owned processes.
          throw error;
        }
      }
    }

    await rm(dir, { recursive: true, force: true });
  }
};

const game = (pid = 123) => ({
  class: "steam_app_123",
  title: "Hearthstone",
  pid,
});

test("lock tool failures cannot be reported as a successful existing owner", async () => {
  await fixture("lock-error", async (f) => {
    await f.publish([game()]);
    const watcher = f.start();
    expect(await watcher.exited).toBe(73);
    expect(await streamText(watcher.stderr)).toContain("Could not acquire");
    expect(await f.launches()).toEqual([]);
  });
});

test("owned launcher crashes retry with backoff, stop after three failures, and reset for a new game", async () => {
  await fixture("crash", async (f) => {
    await f.publish([game()]);
    const watcher = f.start();
    await f.wait(async () => (await launchCount(f)) === 3);
    await Bun.sleep(160);
    const first = await f.launches();
    expect(first.length).toBe(3);
    expect(first[1][1] - first[0][1]).toBeGreaterThanOrEqual(5);
    expect(first[2][1] - first[1][1]).toBeGreaterThanOrEqual(10);
    await f.publish([game(124)]);
    await f.wait(async () => (await launchCount(f)) === 6);
    await Bun.sleep(120);
    expect(await launchCount(f)).toBe(6);
    watcher.kill("SIGTERM");
    await watcher.exited;
    expect(await streamText(watcher.stdout)).toContain("failed three times");
  });
});

test("a launcher that survives a minute gets a fresh failure budget", async () => {
  await fixture("recover", async (f) => {
    await f.publish([game()]);
    f.start();
    await f.wait(async () => (await launchCount(f)) === 3);
    const entries = await f.launches();
    const [third] = entries.slice(2);
    await f.wait(
      async () =>
        Number(await readFile(path.join(f.dir, "clock"), "utf-8")) - third[1] >=
        64
    );
    process.kill(third[0], "SIGTERM");
    await f.wait(async () => (await launchCount(f)) === 4);
    expect(await launchCount(f)).toBe(4);
  });
});

test("normal tracker exit stays closed until a new game process appears", async () => {
  await fixture("normal", async (f) => {
    await f.publish([game()]);
    f.start();
    await f.wait(async () => (await launchCount(f)) === 1);
    await Bun.sleep(200);
    expect(await launchCount(f)).toBe(1);
    await f.publish([game(124)]);
    await f.wait(async () => (await launchCount(f)) === 2);
  });
});

test("watcher neither duplicates an in-flight launcher nor keeps its lock in the child", async () => {
  await fixture("hold", async (f) => {
    await f.publish([game()]);
    const watcher = f.start();
    await f.wait(async () => (await launchCount(f)) === 1);
    await Bun.sleep(150);
    expect(await launchCount(f)).toBe(1);
    const [firstLaunch] = await f.launches();
    const [child] = firstLaunch;
    process.kill(child, "SIGSTOP");

    try {
      const before = Number(await readFile(path.join(f.dir, "clock"), "utf-8"));
      await f.wait(
        async () =>
          Number(await readFile(path.join(f.dir, "clock"), "utf-8")) >=
          before + 4
      );
      expect(await launchCount(f)).toBe(1);
      expect(watcher.exitCode).toBeNull();
    } finally {
      process.kill(child, "SIGCONT");
    }

    const duplicate = f.start();
    expect(await duplicate.exited).toBe(0);
    expect(await streamText(duplicate.stdout)).toContain(
      "already has an owner"
    );
    watcher.kill("SIGTERM");
    await watcher.exited;
    // No game means a replacement watcher can acquire the lock without launching.
    await f.publish([]);
    const replacement = f.start();
    await Bun.sleep(100);
    expect(replacement.exitCode).toBeNull();
    expect(await launchCount(f)).toBe(1);
  });
});

test("compositor titles cannot make a non-tracker process count as HDT", async () => {
  await fixture("normal", async (f) => {
    const tracker = {
      class: "steam_app_123",
      title: "Hearthstone Deck Tracker",
      pid: process.pid,
    };

    await f.publish([game(), tracker]);
    f.start();
    await f.wait(async () => (await launchCount(f)) === 1);
    const name = await readFile(`/proc/${process.pid}/comm`, "utf-8");
    expect(name.trim()).not.toBe("HearthstoneDeck");
  });
});

test("watcher leaves an existing live tracker and the game process untouched", async () => {
  await fixture("normal", async (f) => {
    const tracker = Bun.spawn(
      [
        "bash",
        "-c",
        "printf '%s\\n' HearthstoneDeck > /proc/$$/comm; while read -r line; do :; done",
      ],
      { stdin: "pipe", stdout: "pipe", stderr: "pipe" }
    );

    f.processes.push(tracker);
    await f.wait(async () => {
      const name = await readFile(`/proc/${tracker.pid}/comm`, "utf-8");

      return name.trim() === "HearthstoneDeck";
    });
    await f.publish([
      game(process.pid),
      {
        class: "steam_app_123",
        title: "Hearthstone Deck Tracker",
        pid: tracker.pid,
      },
    ]);
    f.start();
    await Bun.sleep(180);
    expect(await f.launches()).toEqual([]);
    expect(tracker.exitCode).toBeNull();
    expect(process.kill(process.pid, 0)).toBe(true);
    tracker.kill("SIGTERM");
    await tracker.exited;
    await Bun.sleep(120);
    expect(await f.launches()).toEqual([]);
  });
});

for (const snapshot of [[], [game(), game(124)], [game(-1)]]) {
  test(`watcher refuses an absent or ambiguous game: ${JSON.stringify(snapshot.map((g) => g.pid))}`, async () => {
    await fixture("normal", async (f) => {
      await f.publish(snapshot);
      const watcher = f.start();
      await Bun.sleep(150);
      expect(await f.launches()).toEqual([]);

      if (snapshot.length) {
        expect(await watcher.exited).not.toBe(0);
      } else {
        expect(watcher.exitCode).toBeNull();
      }
    });
  });
}
