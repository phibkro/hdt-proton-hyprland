import {
  mkdirSync,
  chmodSync,
  existsSync,
  readFileSync,
  writeFileSync,
  renameSync,
  realpathSync,
  openSync,
  closeSync,
} from "node:fs";
import { join } from "node:path";

import { main, root, run, lock, read, CommandFailure } from "./process";

const lab = join(root, "local/replay");

const usage =
  "Usage: hdt-replay prepare --proton DIR --runtime DIR [--reuse] | run [--inject-failure] [--with-plugin] [--plaque-level 0..5] [--without-particles] [--battlegrounds-tabs] [--diagnostics] [--canonical-events]";

const fail = (message: string): never => {
  throw new Error(`HDT replay: ${message}`);
};

const assert = (condition: unknown, message: string): void => {
  if (!condition) {
    fail(message);
  }
};

const hash = (path: string) =>
  new Bun.CryptoHasher("sha256").update(readFileSync(path)).digest("hex");

const json = (path: string) => JSON.parse(read(path));

const equal = (left: string, right: string, message: string) => {
  assert(readFileSync(left).equals(readFileSync(right)), message);
};

function privateDirectory(path: string) {
  mkdirSync(path, { recursive: true, mode: 0o700 });
  chmodSync(path, 0o700);
}

