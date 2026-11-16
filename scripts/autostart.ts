import { main, run, read, pid, lock } from "./process";
type Client = { class: string; title: string; pid: number };
main(async () => {
  const { windowClass } = await import("../src/runtime/config");
  const runtime = process.env.XDG_RUNTIME_DIR;
  if (!runtime) throw Error("A graphical login is required");
  const ownership = lock(`${runtime}/hdt-proton-${windowClass}.lock`);
  if (!ownership.acquired) { ownership.close(); console.log("HDT automatic startup already has an owner."); return; }
  let gameSeen = "", launchedGame = "", launchedAt = 0, failures = 0, nextAttempt = 0, paused = false;
  let launcher: ReturnType<typeof Bun.spawn> | undefined;
  let outcome: { status: number } | undefined;
  const alive = (clients: Client[]) => clients.some(client => {
    if (client.class !== windowClass || client.title !== "Hearthstone Deck Tracker") return false;
    try {
      const id = pid(client.pid);
      const state = read(`/proc/${id}/status`).match(/^State:\s+(\S+)/m)?.[1];
      return read(`/proc/${id}/comm`) === "HearthstoneDeck" && state !== undefined && !["Z", "X"].includes(state);
    } catch { return false; }
  });
  console.log("HDT automatic startup is watching for Hearthstone.");
  try {
    while (true) {
      const clients: Client[] = JSON.parse(run(["hyprctl", "-j", "clients"], { capture: true }));
      const games = [...new Set(clients.filter(c => c.class === windowClass && c.title === "Hearthstone").map(c => pid(c.pid)))];
      if (games.length > 1) throw Error("Multiple Hearthstone processes; refusing to guess");
      const game = games[0]?.toString() ?? "";
      const now = Number(run(["date", "+%s"], { capture: true }));
      if (!Number.isSafeInteger(now)) throw Error("Invalid clock");
      if (game !== gameSeen) { gameSeen = game; failures = 0; nextAttempt = 0; paused = false; }
      if (launcher) {
        if (outcome) {
          const status = outcome.status;
          launcher = undefined; outcome = undefined;
          if (launchedGame === game && game !== "") {
            if (status === 0) {
              paused = true;
              console.log("HDT launcher exited successfully; automatic recovery paused until the game restarts or the tracker service is restarted.");
            } else if (++failures >= 3) {
              paused = true;
              console.log("HDT failed three times; automatic recovery paused. Restart the tracker service to retry.");
            } else {
              const delay = 5 * failures; nextAttempt = now + delay;
              console.log(`HDT launcher failed (status ${status}); retrying in at least ${delay} seconds.`);
            }
          }
        } else if (now - launchedAt >= 60) failures = 0;
      }
      if (game !== "" && !launcher && !paused && now >= nextAttempt) {
        if (alive(clients)) {
          paused = true;
          console.log("Existing HDT left running; its exit status belongs to its original launcher.");
        } else {
          console.log("Hearthstone detected; starting the compatibility launcher.");
          launcher = Bun.spawn(["hdt-overlay-launch"], { stdin: "ignore", stdout: "inherit", stderr: "inherit" });
          // The owned subprocess object, never compositor PIDs, supplies exit status.
          launcher.exited.then(status => { outcome = { status: status ?? 1 }; });
          launchedGame = game; launchedAt = now;
        }
      }
      const delay = Bun.spawn(["sleep", "2"], { stdin: "ignore", stdout: "inherit", stderr: "inherit" });
      if (await delay.exited !== 0) throw Error("Watcher delay failed");
    }
  } finally { ownership.close(); }
});
