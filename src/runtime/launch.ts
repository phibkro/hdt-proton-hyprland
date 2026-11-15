import { readdir, readFile, readlink, realpath, stat, mkdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { projectRoot as root, sourceRoot, prefix, appDirectory } from "./config";

const canonicalPrefix=await realpath(prefix);
// Velopack locates Update.exe relative to the installed executable's directory.
// Resolve an app -> managed/current link before crossing into Wine.
const exe = await realpath(join(appDirectory, "HearthstoneDeckTracker.exe"));
await mkdir(join(root,"evidence"),{recursive:true,mode:0o700});
async function environment(pid: string): Promise<Record<string, string>> {
  return Object.fromEntries((await readFile(`/proc/${pid}/environ`, "utf8"))
    .split("\0").filter(x => x.includes("=")).map(x => {
      const n = x.indexOf("="); return [x.slice(0, n), x.slice(n + 1)];
    }));
}
async function session(pid: string) {
  const e = await environment(pid);
  if (await realpath(e.WINEPREFIX ?? "/missing") !== canonicalPrefix) throw Error("Wrong game prefix");
  const server = await readlink(`/proc/${pid}/exe`);
  if (!server.endsWith("/files/bin/wineserver")) throw Error("Expected live Proton wineserver");
  const proton = server.slice(0, -"files/bin/wineserver".length) + "proton";
  for (const fd of await readdir(`/proc/${pid}/fd`)) {
    let path: string;
    try { path = await readlink(`/proc/${pid}/fd/${fd}`); }
    catch (error) {
      if (error instanceof Error && "code" in error && error.code === "ENOENT") continue;
      throw error;
    }
    if (/wine-.*-fsync \(deleted\)$/.test(path)) throw Error("Wine fsync file is deleted; fully restart the Steam game session before launching HDT.");
  }
  return { e, proton };
}
const inside = process.argv[2] === "--inside";
const probe = process.argv.includes("--window-probe");
const buildPlugin = process.argv.includes("--build-plugin");
if (inside) {
  const { e, proton } = await session(process.argv[3]);
  const outer = await stat(`/proc/${process.argv[3]}/root/tmp`);
  const current = await stat("/tmp");
  if (outer.dev !== current.dev || outer.ino !== current.ino) throw Error("Steam temporary-files namespace was not joined");
  if (buildPlugin) {
    const win = (path: string) => "Z:" + path.replaceAll("/", "\\");
    const wpf = "C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\WPF\\";
    const args = ["/nologo", "/target:library", "/platform:x64", "/out:"+win(join(root,"evidence/ProtonCompatibility.dll")),
      "/r:"+win(exe), "/r:System.Drawing.dll", "/r:System.Xaml.dll",
      "/r:"+win(join(appDirectory,"HearthMirror.dll")),
      "/r:System.Web.Extensions.dll",
      "/r:"+win(join(appDirectory,"MahApps.Metro.dll")),
      ...["PresentationCore.dll","PresentationFramework.dll","WindowsBase.dll"].map(x=>"/r:"+wpf+x),
      ...(await readdir(join(sourceRoot,"src/plugin"))).filter(x=>x.endsWith(".cs")).sort()
        .map(x=>win(join(sourceRoot,"src/plugin",x)))];
    const p = Bun.spawn([join(dirname(proton),"files/bin/wine"),"C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe",...args],
      {cwd:root,env:e,stdout:"inherit",stderr:"inherit"});
    process.exit(await p.exited);
  }
  if (probe) {
    // A windowless, read-only helper; compile and execute with the same installed CLR/Wine.
    const wine = join(dirname(proton), "files/bin/wine");
    const win = (path: string) => "Z:" + path.replaceAll("/", "\\");
    const output = join(root, "evidence/window-probe.exe");
    const build = Bun.spawn([wine, "C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe",
      "/nologo", "/target:winexe", "/platform:x64", "/out:" + win(output), win(join(sourceRoot,"src/diagnostics/window-probe.cs"))],
      {cwd: root, env: e, stdout:"inherit", stderr:"inherit"});
    if(await build.exited) throw Error("Window probe compilation failed");
    const run = Bun.spawn([wine, output, win(join(root,"evidence/window-probe.json"))],
      {cwd:root, env:e, stdout:"inherit", stderr:"inherit"});
    process.exit(await run.exited);
  }
  const renderer = process.env.HDT_RENDERER ?? (process.env.HDT_SOFTWARE_RENDER === "1" ? "dxvk-software" : "inherit");
  if (!["inherit", "dxvk-software", "wine-gdi"].includes(renderer)) throw Error("HDT_RENDERER must be inherit, dxvk-software, or wine-gdi");
  if (renderer === "dxvk-software") {
    // Process-local DXVK workaround: no prefix registry or game environment changes.
    e.DXVK_CONFIG = [e.DXVK_CONFIG, "d3d9.shaderModel = 1"].filter(Boolean).join("; ");
    e.DXVK_LOG_PATH = join(root, "evidence");
  }
  if (renderer === "wine-gdi") {
    // Keep every other process-local DLL override, replacing only d3d9's choice.
    const overrides = (e.WINEDLLOVERRIDES ?? "").split(";").filter(Boolean).flatMap(rule => {
      const eq = rule.indexOf("=");
      if(eq < 0) throw Error("Malformed inherited DLL override");
      const modules = rule.slice(0,eq).split(",").filter(x=>x.trim().toLowerCase()!=="d3d9");
      return modules.length ? [modules.join(",")+rule.slice(eq)] : [];
    });
    e.WINEDLLOVERRIDES = [...overrides,"d3d9=b"].join(";");
    e.WINE_D3D_CONFIG = [...(e.WINE_D3D_CONFIG ?? "").split(/[;,]/).filter(x=>x && !x.startsWith("renderer=")),"renderer=gdi"].join(";");
    e.WINEDEBUG = "-all,+winediag";
  }
  if (process.env.HDT_DEBUG === "1") e.WINEDEBUG = "+mscoree,+seh";
  e.HDT_FLIGHT_RECORDER=process.env.HDT_FLIGHT_RECORDER ?? "1";
  if(!["0","1"].includes(e.HDT_FLIGHT_RECORDER)) throw Error("HDT_FLIGHT_RECORDER must be 0 or 1");
  if (process.env.HDT_HOVER_DIAGNOSTICS === "1") e.HDT_HOVER_DIAGNOSTICS_FILE = join(root,"evidence/hover-state.json");
  if (process.env.HDT_ALPHA_FLOOR === "1") e.HDT_ALPHA_FLOOR="1";
  if(process.env.HDT_CONTROL_INPUT === "1") {
    e.HDT_INPUT_REGION_FILE=join(root,"evidence/input-region.json");
    e.HDT_INPUT_SESSION=process.env.HDT_INPUT_SESSION!;
    if(!e.HDT_INPUT_SESSION) throw Error("Input-region session is missing");
  }
  // Do not persist the inherited session environment: it may contain credentials.
  const command = process.env.HDT_DIRECT === "1"
    ? [join(dirname(proton), "files/bin/wine"), exe]
    : ["/usr/bin/python3", proton, "run", exe];
  const p = Bun.spawn(command, {
    cwd: dirname(exe), env: e, stdin: "inherit", stdout: "inherit", stderr: "inherit",
  });
  const code = await p.exited;
  console.log(`HDT launch process exited with status ${code}`);
  process.exit(code);
}
const candidates: string[] = [];
for (const pid of await readdir("/proc")) {
  if (!/^\d+$/.test(pid)) continue;
  try {
    if ((await readFile(`/proc/${pid}/comm`, "utf8")).trim() !== "wineserver") continue;
    await session(pid); candidates.push(pid);
  } catch (error) {
    if (error instanceof Error && "code" in error && ["ENOENT", "EACCES", "EPERM"].includes(String(error.code))) continue;
    if (error instanceof Error && error.message === "Wrong game prefix") continue;
    throw error;
  }
}
if (candidates.length !== 1) throw Error(`Expected one running game wineserver; found ${candidates.length}. Launch Battle.net through Steam first.`);
await readFile(exe);
const nsenter = Bun.which("nsenter");
if (!nsenter) throw Error("Enter the project devenv shell first (nsenter required).");
console.log("Launching HDT inside the existing Steam Proton session.");
let bridge: ReturnType<typeof Bun.spawn> | undefined;
if(process.env.HDT_ELEMENT_INPUT === "1") throw Error("X11 input-shape experiment retired; use HDT_CONTROL_INPUT=1 for compositor control gating.");
if(process.env.HDT_CONTROL_INPUT === "1" && !probe && !buildPlugin) {
  process.env.HDT_INPUT_SESSION=crypto.randomUUID();
  bridge=Bun.spawn([process.execPath,join(sourceRoot,"src/hyprland/control-input.ts"),process.env.HDT_INPUT_SESSION],{cwd:root,env:{...process.env},stdout:"inherit",stderr:"inherit"});
  bridge.exited.then(code=>{if(code) console.error(`HDT input-region helper failed (${code}); selective input is not verified.`);});
}
const p = Bun.spawn([nsenter, "--target", candidates[0], "--user", "--mount", "--preserve-credentials", "--root", "--", process.execPath, import.meta.path, "--inside", candidates[0], ...(probe ? ["--window-probe"] : []), ...(buildPlugin ? ["--build-plugin"] : [])], {
  cwd: root, env: {...process.env}, stdin: "inherit", stdout: "inherit", stderr: "inherit",
});
const code=await p.exited;
if(bridge) { bridge.kill("SIGTERM"); await bridge.exited; }
process.exit(code);
