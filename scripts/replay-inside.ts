import {
  existsSync,
  readFileSync,
  writeFileSync,
  copyFileSync,
  renameSync,
  unlinkSync,
  readdirSync,
  readlinkSync,
  openSync,
  closeSync,
} from "node:fs";
import { join, dirname } from "node:path";

import { main, run as command, read } from "./process";

main(async () => {
  const [
    root,
    capture,
    proton,
    negative,
    plugin,
    level,
    particles,
    bgs = "false",
    diagnostics = "false",
    canonical = "false",
  ] = process.argv.slice(2);

  const snapshot = join(capture, "source");
  const app = join(capture, "app");

  const file = (name: string) => join(capture, name);

  const assert = (condition: unknown, reason: string): void => {
    if (!condition) {
      throw Error(reason);
    }
  };

  const json = (name: string) => JSON.parse(read(file(name)));

  const save = (name: string, value: unknown) => {
    writeFileSync(file(name), JSON.stringify(value));
  };

  const run = (argv: string[], captureOutput = false) =>
    command(argv, { capture: captureOutput, cwd: app });

  const check = (query: string, name: string) =>
    run(["jq", "-e", query, file(name)], true);

  const rows = (name: string) =>
    read(file(name))
      .split("\n")
      .filter(Boolean)
      .map((line) => JSON.parse(line));

  const waitFor = async (
    predicate: () => boolean,
    attempts: number,
    delay = 100
  ) => {
    for (let attempt = 0; attempt < attempts; attempt++) {
      if (predicate()) {
        return;
      }
      await Bun.sleep(delay);
    }

    throw new Error("Timed out awaiting isolated replay state");
  };

  const stateExists = () =>
    existsSync(file("state.json")) &&
    readFileSync(file("state.json")).length > 0;

  const logChild = (argv: string[], log: string) => {
    const fd = openSync(file(log), "w", 0o600);

    try {
      return Bun.spawn(argv, {
        cwd: app,
        env: process.env,
        stdin: "ignore",
        stdout: fd,
        stderr: fd,
      });
    } finally {
      closeSync(fd);
    }
  };

  const winpath = (path: string) => `Z:${path.replaceAll("/", "\\")}`;
  const wine = join(proton, "files/bin/wine");

  const compile = async (args: string[], log: string, limit = "35") => {
    const child = logChild(
      ["timeout", "--kill-after=3s", limit, wine, ...args],
      log
    );

    assert((await child.exited) === 0, `Wine command failed; inspect ${log}`);
  };

  assert(process.env.HDT_REPLAY_TOOLS_PATH, "Replay tool path missing");
  process.env.PATH = process.env.HDT_REPLAY_TOOLS_PATH;
  assert(
    capture.startsWith(`${root}/local/replay/runs/`),
    "Replay path outside owned lab"
  );
  assert(
    json("ownership.json").kind === "hdt-ui-replay-run" &&
      json("ownership.json").run === capture,
    "Invalid run ownership"
  );
  assert(
    !process.env.DISPLAY && !process.env.WAYLAND_DISPLAY,
    "Host desktop environment leaked into replay"
  );

  const { sourcePrefix } = JSON.parse(
    read(join(root, "local/replay/ownership.json"))
  );

  assert(
    !existsSync(join(sourcePrefix, "system.reg")),
    "Source prefix is visible"
  );

  const interfaces = read("/proc/net/dev")
    .split("\n")
    .slice(2)
    .map((line) => line.split(":")[0].trim())
    .filter(Boolean);

  assert(
    interfaces.every((name) => name === "lo"),
    "Unexpected network interface"
  );

  for (const tool of [
    "Xvfb",
    "openbox",
    "xcompmgr",
    "xdotool",
    "xwininfo",
    "magick",
  ]) {
    assert(
      Bun.which(tool, { PATH: process.env.PATH }),
      `Tool missing: ${tool}`
    );
  }
  Object.assign(process.env, {
    WINEPREFIX: file("prefix"),
    XDG_RUNTIME_DIR: file("runtime"),
    WINEDLLOVERRIDES: "mscoree=n,b;d3d9=n",
    WINEDEBUG: "-all",
    DXVK_CONFIG: "d3d9.shaderModel = 1",
    DXVK_LOG_PATH: capture,
  });

  // stdout is the displayfd sink; no shell redirection or global X server.
  const xvfb = Bun.spawn(
    [
      "Xvfb",
      "-displayfd",
      "1",
      "-screen",
      "0",
      "2560x1440x24",
      "-nolisten",
      "tcp",
      "-ac",
    ],
    {
      env: process.env,
      stdout: Bun.file(file("display")),
      stderr: Bun.file(file("xvfb.log")),
    }
  );

  const children: ReturnType<typeof Bun.spawn>[] = [xvfb];
  let journal: ReturnType<typeof Bun.spawn> | undefined;
  let failed = true;

  try {
    await waitFor(
      () => existsSync(file("display")) && read(file("display")) !== "",
      50
    );
    assert(xvfb.exitCode === null, "Private X server exited");
    process.env.DISPLAY = `:${read(file("display"))}`;
    children.push(
      logChild(["openbox"], "openbox.log"),
      logChild(["xcompmgr"], "compositor.log")
    );
    save("isolation.json", {
      networkNamespace: readlinkSync("/proc/self/ns/net"),
      pidNamespace: readlinkSync("/proc/self/ns/pid"),
      sourcePrefixHidden: true,
      hostDesktopVariablesAbsent: true,
    });
    copyFileSync(
      join(app, "HearthstoneDeckTracker.exe.config"),
      join(app, "ReplayHost.exe.config")
    );
    const wpf = "C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\WPF";
    const csc = "C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";

    const wpfReferences = [
      "PresentationCore",
      "PresentationFramework",
      "WindowsBase",
    ].map((name) => `/r:${wpf}\\${name}.dll`);

    if (plugin === "true") {
      const pluginSources = readdirSync(join(snapshot, "src/plugin"))
        .filter((name) => name.endsWith(".cs"))
        .sort()
        .map((name) => winpath(join(snapshot, "src/plugin", name)));

      await compile(
        [
          csc,
          "/nologo",
          "/target:library",
          "/platform:x64",
          "/out:ProtonCompatibility.dll",
          ...[
            "HearthstoneDeckTracker.exe",
            "HearthMirror.dll",
            "MahApps.Metro.dll",
            "System.Drawing.dll",
            "System.Xaml.dll",
            "System.Web.Extensions.dll",
          ].map((name) => `/r:${name}`),
          ...wpfReferences,
          ...pluginSources,
        ],
        "plugin-compile.log"
      );
      Object.assign(process.env, {
        HDT_ALPHA_FLOOR: "1",
        HDT_INPUT_SESSION: "isolated-replay",
        HDT_INPUT_REGION_FILE: file("plugin-input.json"),
      });
    }

    if (diagnostics === "true") {
      await compile(
        [
          csc,
          "/nologo",
          "/target:exe",
          "/platform:x64",
          "/out:FlightRecorderChecks.exe",
          "/r:System.Web.Extensions.dll",
          winpath(join(snapshot, "tests/FlightRecorderChecks.cs")),
          winpath(join(snapshot, "src/plugin/FlightRecorder.cs")),
        ],
        "recorder-compile.log"
      );
      await compile(
        [
          join(app, "FlightRecorderChecks.exe"),
          winpath(file("recorder-checks")),
        ],
        "recorder-checks.log",
        "10"
      );
    }

    await compile(
      [
        csc,
        "/nologo",
        "/target:winexe",
        "/platform:x64",
        "/out:ReplayHost.exe",
        "/r:System.Xaml.dll",
        "/r:System.Web.Extensions.dll",
        "/r:Newtonsoft.Json.dll",
        ...wpfReferences,
        winpath(join(snapshot, "src/replay/ReplayHost.cs")),
        winpath(join(snapshot, "src/replay/CanonicalReplay.cs")),
      ],
      "compile.log"
    );

    const startHost = (log: string, limit: string, hostLog: string) => {
      const child = logChild(
        [
          "timeout",
          limit,
          wine,
          join(app, "ReplayHost.exe"),
          winpath(file(hostLog)),
          plugin,
          level,
          particles,
        ],
        log
      );

      children.push(child);

      return child;
    };

    let host = startHost("wine.log", "70", "host.log");
    await waitFor(stateExists, 200);
    writeFileSync(
      file("windows.txt"),
      run(["xwininfo", "-root", "-tree"], true)
    );

    const screenshot = (name: string) =>
      run(["magick", "import", "-window", "root", file(name)]);

    screenshot("startup.png");
    check(".settingsButton!=null and .commandId==0", "state.json");
    let id = 0;

    const send = async (op: string) => {
      save("command.json.next", { id: ++id, op });
      renameSync(file("command.json.next"), file("command.json"));
      await waitFor(
        () => stateExists() && json("state.json").commandId === id,
        50
      );
    };

    const settings = async () => {
      const button = json("state.json").settingsButton;
      run([
        "xdotool",
        "mousemove",
        "--sync",
        String(button.x),
        String(button.y),
        "click",
        "1",
      ]);
      await waitFor(() => json("state.json").settingsOpen === true, 30);
      copyFileSync(file("state.json"), file(`settings-${id}.json`));
      await send("settings-close");
    };

    if (canonical === "true") {
      writeFileSync(file("canonical-input.json"), "");
      journal = logChild(
        [
          "bun",
          join(snapshot, "src/diagnostics/input-journal.ts"),
          "--output",
          file("journal"),
          "--from",
          "start",
          "--max-bytes",
          "8388608",
          "--source",
          `input=${file("canonical-input.json")}`,
          "--source",
          `hdt=${file("canonical.jsonl")}`,
        ],
        "journal.log"
      );
      await send("canonical-start");
      const modes = ["DRAFT", "HUB", "BACON", "HUB"];
      save(
        "canonical-input.json",
        modes.map((mode, index) => ({
          ns: "LoadingScreen",
          line: `D 00:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=${index ? modes[index - 1] : "HUB"} currMode=${mode}`,
        }))
      );

      const events = () =>
        rows("canonical.jsonl")
          .filter(
            (row) =>
              row.kind === "hdt.event" && row.data.name === "OnModeChanged"
          )
          .map((row) => row.data.value);

      await send("canonical-input");
      assert(
        JSON.stringify(events()) === JSON.stringify(modes),
        "Canonical mode events differ"
      );
      copyFileSync(file("canonical.jsonl"), file("canonical-first.jsonl"));
      await send("canonical-input");
      assert(
        JSON.stringify(events()) === JSON.stringify([...modes, ...modes]),
        "Repeated canonical mode events differ"
      );
      assert(
        rows("canonical.jsonl").filter((row) => row.kind === "parser.return")
          .length === 8 &&
          rows("canonical.jsonl").every(
            (row) =>
              !["parser.error", "hdt.event.capture-failed"].includes(row.kind)
          ),
        "Canonical parser failed"
      );
      const replayPid = Number(run(["pgrep", "-P", String(host.pid)], true));
      assert(
        Number.isSafeInteger(replayPid) && replayPid > 0,
        "Ambiguous replay process"
      );
      assert(
        read(`/proc/${replayPid}/cmdline`).includes("ReplayHost.exe"),
        "Replay process identity differs"
      );
      process.kill(replayPid, "SIGSTOP");
      await waitFor(
        () => read(`/proc/${replayPid}/stat`).includes(") T "),
        30,
        10
      );
      const journalRows = () => rows("journal/journal.jsonl");
      const before = journalRows().length;
      writeFileSync(file("canonical-input.json"), "\n", { flag: "a" });
      await Bun.sleep(1000);

      const bytes = (source: string, generation?: number) =>
        Buffer.concat(
          journalRows()
            .filter(
              (row) =>
                row.kind === "source.bytes" &&
                row.data.source === source &&
                (generation === undefined || row.data.generation === generation)
            )
            .map((row) => Buffer.from(row.data.base64, "base64"))
        );

      assert(
        bytes("input").equals(readFileSync(file("canonical-input.json"))),
        "Input capture stalled with the HDT host"
      );
      process.kill(replayPid, "SIGCONT");
      assert(journalRows().length > before, "Journal did not progress");
      process.kill(replayPid, "SIGKILL");
      const firstStatus = await host.exited;
      assert(
        firstStatus !== 0 && firstStatus !== 124,
        "Deliberately killed host returned an unexpected status"
      );
      writeFileSync(file("canonical-killed-status.txt"), `${firstStatus}\n`);
      assert(!existsSync(`/proc/${replayPid}`), "Killed host still exists");
      renameSync(file("canonical.jsonl"), file("canonical-session1.jsonl"));
      unlinkSync(file("command.json"));
      unlinkSync(file("state.json"));
      id = 0;
      host = startHost("wine-second.log", "30", "host-second.log");
      await waitFor(stateExists, 100);
      await send("canonical-start");
      await send("canonical-input");
      assert(
        JSON.stringify(events()) === JSON.stringify(modes) &&
          rows("canonical.jsonl").every(
            (row) =>
              !["parser.error", "hdt.event.capture-failed"].includes(row.kind)
          ),
        "Second canonical session differs"
      );
      await send("finish");
      assert((await host.exited) === 0, "Second host failed");
      await Bun.sleep(500);
      journal.kill("SIGTERM");
      assert(
        (await journal.exited) === 0,
        "Independent journal shutdown failed"
      );
      journal = undefined;

      const collected = journalRows();
      const last = collected.at(-1);

      assert(
        collected.some(
          (row) => row.kind === "source.bytes" && row.data.source === "input"
        ) &&
          collected.some(
            (row) =>
              row.kind === "source.bytes" &&
              row.data.source === "hdt" &&
              row.data.generation === 1
          ) &&
          collected.some((row) => row.kind === "source.replaced") &&
          last.kind === "journal.stop" &&
          last.data.reason === "signal",
        "Journal lifecycle differs"
      );

      for (const [generation, name] of [
        [0, "canonical-session1.jsonl"],
        [1, "canonical.jsonl"],
      ] as const) {
        assert(
          bytes("hdt", generation).equals(readFileSync(file(name))),
          "Canonical publisher bytes differ from independent journal"
        );
      }
      console.log(
        "Canonical installed-parser mode transitions and independent journal survival verified in isolation."
      );
      failed = false;

      return;
    }

    await settings();
    let recorder: string | undefined;

    if (diagnostics === "true") {
      await Bun.sleep(1200);
      await send("diagnostic-ui-stall");
      await Bun.sleep(1500);

      const candidates = readdirSync(capture)
        .filter((name) => name.startsWith("flight-recorder-"))
        .map((name) => `${name}/events.jsonl`)
        .filter((name) => existsSync(file(name)));

      assert(candidates.length === 1, "Ambiguous recorder evidence");
      recorder = candidates[0];
      const events = rows(recorder);
      assert(
        events.some((row) => row.name === "ui.stall") &&
          events.some((row) => row.name === "ui.recovered") &&
          events.some(
            (row) => row.name === "ui.dispatch" && row.data.durationMS >= 2000
          ) &&
          events.some(
            (row) => row.name === "render.sample" && row.data.callbacks > 0
          ) &&
          events.some((row) => row.name === "build.identity"),
        "Recorder stall evidence incomplete"
      );
      assert(
        readFileSync(file(join(dirname(recorder), "first-stall-events.jsonl")))
          .length > 0,
        "First stall evidence missing"
      );
    }

    if (bgs === "true") {
      await send("bgs-start");

      for (let toggle = 0; toggle < 6; toggle++) {
        await send("bgs-minions-toggle");
        await Bun.sleep(500);
        copyFileSync(file("state.json"), file(`bgs-tab-${toggle}.json`));
        check(
          toggle % 2 === 0
            ? '.battlegroundsActiveTab=="BattlegroundsMinionsViewModel" and .battlegroundsContent.visible and .battlegroundsContent.height>0 and .battlegroundsContent.minionViews==1 and .battlegroundsContent.groupViews>0'
            : ".battlegroundsActiveTab==null",
          "state.json"
        );
        screenshot(`bgs-tab-${toggle}.png`);
        await settings();
      }

      await send("bgs-end");
    }

    const phases = [
      "arena-lobby",
      ...Array.from({ length: 6 }, () => ["draft-1", "draft-2"]).flat(),
      "menu",
    ];

    for (const phase of phases) {
      await send(phase);
      await Bun.sleep(1000);
      copyFileSync(file("state.json"), file(`${phase}.json`));

      const {
        observedUTC,
        commandId,
        scene,
        cards,
        plaqueLevels,
        visibleScores,
      } = json("state.json");

      writeFileSync(
        file("transitions.jsonl"),
        `${JSON.stringify({ observedUTC, commandId, scene, cards, plaqueLevels, visibleScores })}\n`,
        { flag: "a" }
      );
      screenshot(`${phase}.png`);
      await settings();
    }

    assert(
      rows("transitions.jsonl").filter((row) => row.scene.startsWith("draft-"))
        .length === 12,
      "Draft transition count differs"
    );

    const levels =
      level === "mixed"
        ? [1, 3, 5]
        : [Number(level), Number(level), Number(level)];

    const emitters =
      particles !== "true" || plugin === "true"
        ? 0
        : level === "mixed"
          ? 1
          : level === "5"
            ? 3
            : 0;

    for (const [name, cards, scores] of [
      ["draft-1", ["CS2_029", "CS2_032", "CS2_172"], ["60", "61", "62"]],
      ["draft-2", ["CS2_024", "CS2_025", "CS2_182"], ["80", "81", "82"]],
    ] as const) {
      const state = json(`${name}.json`);
      assert(
        JSON.stringify(state.cards) === JSON.stringify(cards) &&
          JSON.stringify(state.visibleScores) === JSON.stringify(scores) &&
          JSON.stringify(state.plaqueLevels) === JSON.stringify(levels) &&
          state.visibleEmitters === emitters,
        `Draft score binding differs: ${name}`
      );
    }

    check(
      '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]',
      "menu.json"
    );

    if (plugin === "true") {
      check(".pluginUpdates>0", "draft-2.json");
      check('.state.arenaClientState=="None"', "plugin-input.json");

      if (particles === "true" && ["mixed", "5"].includes(level)) {
        check(".state.arenaParticleEmittersSuppressed>=1", "plugin-input.json");
      }
    }

    const mains = run(
      [
        "xdotool",
        "search",
        "--onlyvisible",
        "--name",
        "^Hearthstone Deck Tracker$",
      ],
      true
    ).split("\n");

    assert(mains.length === 1, "Ambiguous main window");
    run(["xdotool", "windowsize", mains[0], "1280", "720"]);
    await Bun.sleep(500);
    check(".mainWidth>=1250 and .mainHeight>=700", "state.json");
    await settings();
    await send("popup-show");
    await Bun.sleep(300);
    check(".popupOpen", "state.json");
    screenshot("popup.png");
    await send("popup-hide");
    check(".popupOpen==false", "state.json");
    await send("new");
    await Bun.sleep(1000);
    copyFileSync(file("state.json"), file("new.json"));
    check('.visibleDialogTypes | any(endswith("DeckTypeDialog"))', "new.json");
    screenshot("new.png");

    if (negative === "true") {
      console.error(
        "Deliberately asserting that the current offer is an impossible fixture."
      );
      check('.cards==["INTENTIONAL_ASSERTION_FAILURE"]', "state.json");
    }

    await send("finish");
    assert((await host.exited) === 0, "Replay host shutdown failed");

    if (diagnostics === "true") {
      if (bgs === "true") {
        const events = rows(recorder!);
        assert(
          events.filter((row) => row.name === "battlegrounds.tab.changed")
            .length === 6 &&
            events.filter(
              (row) => row.name === "battlegrounds.minions.pointer.down"
            ).length === 6,
          "Battlegrounds diagnostic event count differs"
        );
      }

      run([
        "bun",
        join(snapshot, "src/diagnostics/trace-export.ts"),
        file("trace.json"),
        file(recorder!),
      ]);
    }

    assert(
      !/^(dispatcher|command|startup) /m.test(read(file("host.log"))),
      "Fixture host recorded an exception"
    );
    console.log(
      "Verified fixture transitions, rendered score bindings, native Settings input, popup, resize, New dialog, and host shutdown."
    );
    failed = false;
  } finally {
    if (failed && process.env.DISPLAY) {
      try {
        run([
          "timeout",
          "3",
          "magick",
          "import",
          "-window",
          "root",
          file("failure.png"),
        ]);
      } catch {}
    }
    if (journal && journal.exitCode === null) {
      journal.kill("SIGTERM");
      await journal.exited;
    }

    for (const child of children.reverse()) {
      if (child.exitCode === null) {
        child.kill("SIGTERM");
        await child.exited;
      }
    }
  }
});