main(async () => {
  const args = process.argv.slice(2);
  const action = args.shift() ?? "help";

  if (!["prepare", "run"].includes(action)) {
    console.log(usage);

    return;
  }

  privateDirectory(lab);
  const ownership = lock(join(lab, ".lock"));

  if (!ownership.acquired) {
    ownership.close();
    fail("Another replay command owns the environment.");
  }

  try {
    const manifest = join(lab, "ownership.json");
    const seed = join(lab, "seed");

    if (action === "prepare") {
      let proton = "",
        reuse = false,
        runtime = "";

      while (args.length) {
        const option = args.shift();

        if (option === "--reuse") {
          reuse = true;
        } else if (["--proton", "--runtime"].includes(option!)) {
          const value = args.shift();
          assert(value, "Missing directory argument.");

          if (option === "--proton") {
            proton = realpathSync(value!);
          } else {
            runtime = realpathSync(value!);
          }
        } else {
          console.error(usage);
          process.exit(2);
        }
      }

      run(["test", "-x", join(proton, "files/bin/wine")]);
      run(["test", "-x", join(runtime, "run")]);

      const { prefix: sourcePrefix, appDirectory } =
        await import("../src/runtime/config");

      const sourceApp = realpathSync(appDirectory);
      const clr =
        "drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/clr.dll";

      assert(
        existsSync(join(sourcePrefix, clr)),
        "Native .NET is required in the source prefix."
      );

      const platform = /^dir=(steamrt[a-zA-Z0-9_.-]*)$/m.exec(
        read(join(runtime, "run"))
      )?.[1];

      assert(
        platform && existsSync(join(runtime, platform)),
        "Unsupported Steam runtime layout."
      );

      const copyLogged = (argv: string[]) => {
        const log = join(lab, "prepare.log");
        const child = Bun.spawnSync(argv, { stdout: "pipe", stderr: "pipe" });
        writeFileSync(log, Buffer.concat([child.stdout, child.stderr]), {
          flag: "a",
        });

        if (child.exitCode !== 0) {
          fail(`Copy failed; inspect ${lab}/prepare.log`);
        }
      };

      if (existsSync(seed)) {
        assert(
          reuse && existsSync(manifest),
          "Seed exists. Use --reuse to verify and adopt this project-owned seed."
        );
        const owner = json(manifest);
        assert(
          owner.kind === "hdt-ui-replay" && owner.project === root,
          "Seed ownership does not match this project."
        );
      } else {
        privateDirectory(join(seed, "prefix/drive_c"));
        privateDirectory(join(seed, "app"));
        copyLogged([
          "cp",
          "-a",
          "--reflink=auto",
          join(sourcePrefix, "drive_c/windows"),
          join(seed, "prefix/drive_c/"),
        ]);

        for (const file of ["system.reg", "userdef.reg"]) {
          const before = hash(join(sourcePrefix, file));
          run([
            "cp",
            "--reflink=auto",
            join(sourcePrefix, file),
            join(seed, "prefix", file),
          ]);
          assert(
            before === hash(join(sourcePrefix, file)),
            "Source registry changed during copy. Preparation is incomplete."
          );
        }

        run([
          "cp",
          "-a",
          join(sourcePrefix, ".update-timestamp"),
          join(seed, "prefix/.update-timestamp"),
        ]);
        let keep = true;

        const winePreferences = (
          readFileSync(join(sourcePrefix, "user.reg"), "utf-8").match(
            /[^\n]*\n|[^\n]+$/g
          ) ?? []
        )
          .filter((line) => {
            if (line.startsWith("[")) {
              keep = line.startsWith("[Software\\\\Wine");
            }
            return keep;
          })
          .join("");

        writeFileSync(join(seed, "prefix/user.reg"), winePreferences);
        privateDirectory(join(seed, "prefix/dosdevices"));
        privateDirectory(join(seed, "prefix/drive_c/users/steamuser"));
        run(["ln", "-s", "../drive_c", join(seed, "prefix/dosdevices/c:")]);
        run(["ln", "-s", "/", join(seed, "prefix/dosdevices/z:")]);
        run([
          "rsync",
          "-a",
          "--exclude=/Plugins",
          "--exclude=/Crash Reports",
          "--exclude=*.log",
          `${sourceApp}/`,
          `${seed}/app/`,
        ]);
      }

      equal(
        join(sourcePrefix, clr),
        join(seed, "prefix", clr),
        "Seed CLR differs from the selected source."
      );
      assert(
        existsSync(join(seed, "prefix/.update-timestamp")),
        "Seed is missing its Wine initialization marker."
      );
      equal(
        join(sourceApp, "HearthstoneDeckTracker.exe"),
        join(seed, "app/HearthstoneDeckTracker.exe"),
        "Seed HDT differs from the selected source."
      );

      if (!existsSync(join(lab, "runtime"))) {
        privateDirectory(join(lab, "runtime"));

        for (const file of ["run", "pressure-vessel", platform!]) {
          copyLogged([
            "cp",
            "-a",
            "--reflink=auto",
            join(runtime, file),
            `${lab}/runtime/`,
          ]);
        }
      }

      equal(
        join(runtime, "run"),
        join(lab, "runtime/run"),
        "Private runtime differs from the selected runtime."
      );
      writeFileSync(
        `${manifest}.next`,
        JSON.stringify({
          schema: 2,
          kind: "hdt-ui-replay",
          project: root,
          sourcePrefix,
          proton,
          runtime,
          hdtSHA256: hash(join(seed, "app/HearthstoneDeckTracker.exe")),
        })
      );
      renameSync(`${manifest}.next`, manifest);
      console.log(
        "Prepared isolated HDT seed. No game or HDT account data copied."
      );

      return;
    }

    let bgs = false,
      canonical = false,
      diagnostics = false,
      expected = false,
      level = "mixed",
      particles = true,
      plugin = false;

    while (args.length) {
      switch (args.shift()) {
        case "--inject-failure": {
          expected = true;
          break;
        }
        case "--with-plugin": {
          plugin = true;
          break;
        }
        case "--battlegrounds-tabs": {
          bgs = true;
          break;
        }
        case "--diagnostics": {
          diagnostics = true;
          break;
        }
        case "--canonical-events": {
          canonical = true;
          break;
        }
        case "--without-particles": {
          particles = false;
          break;
        }
        case "--plaque-level": {
          level = args.shift() ?? "";
          assert(/^[0-5]$/.test(level), "Plaque level must be 0..5.");
          break;
        }
        default: {
          console.error(usage);
          process.exit(2);
        }
      }
    }

    assert(!diagnostics || plugin, "--diagnostics requires --with-plugin.");
    assert(existsSync(manifest), "Run prepare first.");
    const owner = json(manifest);
    assert(
      owner.schema === 2 &&
        owner.kind === "hdt-ui-replay" &&
        owner.project === root,
      "Invalid environment ownership."
    );

    const { proton } = owner,
      { sourcePrefix } = owner;

    assert(
      typeof proton === "string" && typeof sourcePrefix === "string",
      "Invalid environment paths."
    );

    const revision = run(["git", "-C", root, "rev-parse", "HEAD"], {
      capture: true,
    });

    const dirty =
      run(["git", "-C", root, "status", "--porcelain"], { capture: true }) !==
      "";

    const stamp = new Date()
      .toISOString()
      .replaceAll(/[-:]/g, "")
      .replace(/\.\d+Z$/, "");

    const capture = join(lab, "runs", `${stamp}-${process.pid}`);

    for (const part of ["home", "runtime", "source"]) {
      privateDirectory(join(capture, part));
    }
    run([
      "cp",
      "-R",
      join(root, "src"),
      join(root, "tests"),
      join(root, "scripts"),
      `${capture}/source/`,
    ]);

    const files = run(["rg", "--files", "--hidden", "--no-ignore", "-0"], {
      capture: true,
      cwd: join(capture, "source"),
    })
      .split("\0")
      .filter(Boolean)
      .sort((a, b) => Buffer.compare(Buffer.from(a), Buffer.from(b)));

    writeFileSync(
      join(capture, "source-sha256.txt"),
      files
        .map((file) => `${hash(join(capture, "source", file))}  ${file}\n`)
        .join("")
    );
    const snapshot = hash(join(capture, "source-sha256.txt"));

    for (const part of ["prefix", "app"]) {
      run([
        "cp",
        "-a",
        "--reflink=auto",
        join(seed, part),
        join(capture, part),
      ]);
    }
    assert(
      realpathSync(join(capture, "prefix")) === join(capture, "prefix") &&
        join(capture, "prefix") !== sourcePrefix,
      "Unsafe replay prefix."
    );
    writeFileSync(
      join(capture, "ownership.json"),
      JSON.stringify({ kind: "hdt-ui-replay-run", run: capture, project: root })
    );

    const argv = [
      "timeout",
      "120",
      "bwrap",
      "--die-with-parent",
      "--unshare-net",
      "--unshare-pid",
    ];

    for (const path of [
      "/nix/store",
      "/etc",
      "/bin",
      "/usr",
      "/lib64",
      "/sys",
      "/run/current-system",
      "/run/opengl-driver",
      root,
    ]) {
      argv.push("--ro-bind", path, path);
    }
    argv.push(
      "--proc",
      "/proc",
      "--dev",
      "/dev",
      "--tmpfs",
      "/dev/shm",
      "--dev-bind",
      "/dev/dri",
      "/dev/dri",
      "--bind",
      lab,
      lab,
      "--ro-bind",
      seed,
      seed,
      "--ro-bind",
      `${capture}/source`,
      `${capture}/source`,
      "--tmpfs",
      process.env.HOME!,
      "--ro-bind",
      proton,
      proton,
      "--tmpfs",
      sourcePrefix,
      "--tmpfs",
      "/run/user",
      "--tmpfs",
      "/tmp",
      "--clearenv"
    );

    for (const [key, value] of Object.entries({
      PATH: process.env.PATH!,
      HDT_REPLAY_TOOLS_PATH: process.env.PATH!,
      HOME: `${capture}/home`,
      USER: "steamuser",
      LANG: "C.UTF-8",
      PRESSURE_VESSEL_VARIABLE_DIR: `${capture}/pv-var`,
    })) {
      argv.push("--setenv", key, value);
    }
    argv.push(
      "steam-run",
      `${lab}/runtime/run`,
      "--",
      process.execPath,
      `${capture}/source/scripts/replay-inside.ts`,
      root,
      capture,
      proton,
      ...[expected, plugin, level, particles, bgs, diagnostics, canonical].map(
        String
      )
    );
    const log = openSync(join(capture, "supervisor.log"), "w", 0o600);
    let child: ReturnType<typeof Bun.spawn>;

    try {
      child = Bun.spawn(argv, { stdout: log, stderr: log });
    } finally {
      closeSync(log);
    }

    const status = await child.exited;
    writeFileSync(
      join(capture, "result.json"),
      JSON.stringify(
        {
          exitStatus: status,
          negativeAssertionTest: expected,
          compatibilityPlugin: plugin,
          plaqueLevel: level,
          particles,
          battlegroundsTabs: bgs,
          diagnostics,
          canonicalEvents: canonical,
          evidence: capture,
          sourceRevision: revision,
          sourceDirtyAtStart: dirty,
          sourceManifestSHA256: snapshot,
        },
        null,
        2
      )
    );
    console.log(`Replay exit status: ${status}. Evidence: ${capture}`);

    if (status !== 0) {
      throw new CommandFailure(status, "isolated replay");
    }
  } finally {
    ownership.close();
  }
});
