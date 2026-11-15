import { dirname, join, resolve } from "node:path";

export const projectRoot = dirname(import.meta.path);
export function parseAppId(value: unknown): string {
  if(typeof value!=="string" || !/^[1-9][0-9]{0,9}$/.test(value) || BigInt(value)>4294967295n)
    throw Error("Set HS_APP_ID to the numeric Steam shortcut app ID (uint32).");
  return value;
}
const file=Bun.file(join(projectRoot,"hdt.local.json"));
const local=await file.exists()?await file.json():{};
if(local===null || typeof local!=="object" || Array.isArray(local) || Object.keys(local).some(key=>!["steamAppId","steamRoot"].includes(key)))
  throw Error("hdt.local.json accepts only steamAppId and optional steamRoot.");
export const appId=parseAppId(process.env.HS_APP_ID??local.steamAppId);
const home=process.env.HOME;
if(!home) throw Error("HOME is required.");
const configuredSteam=process.env.STEAM_ROOT??local.steamRoot??join(home,".local/share/Steam");
if(typeof configuredSteam!=="string" || !configuredSteam.startsWith("/")) throw Error("STEAM_ROOT must be an absolute path.");
export const steamRoot=resolve(configuredSteam);
export const prefix=join(steamRoot,"steamapps/compatdata",appId,"pfx");
export const hdtData=join(prefix,"drive_c/users/steamuser/AppData/Roaming/HearthstoneDeckTracker");
export const appDirectory=resolve(process.env.HDT_APP_DIR??join(projectRoot,"app"));
export const windowClass=`steam_app_${appId}`;
