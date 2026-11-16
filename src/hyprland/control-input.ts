import { join } from "node:path";

import {
  projectRoot as root,
  nativeDirectory,
  windowClass,
} from "../runtime/config";

const session = process.argv[2];

if (!session) {
  throw Error("Expected control-region session nonce");
}
let lastFailure = "",
  running = true;

let linkedPair = "";

const links: {
  pid: number;
  overlay: number;
  game: number;
  original: number;
}[] = [];

process.on("SIGTERM", () => {
  running = false;
});

async function command(args: string[]) {
  const p = Bun.spawn(args, { stdout: "pipe", stderr: "pipe" });

  const [out, err] = await Promise.all([
    new Response(p.stdout).text(),
    new Response(p.stderr).text(),
  ]);

  if (await p.exited) {
    throw Error(err.trim() || `${args[0]} failed`);
  }
  return out;
}

while (running) {
  try {
    const data = await Bun.file(
      join(root, "evidence/input-region.json")
    ).json();

    if (data.session !== session) {
      await Bun.sleep(200);
      continue;
    }

    const clients = JSON.parse(await command(["hyprctl", "-j", "clients"]));

    const overlays = clients.filter(
      (x: any) => x.title === "HearthstoneOverlay" && x.class === windowClass
    );

    const mains = clients.filter(
      (x: any) =>
        x.title === "Hearthstone Deck Tracker" && x.class === windowClass
    );

    if (
      overlays.length !== 1 ||
      mains.length !== 1 ||
      overlays[0].pid !== mains[0].pid
    ) {
      throw Error("Ambiguous HDT identity");
    }
    const games = clients.filter(
      (x: any) => x.title === "Hearthstone" && x.class === windowClass
    );

    if (games.length === 1) {
      const pair = `${overlays[0].pid}:${overlays[0].address}:${games[0].pid}:${games[0].address}`;

      if (pair !== linkedPair) {
        const link = JSON.parse(
          await command([
            join(nativeDirectory, "overlay-link"),
            "apply",
            String(overlays[0].pid),
            String(games[0].pid),
            windowClass,
          ])
        );

        if (
          ![link.overlay, link.game, link.original].every(
            (x) => Number.isInteger(x) && x >= 0 && x <= 4_294_967_295
          ) ||
          !link.overlay ||
          !link.game
        ) {
          throw Error("Invalid overlay relationship readback");
        }
        links.push({ pid: overlays[0].pid, ...link });
        // Hyprland now raises the overlay with the game in one transient stack.
        // Keep the active-window handler; stop polling raises only after readback.
        await command([
          "hyprctl",
          "eval",
          "if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(false) end",
        ]);
        linkedPair = pair;
        console.log(
          "HDT overlay linked to game stack; periodic raising disabled."
        );
      }
    }

    const [width, height] = overlays[0].size;

    const fresh =
      Number.isFinite(data.observedMS) &&
      Math.abs(Date.now() - data.observedMS) < 1500 &&
      width === data.width &&
      height === data.height;

    const rectangles = fresh ? data.rectangles : [];

    if (
      !Array.isArray(rectangles) ||
      rectangles.length > 2048 ||
      !rectangles.every(
        (r: any) =>
          Array.isArray(r) &&
          r.length === 4 &&
          r.every(Number.isInteger) &&
          r[0] >= 0 &&
          r[1] >= 0 &&
          r[2] > 0 &&
          r[3] > 0 &&
          r[0] + r[2] <= width &&
          r[1] + r[3] <= height
      )
    ) {
      throw Error("Invalid control rectangles");
    }
    // Only validated integers enter Lua; never interpolate target text or card/account content.
    const lua = `hdtProtonControls={${rectangles.map((r: number[]) => `{${r.join(",")}}`).join(",")}}; hdtProtonControlTicks=40`;
    await command(["hyprctl", "eval", lua]);
    const cursor = JSON.parse(await command(["hyprctl", "-j", "cursorpos"]));
    const [left, top] = overlays[0].at;

    const hit = rectangles.some(
      (r: number[]) =>
        cursor.x >= left + r[0] &&
        cursor.y >= top + r[1] &&
        cursor.x < left + r[0] + r[2] &&
        cursor.y < top + r[1] + r[3]
    );

    await Bun.write(
      join(root, "evidence/control-input.json"),
      JSON.stringify({
        observedUTC: new Date().toISOString(),
        fresh,
        controls: rectangles.length,
        pointerOverControl: hit,
      })
    );
    lastFailure = "";
  } catch (error) {
    if ((error as any).code !== "ENOENT") {
      const message = String(error);

      if (message !== lastFailure) {
        console.error("HDT control input pending:", message);
      }
      lastFailure = message;
    }
  }

  await Bun.sleep(200);
}

// Return to pass-through on normal shutdown; the compositor TTL covers an abrupt exit.
await command([
  "hyprctl",
  "eval",
  "hdtProtonControls=nil; hdtProtonControlTicks=0; if hdtProtonControlRule then hdtProtonControlRule:set_enabled(false) end; hdtProtonControlActive=false",
]);

// Restore only our own overlay relationship, and only if its identity/current
// value still match. Closed/replaced overlay windows require no restoration.
await command([
  "hyprctl",
  "eval",
  "if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(true) end",
]);

for (const link of links.reverse()) {
  await command([
    join(nativeDirectory, "overlay-link"),
    "restore",
    String(link.pid),
    windowClass,
    String(link.overlay),
    String(link.game),
    String(link.original),
  ]);
}
