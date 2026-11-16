import { test, expect } from "bun:test";
import {
  mkdir,
  mkdtemp,
  readFile,
  writeFile,
  rm,
  stat,
} from "node:fs/promises";
import path from "node:path";

import { traceEvents } from "../src/diagnostics/trace-export";

interface EventChange {
  schema?: number;
  session?: string;
  tsUS?: number;
  name?: string;
  data?: { durationMS?: number } | never[];
  thread?: number;
  seq?: number;
}

const event = (change: EventChange = {}) =>
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

const run = async (...args: string[]) => {
  const child = Bun.spawn(
    [
      process.execPath,
      path.resolve(import.meta.dir, "../src/diagnostics/trace-export.ts"),
      ...args,
    ],
    { stdout: "pipe", stderr: "pipe" }
  );

  const [status, , stderr] = await Promise.all([
    child.exited,
    new Response(child.stdout).text(),
    new Response(child.stderr).text(),
  ]);

  return { status, stderr };
};

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
  const root = path.resolve(import.meta.dir, "../evidence");
  await mkdir(root, { recursive: true });
  const directory = await mkdtemp(path.join(root, "trace-export-test-"));

  try {
    const input = path.join(directory, "events.jsonl");
    const output = path.join(directory, "trace.json");

    const source = `${event()}\n`;
    await writeFile(input, source);

    const initialRun = await run(output, input);
    expect(initialRun.status).toBe(0);
    const outputStat = await stat(output);
    // oxlint-disable-next-line no-bitwise -- POSIX mode mask verifies no group/other access to exported evidence.
    expect(outputStat.mode & 0o077).toBe(0);
    const exported = await readFile(output, "utf-8");
    const parsed: unknown = JSON.parse(exported);

    /* oxlint-disable anti-slop/no-runtime-typeof -- Parse the actual CLI output contract at its file boundary. */
    if (
      typeof parsed !== "object" ||
      parsed === null ||
      !("traceEvents" in parsed) ||
      !Array.isArray(parsed.traceEvents)
    ) {
      throw new Error("CLI export is missing its trace event array");
    }

    /* oxlint-enable anti-slop/no-runtime-typeof */
    expect(parsed.traceEvents).toHaveLength(1);
    const existing = await run(output, input);
    expect(existing.status).not.toBe(0);
    expect(existing.stderr).toContain("EEXIST");
    expect(await readFile(output, "utf-8")).toBe(exported);
    const duplicate = path.join(directory, "duplicate.json");
    const duplicateRun = await run(duplicate, input, input);
    expect(duplicateRun.status).not.toBe(0);
    expect(await Bun.file(duplicate).exists()).toBe(false);
    expect(await readFile(input, "utf-8")).toBe(source);
  } finally {
    await rm(directory, { recursive: true });
  }
});
