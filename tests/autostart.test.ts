import {test,expect} from "bun:test";
import {mkdtemp,mkdir,writeFile,readFile,chmod,rm} from "node:fs/promises";
import {join,resolve} from "node:path";
import {tmpdir} from "node:os";

// Only compositor snapshots and time are simulated. The watcher, launcher,
// process identity checks, lock, and child exit statuses are real processes.
type Fixture={dir:string;clients:string;start:()=>ReturnType<typeof Bun.spawn>;launches:()=>Promise<number[][]>;wait:(f:()=>Promise<boolean>)=>Promise<void>;processes:ReturnType<typeof Bun.spawn>[]};
async function fixture(mode:string,run:(f:Fixture)=>Promise<void>) {
  const dir=await mkdtemp(join(tmpdir(),"hdt-watch-"));
  const processes:ReturnType<typeof Bun.spawn>[]=[];
  const clients=join(dir,"clients"),calls=join(dir,"calls");
  await mkdir(join(dir,"runtime"));await writeFile(clients,"[]");await writeFile(join(dir,"clock"),"100");
  const sources={
    ...(mode==="lock-error"?{flock:"exit 73"}:{}),
    bun:'printf "%s\\n" steam_app_123',
    hyprctl:'cat "$TEST_DIR/clients"',
    date:'cat "$TEST_DIR/clock"',
    sleep:`clock=$(cat "$TEST_DIR/clock"); printf '%s\\n' "$((clock+2))" > "$TEST_DIR/clock"; exec '${Bun.which("sleep")}' 0.03`,
    "hdt-overlay-launch":`printf '%s %s\\n' "$$" "$(cat "$TEST_DIR/clock")" >> "$TEST_DIR/calls"
case "$TEST_MODE" in
  crash) exit 1 ;;
  normal) exit 0 ;;
  recover) if [ "$(wc -l < "$TEST_DIR/calls")" -lt 3 ]; then exit 1; fi; exec "$TEST_BUN" -e 'setInterval(()=>{},1000)' ;;
  hold) exec "$TEST_BUN" -e 'setInterval(()=>{},1000)' ;;
esac`,
  };
  for(const [name,source] of Object.entries(sources)) {
    const file=join(dir,name);await writeFile(file,`#!/bin/sh\n${source}\n`);await chmod(file,0o700);
  }
  const env={...process.env,PATH:`${dir}:${process.env.PATH}`,XDG_RUNTIME_DIR:join(dir,"runtime"),TEST_DIR:dir,TEST_MODE:mode,TEST_BUN:process.execPath};
  const start=()=>{const p=Bun.spawn(["bash",resolve(import.meta.dir,"../scripts/autostart.sh")],{env,stdin:"ignore",stdout:"pipe",stderr:"pipe"});processes.push(p);return p};
  const launches=async()=>{try{return (await readFile(calls,"utf8")).trim().split("\n").filter(Boolean).map(s=>s.split(" ").map(Number))}catch{return []}};
  const wait=async(f:()=>Promise<boolean>)=>{
    for(let i=0;i<150;i++){
      if(await f())return;
      if(processes[0]?.exitCode!==null && processes[0]?.exitCode!==undefined)
        throw Error(`Fixture process exited (${processes[0].exitCode}): ${await new Response(processes[0].stderr).text()}`);
      await Bun.sleep(20);
    }
    throw Error("Watcher fixture timed out");
  };
  try {await run({dir,clients,start,launches,wait,processes});}
  finally {
    for(const p of processes) if(p.exitCode===null) {p.kill("SIGTERM");await p.exited;}
    // Launcher stand-ins belong to this fixture. Never signal a snapshot's PID.
    for(const [pid] of await launches()) {
      try {
        const environment=(await readFile(`/proc/${pid}/environ`,"utf8")).split("\0");
        if(environment.includes(`TEST_DIR=${dir}`)) process.kill(pid,"SIGTERM");
      } catch(error) {if(!["ESRCH","ENOENT"].includes((error as NodeJS.ErrnoException).code!))throw error}
    }
    await rm(dir,{recursive:true,force:true});
  }
}
const game=(pid=123)=>({class:"steam_app_123",title:"Hearthstone",pid});

test("lock tool failures cannot be reported as a successful existing owner",()=>fixture("lock-error",async f=>{
  await writeFile(f.clients,JSON.stringify([game()]));const watcher=f.start();
  expect(await watcher.exited).toBe(73);
  expect(await new Response(watcher.stderr).text()).toContain("Could not acquire");
  expect(await f.launches()).toEqual([]);
}));

