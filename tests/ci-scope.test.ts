import { expect, test } from "bun:test";
import { mkdtemp, mkdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

const script = path.resolve(import.meta.dir, "../scripts/ci-scope.sh");

const git = (cwd: string, ...args: string[]) => {
  const result = Bun.spawnSync(
    ["git", "-c", "core.hooksPath=/dev/null", ...args],
    {
      cwd,
      env: {
        ...process.env,
        GIT_CONFIG_GLOBAL: "/dev/null",
        GIT_CONFIG_NOSYSTEM: "1",
        GIT_AUTHOR_NAME: "Fixture",
        GIT_AUTHOR_EMAIL: "fixture@example.invalid",
        GIT_COMMITTER_NAME: "Fixture",
        GIT_COMMITTER_EMAIL: "fixture@example.invalid",
      },
    }
  );

  expect(result.exitCode).toBe(0);

  return result.stdout.toString().trim();
};

const fixture = async (
  run: (cwd: string, base: string) => Promise<void> | void
) => {
  const cwd = await mkdtemp(path.join(tmpdir(), "hdt-ci-scope-"));

  try {
    git(cwd, "init", "--initial-branch=main");
    await Bun.write(path.join(cwd, "README.md"), "baseline\n");
    await mkdir(path.join(cwd, "src"));
    await Bun.write(path.join(cwd, "src/fixture.rs"), "original\n");
    git(cwd, "add", ".");
    git(cwd, "commit", "-m", "Baseline");
    await run(cwd, git(cwd, "rev-parse", "HEAD"));
  } finally {
    await rm(cwd, { recursive: true, force: true });
  }
};

const classify = (cwd: string, base: string, head = "HEAD") =>
  Bun.spawnSync(["bash", script, base, head], { cwd });

test.each([
  ["docs/nested/guide with spaces.md", "true"],
  ["avalonia/README.md", "true"],
  ["avalonia/Hdt.Avalonia/MainWindow.cs", "false"],
  ["docs/guide\nwith-newline.md", "true"],
  [".cursor/skills/verify-hdt-proton/features/launch.md", "true"],
  [".github/workflows/check.yml", "false"],
  ["tooling/rust/clippy.toml", "false"],
  ["docs/example.sh", "false"],
  ["src/fixture.rs", "false"],
])("classifies %s as documentation-only=%s", async (file, expected) => {
  await fixture(async (cwd, base) => {
    await Bun.write(path.join(cwd, file), "changed\n");
    git(cwd, "add", ".");
    git(cwd, "commit", "-m", "Change");
    const result = classify(cwd, base);
    expect(result.exitCode).toBe(0);
    expect(result.stdout.toString().trim()).toBe(expected);
  });
});

test("a documentation tip cannot hide code earlier in the PR", async () => {
  await fixture(async (cwd, base) => {
    await Bun.write(path.join(cwd, "src/fixture.rs"), "changed\n");
    git(cwd, "add", ".");
    git(cwd, "commit", "-m", "Code");
    await Bun.write(path.join(cwd, "README.md"), "documentation tip\n");
    git(cwd, "add", ".");
    git(cwd, "commit", "-m", "Docs");
    expect(classify(cwd, base).stdout.toString().trim()).toBe("false");
  });
});

test("moving code into documentation still requires full checks", async () => {
  await fixture(async (cwd, base) => {
    await mkdir(path.join(cwd, "docs"));
    git(cwd, "mv", "src/fixture.rs", "docs/fixture.md");
    git(cwd, "commit", "-m", "Move code");
    expect(classify(cwd, base).stdout.toString().trim()).toBe("false");
  });
});

test("missing base and empty changes request full checks; bad refs fail", async () => {
  await fixture((cwd, base) => {
    expect(classify(cwd, "").stdout.toString().trim()).toBe("false");
    expect(
      classify(cwd, "0000000000000000000000000000000000000000")
        .stdout.toString()
        .trim()
    ).toBe("false");
    expect(classify(cwd, base).stdout.toString().trim()).toBe("false");
    expect(classify(cwd, "missing-ref").exitCode).not.toBe(0);
  });
});
