import { main, run, read, pid, exec } from "./process";
const serviceName = "hdt-proton-autostart.service";
function notify(message: string) { try { run(["notify-send", "Hearthstone Deck Tracker", message]); } catch {} }
function fail(message: string): never { notify(message); throw Error(message); }
function inService(processId: number, service: string): boolean {
  return read(`/proc/${processId}/cgroup`).split("\n").some(line => {
    const [hierarchy, , group] = line.split(":");
    return hierarchy === "0" && service !== "" && (group === service || group.startsWith(`${service}/`));
  });
}
main(async () => {
  const args = process.argv.slice(2), action = args[0] ?? "open";
  if (args.length > 1 || !["open", "restart"].includes(action)) { console.error("Usage: desktop.sh [open|restart]"); process.exit(2); }
  try {
    const { windowClass } = await import("../src/runtime/config");
    const clients = JSON.parse(run(["hyprctl", "-j", "clients"], { capture: true }));
    const trackers = clients.filter((c: any) => c.class === windowClass && c.title === "Hearthstone Deck Tracker");
    if (trackers.length > 1) fail("Multiple HDT windows; refusing to guess.");
    if (trackers.length && read(`/proc/${pid(trackers[0].pid)}/comm`) !== "HearthstoneDeck") fail("Tracker identity changed.");
    if (action === "open" && trackers.length === 1) {
      const address = trackers[0].address;
      if (typeof address !== "string" || !/^0x[0-9a-fA-F]+$/.test(address)) fail("Invalid tracker address.");
      exec(["hyprctl", "dispatch", `hl.dsp.focus({window="address:${address}"})`]);
    }
    const service = run(["systemctl", "--user", "show", serviceName, "--property=ControlGroup", "--value"], { capture: true });
    if (trackers.length && !inService(pid(trackers[0].pid), service)) fail("HDT is outside its desktop service. Close HDT normally, then open it from the launcher.");
    const games = [...new Set<number>(clients.filter((c: any) => c.class === windowClass && c.title === "Hearthstone").map((c: any) => pid(c.pid)))];
    if (games.length > 1) fail("Multiple game processes; refusing a restart.");
    for (const game of games) if (inService(game, service)) fail("Hearthstone belongs to the tracker service; refusing to restart it.");
    if (!games.length) {
      run(["systemctl", "--user", "start", serviceName]);
      notify("Open Hearthstone through Steam. HDT starts when the game window appears.");
    } else exec(["systemctl", "--user", "restart", serviceName]);
  } catch (error) { notify("Could not open HDT. Check its configuration and the Steam game session."); throw error; }
});