test("owned launcher crashes retry with backoff, stop after three failures, and reset for a new game",()=>fixture("crash",async f=>{
  await writeFile(f.clients,JSON.stringify([game()]));const watcher=f.start();
  await f.wait(async()=>(await f.launches()).length===3);await Bun.sleep(160);
  const first=await f.launches();expect(first.length).toBe(3);
  expect(first[1][1]-first[0][1]).toBeGreaterThanOrEqual(5);
  expect(first[2][1]-first[1][1]).toBeGreaterThanOrEqual(10);
  await writeFile(f.clients,JSON.stringify([game(124)]));
  await f.wait(async()=>(await f.launches()).length===6);await Bun.sleep(120);
  expect((await f.launches()).length).toBe(6);
  watcher.kill("SIGTERM");await watcher.exited;
  expect(await new Response(watcher.stdout).text()).toContain("failed three times");
}));

test("a launcher that survives a minute gets a fresh failure budget",()=>fixture("recover",async f=>{
  await writeFile(f.clients,JSON.stringify([game()]));f.start();
  await f.wait(async()=>(await f.launches()).length===3);
  const third=(await f.launches())[2];
  await f.wait(async()=>Number(await readFile(join(f.dir,"clock"),"utf8"))-third[1]>=64);
  process.kill(third[0],"SIGTERM");
  await f.wait(async()=>(await f.launches()).length===4);
  expect((await f.launches()).length).toBe(4);
}));

test("normal tracker exit stays closed until a new game process appears",()=>fixture("normal",async f=>{
  await writeFile(f.clients,JSON.stringify([game()]));f.start();
  await f.wait(async()=>(await f.launches()).length===1);await Bun.sleep(200);
  expect((await f.launches()).length).toBe(1);
  await writeFile(f.clients,JSON.stringify([game(124)]));
  await f.wait(async()=>(await f.launches()).length===2);
}));

test("watcher neither duplicates an in-flight launcher nor keeps its lock in the child",()=>fixture("hold",async f=>{
  await writeFile(f.clients,JSON.stringify([game()]));const watcher=f.start();
  await f.wait(async()=>(await f.launches()).length===1);await Bun.sleep(150);
  expect((await f.launches()).length).toBe(1);
  const child=(await f.launches())[0][0];process.kill(child,"SIGSTOP");
  try {
    const before=Number(await readFile(join(f.dir,"clock"),"utf8"));
    await f.wait(async()=>Number(await readFile(join(f.dir,"clock"),"utf8"))>=before+4);
    expect((await f.launches()).length).toBe(1);expect(watcher.exitCode).toBeNull();
  }
  finally {process.kill(child,"SIGCONT");}
  const duplicate=f.start();expect(await duplicate.exited).toBe(0);
  expect(await new Response(duplicate.stdout).text()).toContain("already has an owner");
  watcher.kill("SIGTERM");await watcher.exited;
  // No game means a replacement watcher can acquire the lock without launching.
  await writeFile(f.clients,"[]");const replacement=f.start();await Bun.sleep(100);
  expect(replacement.exitCode).toBeNull();expect((await f.launches()).length).toBe(1);
}));

test("compositor titles cannot make a non-tracker process count as HDT",()=>fixture("normal",async f=>{
  const tracker={class:"steam_app_123",title:"Hearthstone Deck Tracker",pid:process.pid};
  await writeFile(f.clients,JSON.stringify([game(),tracker]));f.start();
  await f.wait(async()=>(await f.launches()).length===1);
  expect((await readFile(`/proc/${process.pid}/comm`,"utf8")).trim()).not.toBe("HearthstoneDeck");
}));

test("watcher leaves an existing live tracker and the game process untouched",()=>fixture("normal",async f=>{
  const tracker=Bun.spawn(["bash","-c","printf '%s\\n' HearthstoneDeck > /proc/$$/comm; while read -r line; do :; done"],{stdin:"pipe",stdout:"pipe",stderr:"pipe"});
  f.processes.push(tracker);
  await f.wait(async()=>(await readFile(`/proc/${tracker.pid}/comm`,"utf8")).trim()==="HearthstoneDeck");
  await writeFile(f.clients,JSON.stringify([game(process.pid),{class:"steam_app_123",title:"Hearthstone Deck Tracker",pid:tracker.pid}]));
  f.start();await Bun.sleep(180);expect(await f.launches()).toEqual([]);
  expect(tracker.exitCode).toBeNull();expect(process.kill(process.pid,0)).toBe(true);
  tracker.kill("SIGTERM");await tracker.exited;await Bun.sleep(120);
  expect(await f.launches()).toEqual([]);
}));

for(const snapshot of [[],[game(),game(124)],[game(-1)]]) test(`watcher refuses an absent or ambiguous game: ${JSON.stringify(snapshot.map(g=>g.pid))}`,()=>fixture("normal",async f=>{
  await writeFile(f.clients,JSON.stringify(snapshot));const watcher=f.start();await Bun.sleep(150);
  expect(await f.launches()).toEqual([]);
  if(snapshot.length) expect(await watcher.exited).not.toBe(0);
  else expect(watcher.exitCode).toBeNull();
}));
