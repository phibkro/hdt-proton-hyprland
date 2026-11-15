import { join } from "node:path";
import { appendFile } from "node:fs/promises";
import { projectRoot as root, nativeDirectory, windowClass } from "../runtime/config";
async function command(args:string[]) {
  const p=Bun.spawn(args,{stdout:"pipe",stderr:"pipe"});
  const [out,err]=await Promise.all([new Response(p.stdout).text(),new Response(p.stderr).text()]);
  if(await p.exited) throw Error(err.trim()||`${args[0]} failed`);
  return out;
}
const state=await Bun.file(join(root,"evidence/hover-state.json")).json();
const age=Date.now()-Date.parse(state.observedUTC);
if(!Number.isFinite(age) || age<0 || age>2500) throw Error("Hover state is stale");
const args=process.argv.slice(2);
if(args.length && (args.length!==5 || args[0]!=="--bounds")) throw Error("Usage: surface-check.ts [--bounds x y width height]");
const bounds=args.length ? args.slice(1).map(Number) : state.lastPreviewBounds;
if(!Array.isArray(bounds) || bounds.length!==4 || !bounds.every(Number.isInteger)) throw Error("Hover an overlay card once before sampling");
const clients=JSON.parse(await command(["hyprctl","-j","clients"]));
const overlays=clients.filter((x:any)=>x.title==="HearthstoneOverlay" && x.class===windowClass);
const mains=clients.filter((x:any)=>x.title==="Hearthstone Deck Tracker" && x.class===windowClass);
if(overlays.length!==1 || mains.length!==1 || overlays[0].pid!==mains[0].pid) throw Error("Ambiguous HDT identity");
const tree=await command(["xwininfo","-root","-tree"]);
const ids=[...tree.matchAll(/^\s*(0x[0-9a-f]+) "HearthstoneOverlay":/gm)].map(x=>x[1]);
if(ids.length!==1) throw Error("Ambiguous X11 overlay identity");
const surface=JSON.parse(await command([join(nativeDirectory,"x-surface-probe"),ids[0],String(overlays[0].pid),...bounds.map(String)]));
const result={observedUTC:new Date().toISOString(),bounds,regionSource:args.length?"explicit":"lastPreview",state,surface};
await Bun.write(join(root,"evidence/surface-check.json"),JSON.stringify(result,null,2));
await appendFile(join(root,"evidence/surface-samples.jsonl"),JSON.stringify(result)+"\n");
console.log(JSON.stringify(result,null,2));
