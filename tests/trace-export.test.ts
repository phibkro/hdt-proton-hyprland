import { test, expect } from "bun:test";
import {
  mkdir,
  mkdtemp,
  readFile,
  writeFile,
  rm,
  stat,
} from "node:fs/promises";
import { resolve, join } from "node:path";

import { traceEvents } from "../src/diagnostics/trace-export";

const event = (change: object = {}) =>
  JSON.stringify({
    schema: 1,
    session: "test",
    seq: 1,
    tsUS: 3_500_000,
    thread: 2,
    name: "ui.dispatch",
    data: { durationMS: 3000 },
    ...change,
  });

test("dispatcher delay is a duration ending at its acknowledgment", () => {
  expect(traceEvents([event()])[0]).toMatchObject({
    ph: "X",
    ts: 500_000,
    dur: 3_000_000,
    pid: 1,
    tid: 2,
  });
});

test("sessions have independent clocks and duplicate captures fail loudly", () => {
  const lines = [
    event(),
    event({ session: "next", tsUS: 1000, name: "session.start", data: {} }),
  ];

  expect(traceEvents(lines).map((x) => x.pid)).toEqual([1, 2]);
  expect(() => traceEvents([event(), event()])).toThrow("Duplicate event");
});

test("malformed input cannot become a misleading trace", () => {
  for (const change of [
    { schema: 2 },
    { tsUS: -1 },
    { thread: 0 },
    { seq: 0 },
    { data: [] },
    { data: { durationMS: 4000 } },
  ]) {
    expect(() => traceEvents([event(change)])).toThrow();
  }
});

test("CLI exports privately and refuses to replace evidence or merge duplicate captures", async () => {
  const root = resolve(import.meta.dir, "../evidence");
  await mkdir(root, { recursive: true });
  const directory = await mkdtemp(join(root, "trace-export-test-"));

  try {
    const input = join(directory, "events.jsonl");
    const output = join(directory, "trace.json");

    const source = `${event()}\n`;
    await writeFile(input, source);

    const run = async (...args: string[]) => {
      const process = Bun.spawn(
        [
          Bun.which("bun")!,
          resolve(import.meta.dir, "../src/diagnostics/trace-export.ts"),
          ...args,
        ],
        { stdout: "pipe", stderr: "pipe" }
      );

      const [status, , stderr] = await Promise.all([
        process.exited,
        new Response(process.stdout).text(),
        new Response(process.stderr).text(),
      ]);

      return { status, stderr };
    };

    expect((await run(output, input)).status).toBe(0);
    expect((await stat(output)).mode & 0o077).toBe(0);
    const exported = await readFile(output, "utf-8");
    expect(JSON.parse(exported).traceEvents).toHaveLength(1);
    const existing = await run(output, input);
    expect(existing.status).not.toBe(0);
    expect(existing.stderr).toContain("EEXIST");
    expect(await readFile(output, "utf-8")).toBe(exported);
    const duplicate = join(directory, "duplicate.json");
    expect((await run(duplicate, input, input)).status).not.toBe(0);
    expect(await Bun.file(duplicate).exists()).toBe(false);
    expect(await readFile(input, "utf-8")).toBe(source);
  } finally {
    await rm(directory, { recursive: true });
  }
});
