import {expect,test} from "bun:test";
import {mkdtemp,chmod,readFile,rm} from "node:fs/promises";
import {join,resolve} from "node:path";
import {tmpdir} from "node:os";

const root=resolve(import.meta.dir,"..");
for(const scenario of ["restart","game-owned","no-game","ambiguous"]) {
  test(`desktop action preserves the game boundary: ${scenario}`,async()=>{
    const dir=await mkdtemp(join(tmpdir(),"hdt-desktop-test-"));
    try {
      const group=(await readFile(`/proc/${process.pid}/cgroup`,"utf8")).trim().split("::").at(-1)!;
      const game={class:"steam_app_123",title:"Hearthstone",pid:process.pid};
      const tracker={class:"steam_app_123",title:"Hearthstone Deck Tracker",pid:process.pid};
      await Bun.write(join(dir,"clients"),JSON.stringify(scenario==="ambiguous"?[tracker,tracker]:scenario==="no-game"?[]:[game]));
      for(const [name,source] of Object.entries({
        bun:'printf "%s\\n" steam_app_123',
        hyprctl:'cat "$TEST_DIR/clients"',
        systemctl:`if [ "$2" = show ]; then printf '%s\\n' "$TEST_GROUP"; else printf '%s\\n' "$*" >> "$TEST_DIR/calls"; fi`,
        "notify-send":'exit 0',
      })) {
        const file=join(dir,name);await Bun.write(file,`#!/bin/sh\n${source}\n`);await chmod(file,0o700);
      }
      const p=Bun.spawn(["bash",join(root,"scripts/desktop.sh"),"restart"],{
        env:{...process.env,PATH:`${dir}:${process.env.PATH}`,TEST_DIR:dir,TEST_GROUP:scenario==="game-owned"?group:"/separate-hdt-service"},
        stdout:"pipe",stderr:"pipe",
      });
      const rejected=["game-owned","ambiguous"].includes(scenario);
      expect(await p.exited).toBe(rejected?1:0);
      if(rejected) expect(await Bun.file(join(dir,"calls")).exists()).toBe(false);
      else expect(await readFile(join(dir,"calls"),"utf8")).toBe(`--user ${scenario==="no-game"?"start":"restart"} hdt-proton-autostart.service\n`);
    } finally {await rm(dir,{recursive:true,force:true});}
  });
}
