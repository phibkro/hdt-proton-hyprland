import { expect, test } from "bun:test";
import { mkdtemp, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

const root = path.resolve(import.meta.dir, "../..");

const host =
  process.env.HDT_HOST_BINARY ??
  path.join(
    process.env.CARGO_TARGET_DIR ?? path.join(root, "local/rust-target"),
    "debug/hdt-host"
  );

test("public library validation reaches the native validator without a game or configuration", async () => {
  const directory = await mkdtemp(path.join(tmpdir(), "hdt-libxi-cli-"));

  try {
    const library = path.join(directory, "libXi.so.6");
    const header = Buffer.alloc(64);
    header.set([0x7f, 0x45, 0x4c, 0x46, 2, 1, 1]);
    header.set([3, 0, 62, 0], 16);
    await writeFile(library, header);

    const run = async (arguments_: string[]) => {
      const child = Bun.spawn(
        [
          "bash",
          path.join(root, "scripts/hdt.sh"),
          "libxi-check",
          ...arguments_,
        ],
        {
          env: {
            ...process.env,
            HDT_HOST_BINARY: host,
            HDT_PROJECT_DIR: path.join(directory, "no-config"),
          },
          stdout: "pipe",
          stderr: "pipe",
        }
      );

      const [status, stdout, stderr] = await Promise.all([
        child.exited,
        new Response(child.stdout).text(),
        new Response(child.stderr).text(),
      ]);

      return { status, stdout, stderr };
    };

    const valid = await run([library]);
    expect(valid.status).toBe(0);
    expect(valid.stdout.trim()).toBe(await realpath(library));
    const relative = await run(["libXi.so.6"]);
    expect(relative.status).toBe(1);
    expect(relative.stderr).toContain("must be absolute");
    const missing = await run([]);
    expect(missing.status).toBe(1);
    expect(missing.stderr).toContain("Usage: hdt-host libxi-check LIBRARY");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
