import { readFile, copyFile, writeFile, mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import { hdtData as data, windowClass } from "./config";
const root = dirname(import.meta.path);
const p = Bun.spawn(["hyprctl", "-j", "clients"], { stdout: "pipe" });
const clients = JSON.parse(await new Response(p.stdout).text());
if(await p.exited) throw Error("Cannot identify HDT window");
const trackers = clients.filter((x: any) => x.title === "Hearthstone Deck Tracker" && x.class===windowClass);
if(trackers.length !== 1) throw Error("Expected one HDT main window");
const hdt = trackers[0];
if((await readFile(`/proc/${hdt.pid}/comm`, "utf8")).trim() !== "HearthstoneDeck") throw Error("HDT process identity changed");
const close = Bun.spawn(["hyprctl", "dispatch", `hl.dsp.window.close({window="address:${hdt.address}"})`], { stdout: "inherit", stderr: "inherit" });
if(await close.exited) throw Error("HDT close request failed");
let stopped = false;
for(let n=0;n<50;n++) {
  try { await readFile(`/proc/${hdt.pid}/comm`); }
  catch(error) { if((error as any).code === "ENOENT") { stopped=true; break; } throw error; }
  await Bun.sleep(200);
}
if(!stopped) throw Error("HDT did not exit; nothing installed");
const settings = join(data, "plugins.xml");
let xml = await readFile(settings,"utf8");
const backup = join(root,"backups/hdt-before-render/plugins-before-compatibility.xml");
await mkdir(dirname(backup),{recursive:true,mode:0o700});
try { await copyFile(settings,backup,1); }
catch(error) { if((error as any).code !== "EEXIST") throw error; }
const entry = "<PluginSettings><FileName>Plugins/ProtonCompatibility.dll</FileName><IsEnabled>true</IsEnabled><Name>Proton desktop compatibility</Name></PluginSettings>";
const blocks = xml.match(/<PluginSettings>[\s\S]*?<\/PluginSettings>/g) ?? [];
const ours = blocks.filter(x=>x.includes("<FileName>Plugins/ProtonCompatibility.dll</FileName>"));
if(ours.length>1) throw Error("Duplicate local plugin settings");
if(ours.length) xml=xml.replace(ours[0],entry);
else if(/<ArrayOfPluginSettings[^>]*\/>/.test(xml)) xml=xml.replace(/(<ArrayOfPluginSettings[^>]*?)\s*\/>/,"$1>"+entry+"</ArrayOfPluginSettings>");
else if(xml.includes("</ArrayOfPluginSettings>")) xml=xml.replace("</ArrayOfPluginSettings>",entry+"</ArrayOfPluginSettings>");
else throw Error("Unexpected plugin settings schema");
// Roaming is HDT's authoritative plugin directory; HDT generates app/Plugins from it.
await mkdir(join(data,"Plugins"),{recursive:true});
await copyFile(join(root,"evidence/ProtonCompatibility.dll"),join(data,"Plugins/ProtonCompatibility.dll"));
await writeFile(settings,xml);
console.log("Installed and enabled local HDT compatibility plugin. Hearthstone was untouched.");
