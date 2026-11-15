import {constants} from "node:fs";
import {open, mkdir, lstat} from "node:fs/promises";
import type {FileHandle} from "node:fs/promises";
import {resolve, join} from "node:path";

export type Source = {name:string; path:string};
type Cursor = Source & {handle?:FileHandle; identity?:string; offset:number; generation:number; anchor?:Buffer; unavailable?:boolean};

// Preserve source bytes. Interpretation belongs to the versioned HDT worker.
export class InputJournal {
  private cursors:Cursor[];
  private sink?:FileHandle;
  private bytes=0;
  private seq=0;
  private started=performance.now();
  private closed=false;
  private lastHeartbeat=-Infinity;
  constructor(private directory:string, sources:Source[], private from:"start"|"end", private maxBytes:number) {
    if(!Number.isSafeInteger(maxBytes) || maxBytes<65536) throw Error("Journal capacity must be at least 65536 bytes.");
    if(!sources.length || new Set(sources.map(s=>s.name)).size!==sources.length || new Set(sources.map(s=>resolve(s.path))).size!==sources.length)
      throw Error("Supply unique source names and paths.");
    for(const source of sources) if(!/^[a-z][a-z0-9-]*$/.test(source.name)) throw Error("Invalid source name.");
    this.cursors=sources.map(s=>({...s,path:resolve(s.path),offset:0,generation:0}));
    if(this.cursors.some(s=>s.path===join(resolve(directory),"journal.jsonl"))) throw Error("A journal cannot collect its own output.");
  }
  async start() {
    await mkdir(this.directory,{mode:0o700}); // Exclusive ownership; never adopt existing evidence.
    this.sink=await open(join(this.directory,"journal.jsonl"),"wx",0o600);
    await this.record("journal.start",{schema:1,from:this.from,maxBytes:this.maxBytes,sources:this.cursors.map(s=>({name:s.name,path:s.path}))});
  }
  private async record(kind:string,data:object,reserve=true) {
    if(!this.sink) throw Error("Journal is not open.");
    const line=Buffer.from(JSON.stringify({seq:++this.seq,utc:new Date().toISOString(),elapsedMS:performance.now()-this.started,kind,data})+"\n");
    if(this.bytes+line.length>this.maxBytes-(reserve?2048:0)) throw Error("Journal capacity exhausted.");
    // FileHandle.write can write fewer bytes than requested.
    for(let offset=0;offset<line.length;) {
      const result=await this.sink.write(line,offset,line.length-offset,null);
      if(!result.bytesWritten) throw Error("Journal write made no progress.");
      offset+=result.bytesWritten;
    }
    this.bytes+=line.length;
  }
  private async drain(cursor:Cursor) {
    const handle=cursor.handle!;
    const current=await handle.stat();
    if(current.size<cursor.offset) await this.reset(cursor,"source.truncated");
    else if(cursor.anchor) {
      const check=Buffer.alloc(cursor.anchor.length);
      const read=await handle.read(check,0,check.length,cursor.offset-check.length);
      if(read.bytesRead!==check.length || !check.equals(cursor.anchor)) await this.reset(cursor,"source.rewritten");
    }
    // One bounded chunk per poll per source prevents a hot Power.log starving HDT's log.
    const data=Buffer.alloc(Math.min(32768,Math.max(0,current.size-cursor.offset)));
    if(!data.length) return;
    const {bytesRead}=await handle.read(data,0,data.length,cursor.offset);
    if(!bytesRead) return;
    const bytes=data.subarray(0,bytesRead);
    await this.record("source.bytes",{source:cursor.name,generation:cursor.generation,identity:cursor.identity,offset:cursor.offset,base64:bytes.toString("base64")});
    cursor.offset+=bytesRead;
    cursor.anchor=Buffer.from(bytes.subarray(Math.max(0,bytes.length-64)));
  }
  private async reset(cursor:Cursor,kind:string) {
    await this.record(kind,{source:cursor.name,previousOffset:cursor.offset,generation:++cursor.generation,continuity:"unknown"});
    cursor.offset=0;cursor.anchor=undefined;
  }
  async poll() {
    if(this.closed) throw Error("Journal is closed.");
    const before=this.seq;
    for(const cursor of this.cursors) {
      let pathStat;
      try {pathStat=await lstat(cursor.path);} catch(error) {
        if((error as NodeJS.ErrnoException).code!=="ENOENT") throw error;
      }
      if(pathStat && !pathStat.isFile()) throw Error(`Source ${cursor.name} must be a regular file, not a symlink or device.`);
      const identity=pathStat?`${pathStat.dev}:${pathStat.ino}`:undefined;
      if(cursor.handle) {
        await this.drain(cursor);
        if(identity!==cursor.identity) {
          // Drain the old descriptor to its observed end before switching generations.
          const old=await cursor.handle.stat();
          if(cursor.offset<old.size) continue;
          await cursor.handle.close();cursor.handle=undefined;
          await this.reset(cursor,"source.replaced");
        }
      }
      if(!cursor.handle && pathStat) {
        const handle=await open(cursor.path,constants.O_RDONLY|constants.O_NOFOLLOW|constants.O_NONBLOCK);
        const actual=await handle.stat();
        if(!actual.isFile() || `${actual.dev}:${actual.ino}`!==identity) {await handle.close();throw Error(`Source ${cursor.name} changed while opening.`);}
        cursor.handle=handle;cursor.identity=identity;
        if(cursor.generation===0 && cursor.unavailable===undefined && this.from==="end") cursor.offset=actual.size;
        await this.record("source.open",{source:cursor.name,identity,offset:cursor.offset,generation:cursor.generation});
        cursor.unavailable=false;
        await this.drain(cursor);
      } else if(!pathStat && !cursor.unavailable) {
        await this.record("source.unavailable",{source:cursor.name});cursor.unavailable=true;
      }
    }
    if(performance.now()-this.lastHeartbeat>=1000) {
      await this.record("journal.heartbeat",{offsets:this.cursors.map(s=>({source:s.name,generation:s.generation,offset:s.offset,available:!!s.handle}))});
      this.lastHeartbeat=performance.now();
    }
    if(this.seq!==before) await this.sink!.sync();
  }
  async close(reason:string) {
    if(this.closed) return;
    this.closed=true;
    try {if(this.sink) {await this.record("journal.stop",{reason},false);await this.sink.sync();}}
    finally {await Promise.allSettled(this.cursors.map(s=>s.handle?.close()));await this.sink?.close();}
  }
}

