import {test,expect} from "bun:test";
import {InputJournal} from "../src/diagnostics/input-journal";
import {mkdtemp,writeFile,appendFile,readFile,rename,rm,stat,symlink,mkdir} from "node:fs/promises";
import {join,resolve} from "node:path";

async function fixture(run:(root:string)=>Promise<void>) {
  const base=resolve(import.meta.dir,"../evidence");await mkdir(base,{recursive:true});
  const root=await mkdtemp(join(base,"journal-test-"));
  try {await run(root);} finally {await rm(root,{recursive:true,force:true});}
}
const records=async(root:string)=> (await readFile(join(root,"journal.jsonl"),"utf8")).trim().split("\n").map(s=>JSON.parse(s));
const recovered=(rows:any[],generation:number)=>Buffer.concat(rows.filter(r=>r.kind==="source.bytes" && r.data.generation===generation).map(r=>Buffer.from(r.data.base64,"base64")));

test("journal preserves exact bytes across partial UTF-8 writes and rotation",()=>fixture(async root=>{
  const source=join(root,"hdt.log"),output=join(root,"journal");
  const bytes=Buffer.from("Info|Game ended\r\n雪\n");
  await writeFile(source,bytes.subarray(0,bytes.length-2));
  const journal=new InputJournal(output,[{name:"hdt",path:source}],"start",1048576);
  await journal.start();await journal.poll();
  await appendFile(source,bytes.subarray(bytes.length-2));await journal.poll();
  await rename(source,source+".old");await writeFile(source,"new session\n");await journal.poll();
  await journal.close("test");
  const rows=await records(output);
  expect(recovered(rows,0)).toEqual(bytes);
  expect(recovered(rows,1).toString()).toBe("new session\n");
  expect(rows.find(r=>r.kind==="source.replaced").data.continuity).toBe("unknown");
  expect((await stat(output)).mode&0o077).toBe(0);
  expect((await stat(join(output,"journal.jsonl"))).mode&0o077).toBe(0);
}));

test("same-inode rewrite and truncation start explicit generations",()=>fixture(async root=>{
  const source=join(root,"hdt.log"),output=join(root,"journal");await writeFile(source,"old body\n");
  const journal=new InputJournal(output,[{name:"hdt",path:source}],"start",1048576);
  await journal.start();await journal.poll();await writeFile(source,"replaced with a longer body\n");await journal.poll();
  await writeFile(source,"x");await journal.poll();await journal.close("test");
  const rows=await records(output);
  expect(rows.filter(r=>["source.rewritten","source.truncated"].includes(r.kind)).map(r=>r.kind)).toEqual(["source.rewritten","source.truncated"]);
  expect(recovered(rows,1).toString()).toBe("replaced with a longer body\n");
  expect(recovered(rows,2).toString()).toBe("x");
}));

test("capacity failure is explicit, bounded, and preserves acquired evidence",()=>fixture(async root=>{
  const source=join(root,"hdt.log"),output=join(root,"journal");await writeFile(source,Buffer.alloc(100000,65));
  const journal=new InputJournal(output,[{name:"hdt",path:source}],"start",65536);
  await journal.start();await journal.poll();
  await expect(journal.poll()).rejects.toThrow("capacity exhausted");
  await journal.close("capacity exhausted");
  const rows=await records(output);
  expect(rows.at(-1)).toMatchObject({kind:"journal.stop",data:{reason:"capacity exhausted"}});
  expect((await stat(join(output,"journal.jsonl"))).size).toBeLessThanOrEqual(65536);
  expect(recovered(rows,0)).toEqual(Buffer.alloc(32768,65));
}));

test("collector refuses existing evidence and nonregular sources",()=>fixture(async root=>{
  const output=join(root,"journal");await mkdir(output);
  await expect(new InputJournal(output,[{name:"hdt",path:join(root,"missing")}],"start",65536).start()).rejects.toThrow("EEXIST");
  const target=join(root,"file"),source=join(root,"link");await writeFile(target,"private");await symlink(target,source);
  const journal=new InputJournal(join(root,"second"),[{name:"hdt",path:source}],"start",65536);
  await journal.start();await expect(journal.poll()).rejects.toThrow("regular file");await journal.close("rejected source");
}));

test("end starts after existing bytes but captures a source created after observation began",()=>fixture(async root=>{
  const existing=join(root,"existing"),created=join(root,"created"),output=join(root,"journal");
  await writeFile(existing,"history");
  const journal=new InputJournal(output,[{name:"old",path:existing},{name:"new",path:created}],"end",1048576);
  await journal.start();await journal.poll();await appendFile(existing,"next");await writeFile(created,"first");await journal.poll();await journal.close("test");
  const rows=await records(output),captured=rows.filter(r=>r.kind==="source.bytes").map(r=>[r.data.source,Buffer.from(r.data.base64,"base64").toString()]);
  expect(captured).toEqual([["old","next"],["new","first"]]);
}));

test("native collector survives its observed producer stopping, dying and restarting",()=>fixture(async root=>{
  const source=join(root,"game.log"),hdt=join(root,"hdt.log"),output=join(root,"journal");
  await writeFile(source,"");await writeFile(hdt,"");
  const collector=Bun.spawn([process.execPath,resolve(import.meta.dir,"../src/diagnostics/input-journal.ts"),"--output",output,"--from","start","--max-bytes","1048576","--source",`game=${source}`,"--source",`hdt=${hdt}`],{stdout:"pipe",stderr:"pipe"});
  let producer:ReturnType<typeof Bun.spawn>|undefined;
  const waitFor=async(predicate:()=>Promise<boolean>)=>{
    for(let i=0;i<60;i++) {if(await predicate()) return;await Bun.sleep(50);}
    throw Error("Timed out waiting for real process evidence.");
  };
  try {
    await waitFor(async()=>Bun.file(join(output,"journal.jsonl")).exists());
    producer=Bun.spawn([process.execPath,"-e","const fs=require('node:fs'); fs.appendFileSync(process.argv[1],'running\\n'); setInterval(()=>{},1000)",hdt],{stdout:"pipe",stderr:"pipe"});
    await waitFor(async()=>(await readFile(hdt,"utf8")).includes("running"));
    producer.kill("SIGSTOP");
    await appendFile(source,"game after tracker stall\n");
    await waitFor(async()=>(await records(output)).some(r=>r.kind==="source.bytes" && Buffer.from(r.data.base64,"base64").includes(Buffer.from("game after tracker stall"))));
    producer.kill("SIGKILL");await producer.exited;producer=undefined;
    await appendFile(source,"game after tracker exit\n");
    producer=Bun.spawn([process.execPath,"-e","require('node:fs').appendFileSync(process.argv[1],'restarted\\n')",hdt],{stdout:"pipe",stderr:"pipe"});
    expect(await producer.exited).toBe(0);producer=undefined;
    await waitFor(async()=>(await records(output)).some(r=>r.kind==="source.bytes" && Buffer.from(r.data.base64,"base64").includes(Buffer.from("restarted"))));
    collector.kill("SIGTERM");expect(await collector.exited).toBe(0);
    const rows=await records(output);
    expect(rows.filter(r=>r.kind==="source.bytes" && r.data.source==="game").map(r=>Buffer.from(r.data.base64,"base64").toString()).join("")).toContain("game after tracker exit");
    expect(rows.at(-1)).toMatchObject({kind:"journal.stop",data:{reason:"signal"}});
  } finally {
    if(producer) {producer.kill("SIGKILL");await producer.exited;}
    if(collector.exitCode===null) {collector.kill("SIGKILL");await collector.exited;}
  }
}));
