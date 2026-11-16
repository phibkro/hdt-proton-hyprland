import { main, run, exec } from "./process";
main(() => {
  const args = process.argv.slice(2);
  if (!args.length) {
    console.error("Use this script in the Battle.net Steam shortcut Launch Options, followed by %command%.");
    process.exit(2);
  }
  try { run(["systemctl", "--user", "start", "hdt-proton-autostart.service"]); }
  catch { console.error("HDT startup service unavailable. Install the desktop service described in docs/INSTALL.md; launching the game normally."); }
  exec(args);
});
