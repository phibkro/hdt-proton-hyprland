import { expect, test } from "bun:test";
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const script = path.resolve(
  import.meta.dir,
  "../../scripts/capture-native-error.sh"
);

test("native capture refuses an unrelated real process before creating evidence", () => {
  const directory = mkdtempSync(path.join(tmpdir(), "hdt-native-boundary-"));
  const output = path.join(directory, "capture");

  try {
    const result = Bun.spawnSync([
      "bash",
      script,
      String(process.pid),
      output,
      "1",
    ]);

    expect(result.exitCode).toBe(1);
    expect(result.stderr.toString()).toContain("Target is not HDT");
    expect(existsSync(output)).toBe(false);
  } finally {
    rmSync(directory, { recursive: true });
  }
});

test.each(["0", "86401", "-1", "1; exit 0"])(
  "native capture rejects invalid duration %s before attaching",
  (duration) => {
    const result = Bun.spawnSync([
      "bash",
      script,
      String(process.pid),
      "/unused-output",
      duration,
    ]);

    expect(result.exitCode).toBe(2);
    expect(result.stderr.toString()).toContain("Usage:");
  }
);
