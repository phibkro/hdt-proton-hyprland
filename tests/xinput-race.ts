import {mkdir,writeFile,lstat,readlink,readdir} from "node:fs/promises";
import {resolve,join} from "node:path";
if(process.argv.length!==4 || process.env.DISPLAY || process.env.WAYLAND_DISPLAY || process.env.XAUTHORITY)
 throw Error("Run through scripts/check-xinput.sh with the host desktop hidden.");
const root=resolve(process.argv[2]),out=join(root,"results");
if(root!=="/capture")throw Error("The fixture requires the private capture mount.");
for(const path of ["/home","/srv","/run/user"]) {
 try {await lstat(path);throw Error("A host directory is visible: "+path);}
 catch(error) {if((error as NodeJS.ErrnoException).code!=="ENOENT")throw error;}
}
await mkdir(out,{mode:0o700});
await mkdir("/tmp/.X11-unix",{recursive:true,mode:0o1777});
if((await readdir("/tmp/.X11-unix")).length)throw Error("The fixture requires an empty private X socket directory.");
const isolation={desktopEnvironmentAbsent:true,hostDirectoriesAbsent:true,initialXSocketCount:0,
 mountNamespace:await readlink("/proc/self/ns/mnt"),networkNamespace:await readlink("/proc/self/ns/net"),pidNamespace:await readlink("/proc/self/ns/pid")};
const server=Bun.spawn([process.argv[3],"-displayfd","1","-screen","0","640x480x24","-nolisten","tcp","-noreset"],{stdout:"pipe",stderr:"pipe"});
let startup:ReturnType<typeof setTimeout>|undefined;
try {
 const reader=server.stdout.getReader();
 const first=await Promise.race([reader.read(),new Promise<never>((_,reject)=>{startup=setTimeout(()=>reject(Error("Private X server startup timed out")),5000)})]);
 clearTimeout(startup);reader.releaseLock();
 const number=Buffer.from(first.value??[]).toString().trim();
 if(!/^\d+$/.test(number))throw Error("Private display allocation failed");
 const display=":"+number;
 const results=[];
 for(const [mode,expected] of [["fatal",1],["guarded",0],["valid",0],["unrelated",1],["wrong-serial",1],["bad-match",1]] as const) {
  const p=Bun.spawn([join(root,"xinput-race"),display,mode],{env:{...process.env,HDT_XINPUT_REPRO:"1"},stdout:"pipe",stderr:"pipe"});
  const timeout=setTimeout(()=>p.kill("SIGKILL"),5000);
  const [stdout,stderr,exit]=await Promise.all([new Response(p.stdout).text(),new Response(p.stderr).text(),p.exited]);clearTimeout(timeout);
  await writeFile(join(out,mode+".log"),stdout+stderr,{mode:0o600});
  results.push({mode,exit,expected,stdout,stderr});
  console.log(JSON.stringify({mode,exit,expected}));
  if(exit!==expected)throw Error("Unexpected fixture result: "+mode);
  if((mode==="fatal" || mode==="wrong-serial") && !stderr.includes("X_GetDeviceButtonMapping")) throw Error("Expected mapping failure missing");
  if(mode==="unrelated" && !stderr.includes("X_DestroyWindow")) throw Error("Unrelated error was suppressed");
  if(mode==="bad-match" && !stderr.includes("BadMatch")) throw Error("Wrong mapping error was suppressed");
  if(mode==="guarded" && !stdout.includes("caught=1")) throw Error("Expected mapping error was not caught");
  if(mode==="valid" && !stdout.includes("caught=0")) throw Error("Valid device was rejected");
 }
 await writeFile(join(out,"result.json"),JSON.stringify({display,isolation,results},null,2),{mode:0o600});
} finally {
 clearTimeout(startup);server.kill("SIGTERM");const exit=await server.exited;
 const stderr=await new Response(server.stderr).text();
 await writeFile(join(out,"xvfb.log"),stderr,{mode:0o600});
 if(exit!==0 && stderr)console.error(stderr);
}
