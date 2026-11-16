import { readFile, writeFile } from "node:fs/promises";

export interface RecorderEvent {
  schema: 1;
  session: string;
  seq: number;
  tsUS: number;
  thread: number;
  name: string;
  data: Record<string, unknown>;
}

// Preserve sessions as distinct Perfetto processes: their clocks start separately.
export function traceEvents(lines: string[]) {
  const sessions = new Map<string, number>();
  const seen = new Set<string>();

  return lines
    .filter((x) => x.trim())
    .map((line) => {
      const event = JSON.parse(line) as RecorderEvent;

      if (
        event.schema !== 1 ||
        typeof event.session !== "string" ||
        !event.session ||
        !Number.isSafeInteger(event.seq) ||
        event.seq < 1 ||
        !Number.isSafeInteger(event.tsUS) ||
        event.tsUS < 0 ||
        !Number.isSafeInteger(event.thread) ||
        event.thread < 1 ||
        typeof event.name !== "string" ||
        !event.data ||
        typeof event.data !== "object" ||
        Array.isArray(event.data)
      ) {
        throw Error("Unsupported or malformed flight-recorder event");
      }
      const identity = `${event.session}:${event.seq}`;

      if (seen.has(identity)) {
        throw Error(
          "Duplicate event: pass either rolling files or the stall capture, not both"
        );
      }
      seen.add(identity);

      if (!sessions.has(event.session)) {
        sessions.set(event.session, sessions.size + 1);
      }
      const duration =
        event.name === "ui.dispatch" ? event.data.durationMS : undefined;

      if (
        duration !== undefined &&
        (typeof duration !== "number" ||
          !Number.isFinite(duration) ||
          duration < 0 ||
          duration * 1000 > event.tsUS + 1000)
      ) {
        throw Error("Invalid dispatcher duration");
      }
      return {
        name: event.name,
        cat: event.name.split(".")[0],
        ph: duration === undefined ? "i" : "X",
        ...(duration === undefined ? { s: "t" } : { dur: duration * 1000 }),
        ts:
          duration === undefined
            ? event.tsUS
            : Math.max(0, event.tsUS - duration * 1000),
        pid: sessions.get(event.session),
        tid: event.thread,
        args: { ...event.data, session: event.session, seq: event.seq },
      };
    })
    .sort((a, b) => a.pid! - b.pid! || a.ts - b.ts);
}

if (import.meta.main) {
  const [output, ...inputs] = process.argv.slice(2);

  if (!output || !inputs.length) {
    throw Error(
      "Usage: hdt-proton trace-export OUTPUT.json INPUT.jsonl [INPUT.jsonl ...]"
    );
  }
  const lines = (
    await Promise.all(inputs.map(async (path) => readFile(path, "utf-8")))
  ).flatMap((text) => text.split("\n"));

  const events = traceEvents(lines);

  if (!events.length) {
    throw Error("No trace events found");
  }
  // Avoid accidentally replacing the diagnostic source or an existing export.
  await writeFile(output, JSON.stringify({ traceEvents: events }), {
    flag: "wx",
    mode: 0o600,
  });
  console.log(`Exported ${events.length} events to ${output}`);
}
