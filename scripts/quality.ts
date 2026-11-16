import { mkdirSync } from "node:fs";
import path from "node:path";

process.chdir(path.resolve(import.meta.dir, ".."));

const [mode = "check", language = "all"] = process.argv.slice(2);

if (!new Set(["check", "fix"]).has(mode)) {
  throw new Error(
    "Usage: bun scripts/quality.ts [check|fix] [all|ts|types|c|lua|csharp|csharp-core|csharp-analysis|shell|rust|rust-fast|zig]"
  );
}

const run = (command: string[]) => {
  console.log(`> ${command.join(" ")}`);

  const child = Bun.spawnSync(command, {
    stdin: "inherit",
    stdout: "inherit",
    stderr: "inherit",
    env: {
      ...process.env,
      DOTNET_CLI_HOME: path.resolve("cache/dotnet"),
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
      CARGO_BUILD_JOBS: "2",
      ZIG_GLOBAL_CACHE_DIR: path.resolve("cache/zig"),
      ZIG_LOCAL_CACHE_DIR: path.resolve("local/analysis/zig"),
    },
  });

  if (child.exitCode !== 0) {
    process.exit(child.exitCode || 1);
  }
};

const sources = (extension: string) => {
  const found: string[] = [];

  for (const directory of ["src", "tests", "scripts"]) {
    for (const file of new Bun.Glob(`**/*.${extension}`).scanSync(directory)) {
      found.push(path.join(directory, file));
    }
  }

  return found.toSorted();
};

const cFlags = (file: string, linking: boolean) => {
  let libraries = ["x11"];

  if (file.endsWith("x-surface-probe.c")) {
    libraries = ["x11", "xcomposite", "xrender"];
  } else if (
    file.endsWith("xinput-race.c") ||
    file.endsWith("libxi-open-device.c")
  ) {
    libraries = ["x11", "xi"];
  }

  const result = Bun.spawnSync([
    "pkg-config",
    linking ? "--cflags" : "--cflags-only-I",
    ...(linking ? ["--libs"] : []),
    ...libraries,
  ]);

  if (result.exitCode !== 0) {
    throw new Error(
      `pkg-config failed for ${file}: ${result.stderr.toString()}`
    );
  }

  return result.stdout.toString().trim().split(/\s+/u).filter(Boolean);
};

const binary = (name: string) => path.resolve("node_modules/.bin", name);

switch (language) {
  case "all": {
    for (const item of [
      "ts",
      "types",
      "c",
      "lua",
      "csharp",
      "csharp-core",
      "shell",
      "rust",
    ]) {
      run([process.execPath, import.meta.filename, mode, item]);
    }

    break;
  }

  case "ts": {
    const paths = [
      "src",
      "tests",
      "scripts",
      "oxlint.config.ts",
      "oxfmt.config.ts",
      "package.json",
      "tsconfig.json",
    ];

    run([
      binary("oxlint"),
      "--deny-warnings",
      ...(mode === "fix" ? ["--fix"] : []),
      "src",
      "tests",
      "scripts",
      "oxlint.config.ts",
      "oxfmt.config.ts",
    ]);
    run([binary("oxfmt"), mode === "fix" ? "--write" : "--check", ...paths]);

    break;
  }

  case "types": {
    run([path.resolve("node_modules/.bin/tsgo"), "--noEmit"]);

    break;
  }

  case "c": {
    const files = sources("c");
    run([
      "clang-format",
      ...(mode === "fix" ? ["-i"] : ["--dry-run", "--Werror"]),
      ...files,
    ]);

    for (const file of files) {
      run([
        "clang-tidy",
        file,
        "--",
        "-std=c11",
        "-Wall",
        "-Wextra",
        "-Werror",
        ...cFlags(file, false),
      ]);
    }

    mkdirSync("local/analysis", { recursive: true });
    const probe = "tests/native/reference/x-surface-probe.c";
    const probePath = path.resolve("local/analysis/surface-probe");
    run([
      "clang",
      "-std=c11",
      "-Wall",
      "-Wextra",
      "-Werror",
      probe,
      "-o",
      probePath,
      ...cFlags(probe, true),
    ]);
    run([process.execPath, "tests/native/surface-arguments.ts", probePath]);

    break;
  }

  case "lua": {
    const files = sources("lua");
    run(["stylua", ...(mode === "check" ? ["--check"] : []), ...files]);
    run(["selene", ...files]);

    break;
  }

  case "csharp": {
    for (const directory of ["src", "tests"]) {
      run([
        "dotnet",
        "format",
        "whitespace",
        directory,
        "--folder",
        ...(mode === "check" ? ["--verify-no-changes"] : []),
      ]);
    }

    break;
  }

  case "csharp-core":
  case "csharp-analysis": {
    const app = process.env.HDT_APP_DIR;

    if (
      language === "csharp-analysis" &&
      (app === undefined || !path.isAbsolute(app))
    ) {
      throw new Error(
        "Set HDT_APP_DIR to the absolute path of a complete local HDT installation for C# semantic analysis."
      );
    }

    run([
      "dotnet",
      "restore",
      "tooling/net/HdtAnalysis.csproj",
      "--locked-mode",
    ]);
    run([
      "dotnet",
      "build",
      "tooling/net/HdtAnalysis.csproj",
      "--no-restore",
      `-p:AnalysisScope=${language === "csharp-core" ? "core" : "full"}`,
      ...(app === undefined ? [] : [`-p:HdtAppDir=${app}`]),
      `-p:ErrorLog=${path.resolve("local/analysis/csharp.sarif")}`,
      "-warnaserror",
    ]);

    break;
  }

  case "rust-fast": {
    run([
      "cargo",
      "fmt",
      "--manifest-path",
      "fuzz/Cargo.toml",
      "--all",
      ...(mode === "check" ? ["--", "--check"] : []),
    ]);
    run([
      "cargo",
      "fmt",
      "--all",
      ...(mode === "check" ? ["--", "--check"] : []),
    ]);
    run([
      "cargo",
      "clippy",
      "--locked",
      "--offline",
      "--all-targets",
      "--",
      "-D",
      "warnings",
    ]);

    break;
  }

  case "rust": {
    run([process.execPath, import.meta.filename, mode, "rust-fast"]);
    run([
      "cargo",
      "nextest",
      "run",
      "--locked",
      "--profile",
      "ci",
      "--config-file",
      "nextest.toml",
      "--test-threads",
      "2",
    ]);
    run(["cargo", "test", "--locked", "--doc"]);
    run(["cargo", "deny", "--config", "deny.toml", "check"]);
    run(["cargo", "machete"]);

    break;
  }

  case "shell": {
    const files = [
      "autostart.sh",
      ...sources("sh"),
      ".envrc",
      ".githooks/pre-commit",
    ];

    run(["shellcheck", ...files]);
    run(["shfmt", mode === "fix" ? "-w" : "-d", ...files]);

    break;
  }

  case "zig": {
    mkdirSync("local/analysis", { recursive: true });

    for (const file of sources("c")) {
      run([
        "zig",
        "cc",
        "-std=c11",
        "-Wall",
        "-Wextra",
        "-Werror",
        file,
        "-o",
        path.resolve("local/analysis", path.basename(file, ".c")),
        ...cFlags(file, true),
      ]);
    }

    break;
  }

  default: {
    throw new Error(`Unknown language: ${language}`);
  }
}
