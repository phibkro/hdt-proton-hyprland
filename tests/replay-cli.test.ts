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
import { join, resolve } from "node:path";

const root = resolve(import.meta.dir, "..");

test("replay preserves namespace arguments and a failing supervisor result without touching a game", async () => {
  const temp = await mkdtemp(join(tmpdir(), "hdt-replay-boundary-"));

  try {
    for (const dir of [
      "scripts",
      "src",
      "tests",
      "tools",
      "local/replay/seed/prefix",
      "local/replay/seed/app",
    ]) {
      await mkdir(join(temp, dir), { recursive: true });
    }
    for (const name of ["process.ts", "replay.ts"]) {
      await copyFile(join(root, "scripts", name), join(temp, "scripts", name));
    }
    await Bun.write(join(temp, "src/fixture.txt"), "source snapshot\n");
    const sourcePrefix = join(temp, "real-game-prefix");
    await Bun.write(
      join(temp, "local/replay/ownership.json"),
      JSON.stringify({
        schema: 2,
        kind: "hdt-ui-replay",
        project: temp,
        proton: join(temp, "proton"),
        sourcePrefix,
      })
    );
    const argsFile = join(temp, "arguments.json");
    await Bun.write(
      join(temp, "tools/git"),
      '#!/bin/sh\ncase "$*" in *rev-parse*) printf "%s\\n" fixture-revision;; esac\n'
    );
    await Bun.write(
      join(temp, "tools/bwrap"),
      `#!/bin/sh\nexec "$TEST_BUN" "$TEST_CAPTURE" "$@"\n`
    );
    await Bun.write(
      join(temp, "capture.ts"),
      "await Bun.write(process.env.TEST_ARGV!, JSON.stringify(process.argv.slice(2))); process.exit(17);"
    );

    for (const name of ["git", "bwrap"]) {
      await chmod(join(temp, "tools", name), 0o700);
    }
    const child = Bun.spawn(
      [
        process.execPath,
        join(temp, "scripts/replay.ts"),
        "run",
        "--with-plugin",
        "--without-particles",
      ],
      {
        env: {
          ...process.env,
          PATH: `${temp}/tools:${process.env.PATH}`,
          TEST_BUN: process.execPath,
          TEST_CAPTURE: join(temp, "capture.ts"),
          TEST_ARGV: argsFile,
        },
        stdout: "pipe",
        stderr: "pipe",
      }
    );

    const status = await child.exited;

    if (status !== 17) {
      throw Error(
        `Replay boundary fixture exited ${status}: ${await new Response(child.stderr).text()}`
      );
    }
    expect(status).toBe(17);
    const args: string[] = JSON.parse(await readFile(argsFile, "utf-8"));

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
      arg.endsWith("/scripts/replay-inside.ts")
    );

    expect(args[inside - 1]).toBe(process.execPath);
    expect(args.slice(inside + 4)).toEqual([
      "false",
      "true",
      "mixed",
      "false",
      "false",
      "false",
      "false",
    ]);
    const runs = await readdir(join(temp, "local/replay/runs"));
    expect(runs.length).toBe(1);

    const result = JSON.parse(
      await readFile(
        join(temp, "local/replay/runs", runs[0], "result.json"),
        "utf-8"
      )
    );

    expect(result.exitStatus).toBe(17);
    expect(result.sourceRevision).toBe("fixture-revision");
    expect(result.sourceManifestSHA256).toMatch(/^[0-9a-f]{64}$/);
    expect(result.particles).toBe(false);
    expect(await Bun.file(join(sourcePrefix, "system.reg")).exists()).toBe(
      false
    );
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
