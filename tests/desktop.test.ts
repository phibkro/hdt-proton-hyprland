/* eslint-disable no-await-in-loop -- Fixture setup serializes file creation before launching the subprocess under test. */
import { expect, test } from "bun:test";
import { mkdtemp, chmod, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import nodePath from "node:path";

const root = nodePath.resolve(import.meta.dir, "..");

for (const scenario of [
  "restart",
  "game-owned",
  "no-game",
  "ambiguous",
] as const) {
  test(`desktop action preserves the game boundary: ${scenario}`, async () => {
    const dir = await mkdtemp(nodePath.join(tmpdir(), "hdt-desktop-test-"));

    try {
      const cgroup = await readFile(`/proc/${process.pid}/cgroup`, "utf-8");
      const group = cgroup.trim().split("::").at(-1);

      if (group === undefined || group.length === 0) {
        throw new Error("Fixture process has no readable cgroup identity");
      }

      const game = {
        class: "steam_app_123",
        title: "Hearthstone",
        pid: process.pid,
      };

      const tracker = {
        class: "steam_app_123",
        title: "Hearthstone Deck Tracker",
        pid: process.pid,
      };

      const snapshots = {
        ambiguous: [tracker, tracker],
        "no-game": [],
        "game-owned": [game],
        restart: [game],
      };

      await Bun.write(
        nodePath.join(dir, "clients"),
        JSON.stringify(snapshots[scenario])
      );

      for (const [name, source] of Object.entries({
        bun: 'printf "%s\\n" steam_app_123',
        hyprctl: 'cat "$TEST_DIR/clients"',
        systemctl: `if [ "$2" = show ]; then printf '%s\\n' "$TEST_GROUP"; else printf '%s\\n' "$*" >> "$TEST_DIR/calls"; fi`,
        "notify-send": "exit 0",
      })) {
        const file = nodePath.join(dir, name);
        await Bun.write(file, `#!/bin/sh\n${source}\n`);
        await chmod(file, 0o700);
      }

      const p = Bun.spawn(
        ["bash", nodePath.join(root, "scripts/desktop.sh"), "restart"],
        {
          env: {
            ...process.env,
            PATH: `${dir}:${process.env.PATH}`,
            TEST_DIR: dir,
            HS_APP_ID: "123",
            TEST_GROUP:
              scenario === "game-owned" ? group : "/separate-hdt-service",
          },
          stdout: "pipe",
          stderr: "pipe",
        }
      );

      const rejected = ["game-owned", "ambiguous"].includes(scenario);
      expect(await p.exited).toBe(rejected ? 1 : 0);

      if (rejected) {
        expect(await Bun.file(nodePath.join(dir, "calls")).exists()).toBe(
          false
        );
      } else {
        expect(await readFile(nodePath.join(dir, "calls"), "utf-8")).toBe(
          `--user ${scenario === "no-game" ? "start" : "restart"} hdt-proton-autostart.service\n`
        );
      }
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
}
