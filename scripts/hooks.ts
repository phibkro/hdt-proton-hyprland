import { exec, main, root, run } from "./process";

main(() => {
  process.env.PRE_COMMIT_HOME = `${root}/cache/pre-commit`;

  if (process.argv[2] === "run") {
    exec(["pre-commit", "run", "--hook-stage", "pre-commit"]);
  }

  switch (process.argv[2]) {
    case "install": {
      // Local worktree settings must not replace another checkout's hooks.
      const current = Bun.spawnSync([
        "git",
        "config",
        "--get",
        "core.hooksPath",
      ]);

      const configured = current.stdout.toString().trim();

      if (configured && configured !== ".githooks") {
        throw new Error(
          `Existing hook configuration (${configured}) must be integrated before installation.`
        );
      }

      const worktree = Bun.spawnSync([
        "git",
        "config",
        "--local",
        "--get",
        "core.worktree",
      ]);

      if (worktree.exitCode === 0) {
        throw new Error(
          "Explicit core.worktree configuration requires manual review before enabling per-worktree hooks."
        );
      }

      run(["git", "config", "--local", "extensions.worktreeConfig", "true"]);
      run(["git", "config", "--worktree", "core.hooksPath", ".githooks"]);
      console.log(
        "Installed checks for this checkout. pre-commit hides unstaged tracked changes while checking the index."
      );

      break;
    }

    default: {
      throw new Error("Usage: bun scripts/hooks.ts [install|run]");
    }
  }
});
