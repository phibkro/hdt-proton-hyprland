import { readFile, readdir, stat } from "node:fs/promises";
import { join } from "node:path";

import { projectRoot, prefix, hdtData as data, windowClass } from "./config";

const drive = join(prefix, "drive_c");

async function optional(path: string) {
  try {
    return await readFile(path, "utf-8");
  } catch (error) {
    if (error instanceof Error && "code" in error && error.code === "ENOENT") {
      return null;
    }
    throw error;
  }
}

const hyprctl = Bun.which("hyprctl") ?? "/run/current-system/sw/bin/hyprctl";

async function hypr(kind: string) {
  const p = Bun.spawn([hyprctl, "-j", kind], {
    stdout: "pipe",
    stderr: "pipe",
  });

  const output = await new Response(p.stdout).text();

  if (await p.exited) {
    throw Error(`hyprctl ${kind} failed`);
  }
  return JSON.parse(output);
}

const clients = (await hypr("clients")).filter(
  (x: any) =>
    x.class === windowClass &&
    [
      "Hearthstone",
      "HearthstoneOverlay",
      "Hearthstone Deck Tracker",
      "Battle.net",
    ].includes(x.title)
);

const monitors = await hypr("monitors");
const logs = join(drive, "Program Files (x86)/Hearthstone/Logs");

const sessions = (await readdir(logs))
  .filter((x) => x.startsWith("Hearthstone_"))
  .sort();

const latest = sessions.at(-1);
const files = [];

if (latest) {
  for (const name of ["Power.log", "Arena.log", "LoadingScreen.log"]) {
    try {
      const s = await stat(join(logs, latest, name));
      files.push({ name, bytes: s.size, modifiedUTC: s.mtime.toISOString() });
    } catch (error) {
      if (
        error instanceof Error &&
        "code" in error &&
        error.code === "ENOENT"
      ) {
        files.push({ name, absent: true });
        continue;
      }
      throw error;
    }
  }
}
const hdt = await optional(join(data, "Logs/hdt_log.txt"));

const tracker = clients.find(
  (x: any) => x.title === "Hearthstone Deck Tracker"
);

const maps = tracker ? await optional(`/proc/${tracker.pid}/maps`) : null;

async function renderSettings(pid?: number) {
  if (!pid) {
    return null;
  }
  const raw = await optional(`/proc/${pid}/environ`);

  if (raw === null) {
    return null;
  }
  // Return only known diagnostic booleans; never persist the session environment.
  const fields = raw.split("\0");

  return {
    wineGdiRequested: fields.some(
      (x) =>
        x.startsWith("WINE_D3D_CONFIG=") && /(?:=|;)renderer=gdi(?:;|$)/.test(x)
    ),
    dxvkShaderModel1Requested: fields.some(
      (x) => x.startsWith("DXVK_CONFIG=") && x.includes("d3d9.shaderModel = 1")
    ),
    builtinD3d9Requested: fields.some(
      (x) => x.startsWith("WINEDLLOVERRIDES=") && /(?:=|;)d3d9=b(?:;|$)/.test(x)
    ),
    alphaFloorRequested: fields.includes("HDT_ALPHA_FLOOR=1"),
    selectiveInputRequested: fields.some((x) =>
      x.startsWith("HDT_INPUT_REGION_FILE=")
    ),
  };
}

const deckStats = await optional(join(data, "DeckStats.xml"));

const controlRaw = await optional(
  join(projectRoot, "evidence/control-input.json")
);

const control = controlRaw === null ? null : JSON.parse(controlRaw);

const matches = (deckStats?.match(/<Game\b[^>]*>[\s\S]*?<\/Game>/g) ?? []).map(
  (block) =>
    Object.fromEntries(
      ["GameMode", "Result", "Turns", "Format"].map((key) => [
        key,
        new RegExp(`<${key}>([^<]*)</${key}>`).exec(block)?.[1] ?? null,
      ])
    )
);

console.log(
  JSON.stringify(
    {
      observedUTC: new Date().toISOString(),
      windows: clients.map((x: any) => ({
        title: x.title,
        position: x.at,
        size: x.size,
        xwayland: x.xwayland,
        fullscreen: x.fullscreen,
      })),
      monitors: monitors.map((x: any) => ({
        name: x.name,
        width: x.width,
        height: x.height,
        scale: x.scale,
      })),
      latestGameLogSession: latest,
      files,
      trackerRuntime:
        maps === null
          ? "unobserved"
          : /libmono-2\.0/i.test(maps)
            ? "Wine Mono loaded"
            : /\/clr\.dll/i.test(maps)
              ? "Microsoft CLR loaded"
              : "unresolved",
      trackerRendering: {
        requested: await renderSettings(tracker?.pid),
        builtinD3d9Mapped:
          maps === null ? null : maps.includes("x86_64-windows/d3d9.dll"),
      },
      gameRenderingRequests: await renderSettings(
        clients.find((x: any) => x.title === "Hearthstone")?.pid
      ),
      controlInput:
        control === null
          ? null
          : {
              heartbeatFresh:
                Math.abs(Date.now() - Date.parse(control.observedUTC)) < 2000,
              regionSnapshotFresh: control.fresh,
              controls: control.controls,
              pointerOverControl: control.pointerOverControl,
            },
      savedMatchEntries: deckStats === null ? null : matches.length,
      savedMatchSummary: matches,
      hdtLog:
        hdt === null
          ? null
          : {
              selectedCorrectLogDirectory: hdt.includes(
                "Using Hearthstone log directory 'C:\\Program Files (x86)\\Hearthstone\\Logs'"
              ),
              dnsFailureObserved: hdt.includes("NameResolutionFailure"),
              portableUpdaterWarningObserved: hdt.includes(
                "Velopack.Exceptions.NotInstalledException"
              ),
              hearthMirrorMentioned: hdt.includes("HearthMirror"),
            },
    },
    null,
    2
  )
);
