import { dirname, resolve } from "node:path";
import { main } from "./process";
main(() => {
  if (process.argv.length > 2) { console.error("Usage: hdt-steam-options"); process.exit(2); }
  if (process.env.HDT_INSTALLED === "1") { console.log("hdt-proton steam-run %command%"); return; }
  const wrapper = resolve(process.env.HDT_STEAM_WRAPPER ?? resolve(dirname(process.argv[1]), "steam-launch.sh"));
  console.log(`bash '${wrapper.replaceAll("'", "'\\''")}' %command%`);
});
