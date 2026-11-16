import { readdirSync, mkdirSync } from "node:fs";
import { main, run, root } from "./process";
main(() => {
  process.chdir(root);
  process.env.HS_APP_ID = "1234567890";
  run([process.execPath, "scripts/test-unit.ts"]);
  run(["shellcheck", "autostart.sh", ...readdirSync("scripts").filter(file => file.endsWith(".sh")).map(file => `scripts/${file}`)]);
  const entries = ["src/runtime/launch.ts", "src/hyprland/control-input.ts", "src/runtime/status.ts", "src/hyprland/overlay-rule.ts", "src/diagnostics/surface-check.ts", "src/runtime/install-plugin.ts", "src/runtime/configure-overlay.ts", "src/diagnostics/trace-export.ts", "src/diagnostics/input-journal.ts", "tests/xinput-race.ts",
    ...readdirSync("scripts").filter(file => file.endsWith(".ts")).map(file => `scripts/${file}`)];
  mkdirSync("evidence", { recursive: true });
  run([process.execPath, "build", ...entries, "--target=bun", "--outdir=evidence/check-build"]);
  for (const [source, output, libs] of [
    ["src/hyprland/overlay-link.c", "evidence/overlay-link-check", ["x11"]],
    ["tests/xinput-race.c", "evidence/xinput-race-check", ["x11", "xi"]],
  ] as const) {
    const flags = run(["pkg-config", "--cflags", "--libs", ...libs], { capture: true }).trim().split(/\s+/).filter(Boolean);
    run(["cc", "-Wall", "-Wextra", "-Werror", source, "-o", output, ...flags]);
  }
  run([process.execPath, "scripts/hdt.ts", "build-surface-probe"]);
});
