import { mkdirSync } from "node:fs";
import { resolve, join } from "node:path";
import { main, root, exec, execScript, run } from "./process";
main(() => {
  process.chdir(root);
  const args = process.argv.slice(2), command = args.shift() ?? "open";
  const dataRoot = process.env.HDT_PROJECT_DIR ?? root;
  const native = process.env.HDT_NATIVE_DIR ?? join(dataRoot, "evidence");
  const runtime = (name: string) => exec([process.execPath, resolve(root, "src", name), ...args]);
  const compile = (name: string, source: string, libs: string[]) => {
    if (process.env.HDT_NATIVE_DIR) {
      run(["test", "-x", join(native, name)]);
    } else {
      mkdirSync(native, { recursive: true });
      // pkg-config emits compiler arguments; these package names and project
      // paths are fixed, not operator-provided shell expressions.
      const flags = run(["pkg-config", "--cflags", "--libs", ...libs], { capture: true }).trim().split(/\s+/).filter(Boolean);
      run(["cc", "-Wall", "-Wextra", "-Werror", source, "-o", join(native, name), ...flags]);
    }
  };
  switch (command) {
    case "trace-export": return runtime("diagnostics/trace-export.ts");
    case "observe": return runtime("diagnostics/input-journal.ts");
    case "open": case "restart": return execScript("desktop", [command, ...args]);
    case "watch": return execScript("autostart", args);
    case "setup": return execScript("setup", args);
    case "status": return runtime("runtime/status.ts");
    case "raw-launch": return runtime("runtime/launch.ts");
    case "steam-options": return execScript("steam-options", args);
    case "steam-run": return execScript("steam-launch", args);
    case "build-surface-probe": return compile("x-surface-probe", "src/diagnostics/x-surface-probe.c", ["x11", "xcomposite", "xrender"]);
    case "overlay-launch":
      mkdirSync(join(dataRoot, "evidence"), { recursive: true });
      compile("overlay-link", "src/hyprland/overlay-link.c", ["x11"]);
      run([process.execPath, "src/hyprland/overlay-rule.ts"]);
      Object.assign(process.env, { HDT_DIRECT: "1", HDT_SOFTWARE_RENDER: "1",
        HDT_RENDERER: process.env.HDT_RENDERER ?? "dxvk-software",
        HDT_ALPHA_FLOOR: process.env.HDT_ALPHA_FLOOR ?? "1",
        HDT_CONTROL_INPUT: process.env.HDT_CONTROL_INPUT ?? "1" });
      return runtime("runtime/launch.ts");
    default:
      console.error("Usage: hdt-proton [open|restart|watch|setup|status|trace-export|observe|raw-launch|overlay-launch|steam-options|steam-run|build-surface-probe]");
      process.exit(2);
  }
});