if(import.meta.main) {
  let journal:InputJournal|undefined;
  try {
    const args=process.argv.slice(2), sources:Source[]=[];
    let directory:string|undefined,from:"start"|"end"|undefined,maxBytes:number|undefined;
    for(let i=0;i<args.length;i+=2) {
      const value=args[i+1];if(!value) throw Error("Each option requires a value.");
      switch(args[i]) {
        case "--output":if(directory) throw Error("Duplicate --output.");directory=resolve(value);break;
        case "--from":if(from || !["start","end"].includes(value)) throw Error("Choose --from start or end once.");from=value as "start"|"end";break;
        case "--max-bytes":if(maxBytes!==undefined || !/^[1-9][0-9]*$/.test(value)) throw Error("Invalid --max-bytes.");maxBytes=Number(value);break;
        case "--source":{const split=value.indexOf("=");if(split<1) throw Error("Use --source name=/absolute/file.");sources.push({name:value.slice(0,split),path:value.slice(split+1)});break;}
        default:throw Error(`Unknown option ${args[i]}.`);
      }
    }
    if(!directory || !from || maxBytes===undefined) throw Error("Usage: input-journal --output NEW_DIRECTORY --from start|end --max-bytes INTEGER --source name=FILE [...]");
    journal=new InputJournal(directory,sources,from,maxBytes);
    await journal.start();
    let stopping=false;
    process.on("SIGTERM",()=>{stopping=true;});process.on("SIGINT",()=>{stopping=true;});
    while(!stopping) {await journal.poll();await Bun.sleep(250);}
    await journal.close("signal");
  } catch(error) {
    const message=error instanceof Error?error.message:String(error);
    console.error(`Input journal failed: ${message}`);
    try {await journal?.close(message);} catch(closeError) {console.error("Could not finish journal:",closeError);}
    process.exitCode=1;
  }
}
