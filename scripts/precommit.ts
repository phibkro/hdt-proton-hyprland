import path from "node:path";

import { run } from "./process";

// pre-commit supplies staged filenames and temporarily hides unstaged tracked
// edits. Do not rewrite files or perform dependency installation in this hook.
const files = process.argv.slice(2);

const owned = files.filter((file) =>
  /^(?:src|tests|scripts|tooling)\//u.test(file)
);

const typescript = files.filter(
  (file) =>
    (owned.includes(file) || file.endsWith(".config.ts")) &&
    file.endsWith(".ts")
);

const c = owned.filter((file) => file.endsWith(".c"));
const lua = owned.filter((file) => file.endsWith(".lua"));
const binary = (name: string) => path.resolve("node_modules/.bin", name);

const configs = files.filter(
  (file) =>
    [
      "package.json",
      "tsconfig.json",
      "hdt.local.example.json",
      ".pre-commit-config.yaml",
    ].includes(file) || /^\.github\/workflows\/[^/]+\.ya?ml$/u.test(file)
);

if (configs.length > 0) {
  run([binary("oxfmt"), "--check", ...configs]);
}

if (typescript.length > 0) {
  run([binary("oxfmt"), "--check", ...typescript]);
  run([binary("oxlint"), "--deny-warnings", ...typescript]);
  run([binary("tsgo"), "--noEmit"]);
}

if (c.length > 0) {
  run(["clang-format", "--dry-run", "--Werror", ...c]);

  const flags = run(
    ["pkg-config", "--cflags", "x11", "xi", "xcomposite", "xrender"],
    { capture: true }
  )
    .split(/\s+/u)
    .filter(Boolean);

  // Compile-only mode also tells Nix compiler wrappers to omit linker flags.
  for (const file of c) {
    run([
      "clang",
      "-std=c11",
      "-Wall",
      "-Wextra",
      "-Werror",
      "-c",
      file,
      "-o",
      "/dev/null",
      ...flags,
    ]);
  }
}

if (lua.length > 0) {
  run(["stylua", "--check", ...lua]);
  run(["selene", ...lua]);
}

const workflows = files.filter((file) =>
  /^\.github\/workflows\/[^/]+\.ya?ml$/u.test(file)
);

if (workflows.length > 0) {
  run(["actionlint", ...workflows]);
}

const shell = files.filter(
  (file) =>
    file.endsWith(".sh") || file === ".githooks/pre-commit" || file === ".envrc"
);

if (shell.length > 0) {
  run(["shellcheck", ...shell]);
  run(["shfmt", "-d", ...shell]);
}

for (const directory of ["src", "tests"]) {
  const csharp = owned.filter(
    (file) => file.startsWith(`${directory}/`) && file.endsWith(".cs")
  );

  if (csharp.length > 0) {
    run([
      "dotnet",
      "format",
      "whitespace",
      directory,
      "--folder",
      "--verify-no-changes",
      "--include",
      ...csharp.map((file) => path.resolve(file)),
    ]);
  }
}

if (
  files.some(
    (file) =>
      file.endsWith(".rs") ||
      [
        "Cargo.toml",
        "Cargo.lock",
        "rustfmt.toml",
        "clippy.toml",
        "nextest.toml",
        "deny.toml",
      ].includes(file) ||
      file.startsWith("tooling/rust/")
  )
) {
  run([process.execPath, "scripts/quality.ts", "check", "rust-fast"]);
}
