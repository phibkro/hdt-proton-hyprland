import { main, run } from "./process";
main(() => {
  if (process.argv.length > 2) { console.error("Usage: hdt-setup (inside devenv; Battle.net running through Steam, HDT initialized once)"); process.exit(2); }
  // Compile before installation: a missing assembly must not close a tracker.
  run(["hdt-launch", "--build-plugin"]);
  run(["bun", "src/runtime/install-plugin.ts"]);
  run(["bun", "src/runtime/configure-overlay.ts"]);
  console.log("Compatibility setup complete. Start HDT with: devenv shell -- hdt-overlay-launch\nFor automatic startup through your existing desktop service, run: devenv shell -- hdt-steam-options");
});
