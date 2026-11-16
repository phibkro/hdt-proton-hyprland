import { projectRoot as root, windowClass } from "../runtime/config";

const args = process.argv.slice(2);

if (args.length > 1 || (args.length === 1 && args[0] !== "--disable")) {
  throw Error("Usage: overlay-rule.ts [--disable]");
}
const source =
  args[0] === "--disable"
    ? `
if hdtProtonOverlayRule then hdtProtonOverlayRule:set_enabled(false) end
if hdtProtonMainRule then hdtProtonMainRule:set_enabled(false) end
if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(false) end
if hdtProtonStackSubscription then hdtProtonStackSubscription:remove(); hdtProtonStackSubscription=nil end
if hdtProtonControlRule then hdtProtonControlRule:set_enabled(false) end
if hdtProtonControlTimer then hdtProtonControlTimer:set_enabled(false) end
hdtProtonControls=nil; hdtProtonControlTicks=0; hdtProtonControlActive=false
`
    : (
        await Bun.file(new URL("overlay-rules.lua", import.meta.url)).text()
      ).replaceAll("__STEAM_WINDOW_CLASS__", windowClass);

// hyprctl treats a leading Lua comment as an option; send executable Lua first.
const lua = source
  .split("\n")
  .filter((x) => !x.trimStart().startsWith("--"))
  .join(" ");

const p = Bun.spawn(["hyprctl", "eval", lua], {
  stdout: "inherit",
  stderr: "inherit",
});

process.exit(await p.exited);
