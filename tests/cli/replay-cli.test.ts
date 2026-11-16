/* eslint-disable no-await-in-loop -- Fixture setup serializes file creation before launching the subprocess under test. */
import { test, expect } from "bun:test";
import {
  mkdtemp,
  mkdir,
  chmod,
  copyFile,
  readFile,
  readdir,
  rm,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import nodePath from "node:path";

const root = nodePath.resolve(import.meta.dir, "../..");

test("replay preserves namespace arguments and a failing supervisor result without touching a game", async () => {
  const temp = await mkdtemp(nodePath.join(tmpdir(), "hdt-replay-boundary-"));

  try {
    for (const dir of [
      "scripts",
      "src",
      "tests",
      "tools",
      "local/replay/seed/prefix",
      "local/replay/seed/app",
    ]) {
      await mkdir(nodePath.join(temp, dir), { recursive: true });
    }

    for (const name of ["replay.sh"]) {
      await copyFile(
        nodePath.join(root, "scripts", name),
        nodePath.join(temp, "scripts", name)
      );
    }

    await Bun.write(
      nodePath.join(temp, "src/fixture.txt"),
      "source snapshot\n"
    );
    const sourcePrefix = nodePath.join(temp, "real-game-prefix");
    await Bun.write(
      nodePath.join(temp, "local/replay/ownership.json"),
      JSON.stringify({
        schema: 2,
        kind: "hdt-ui-replay",
        project: temp,
        proton: nodePath.join(temp, "proton"),
        sourcePrefix,
      })
    );
    const argsFile = nodePath.join(temp, "arguments.json");
    await Bun.write(
      nodePath.join(temp, "tools/git"),
      '#!/bin/sh\ncase "$*" in *rev-parse*) printf "%s\\n" fixture-revision;; esac\n'
    );
    await Bun.write(
      nodePath.join(temp, "tools/bwrap"),
      `#!/bin/sh\nexec "$TEST_BUN" "$TEST_CAPTURE" "$@"\n`
    );
    await Bun.write(
      nodePath.join(temp, "capture.ts"),
      "await Bun.write(process.env.TEST_ARGV!, JSON.stringify(process.argv.slice(2))); process.exit(17);"
    );

    for (const name of ["git", "bwrap"]) {
      await chmod(nodePath.join(temp, "tools", name), 0o700);
    }

    const child = Bun.spawn(
      [
        "bash",
        nodePath.join(temp, "scripts/replay.sh"),
        "run",
        "--with-plugin",
        "--without-particles",
      ],
      {
        env: {
          ...process.env,
          PATH: `${temp}/tools:${process.env.PATH}`,
          TEST_BUN: process.execPath,
          HDT_HOST_BINARY:
            process.env.HDT_HOST_BINARY ??
            nodePath.join(
              process.env.CARGO_TARGET_DIR ?? nodePath.join(root, "target"),
              "debug/hdt-host"
            ),
          TEST_CAPTURE: nodePath.join(temp, "capture.ts"),
          TEST_ARGV: argsFile,
        },
        stdout: "pipe",
        stderr: "pipe",
      }
    );

    const status = await child.exited;

    if (status !== 17) {
      throw new Error(
        `Replay boundary fixture exited ${status}: ${await new Response(child.stderr).text()}`
      );
    }

    expect(status).toBe(17);
    const captured: unknown = JSON.parse(await readFile(argsFile, "utf-8"));

    if (
      !Array.isArray(captured) ||
      !captured.every(
        (value: unknown): value is string => typeof value === "string"
      )
    ) {
      throw new TypeError("Captured supervisor arguments must be strings");
    }

    const args = captured;

    for (const required of [
      "--die-with-parent",
      "--unshare-net",
      "--unshare-pid",
      "--clearenv",
    ]) {
      expect(args).toContain(required);
    }

    expect(
      args.some(
        (arg, index) => arg === "--tmpfs" && args[index + 1] === sourcePrefix
      )
    ).toBe(true);
    expect(
      args.some(
        (arg, index) =>
          arg === "--setenv" && args[index + 1] === "HDT_REPLAY_TOOLS_PATH"
      )
    ).toBe(true);

    const inside = args.findIndex((arg) =>
      arg.endsWith("/scripts/replay-inside.sh")
    );

    expect(args[inside - 1]).toBe("bash");
    expect(args.slice(inside + 4)).toEqual([
      "false",
      "true",
      "mixed",
      "false",
      "false",
      "false",
      "false",
      "false",
    ]);
    const runs = await readdir(nodePath.join(temp, "local/replay/runs"));
    expect(runs.length).toBe(1);

    const result: unknown = JSON.parse(
      await readFile(
        nodePath.join(temp, "local/replay/runs", runs[0], "result.json"),
        "utf-8"
      )
    );

    /* eslint-disable anti-slop/no-runtime-typeof -- Validate untrusted JSON evidence at its filesystem boundary before accessing fields. */
    if (
      typeof result !== "object" ||
      result === null ||
      !("exitStatus" in result) ||
      !("sourceRevision" in result) ||
      !("sourceManifestSHA256" in result) ||
      !("particles" in result)
    ) {
      throw new TypeError("Replay result is missing required evidence fields");
    }

    /* eslint-enable anti-slop/no-runtime-typeof */

    expect(result.exitStatus).toBe(17);
    expect(result.sourceRevision).toBe("fixture-revision");
    expect(result.sourceManifestSHA256).toMatch(/^[0-9a-f]{64}$/u);
    expect(result.particles).toBe(false);
    expect(
      await Bun.file(nodePath.join(sourcePrefix, "system.reg")).exists()
    ).toBe(false);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});

test("canonical replay cannot silently bypass requested diagnostics", async () => {
  const temp = await mkdtemp(nodePath.join(tmpdir(), "hdt-replay-conflict-"));

  try {
    await mkdir(nodePath.join(temp, "scripts"));
    await copyFile(
      nodePath.join(root, "scripts/replay.sh"),
      nodePath.join(temp, "scripts/replay.sh")
    );

    const child = Bun.spawn(
      [
        "bash",
        nodePath.join(temp, "scripts/replay.sh"),
        "run",
        "--with-plugin",
        "--canonical-events",
        "--diagnostics",
      ],
      { stdout: "pipe", stderr: "pipe" }
    );

    const status = await child.exited;
    const stderr = await new Response(child.stderr).text();

    expect(status).toBe(1);
    expect(stderr).toContain("require separate replay runs");
    expect(
      await Bun.file(
        nodePath.join(temp, "local/replay/ownership.json")
      ).exists()
    ).toBe(false);
    expect(await readdir(nodePath.join(temp, "local/replay"))).toEqual([
      ".lock",
    ]);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
