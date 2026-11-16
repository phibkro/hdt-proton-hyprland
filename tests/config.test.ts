import { expect, test } from "bun:test";
import { parseAppId } from "../src/runtime/config";

test("accepts Steam's uint32 shortcut IDs without coercion",()=>{
  expect(parseAppId("1")).toBe("1");
  expect(parseAppId("4294967295")).toBe("4294967295");
});
test("rejects IDs that could target another prefix or inject compositor code",()=>{
  for(const value of [undefined,null,1,"","0","01","-1","4294967296","1/pfx","1\n","1;error()"])
    expect(()=>parseAppId(value)).toThrow();
});

test("configuration uses the checkout when invoked from another directory",async()=>{
  const expected = new URL("..", import.meta.url);
  const p = Bun.spawn([process.execPath, "-e",
    `import {projectRoot} from ${JSON.stringify(new URL("../src/runtime/config.ts", import.meta.url).href)}; console.log(projectRoot)`],
    {cwd:"/tmp",env:{...process.env,HS_APP_ID:"1234567890"},stdout:"pipe",stderr:"pipe"});
  expect(await p.exited).toBe(0);
  expect((await new Response(p.stdout).text()).trim()).toBe(decodeURIComponent(expected.pathname).replace(/\/$/, ""));
});

test("installed source reads configuration and app data from the explicit mutable directory",async()=>{
  const {mkdtemp,rm} = await import("node:fs/promises");
  const dir = await mkdtemp("/tmp/hdt-config-");
  try {
    await Bun.write(`${dir}/hdt.local.json`,JSON.stringify({steamAppId:"6789",steamRoot:`${dir}/steam`}));
    const env: NodeJS.ProcessEnv={...process.env,HDT_PROJECT_DIR:dir};
    delete env.HS_APP_ID;
    delete env.STEAM_ROOT;
    delete env.HDT_APP_DIR;
    delete env.HDT_NATIVE_DIR;
    const p=Bun.spawn([process.execPath,"-e",
      `import {projectRoot,sourceRoot,appId,appDirectory,nativeDirectory,prefix} from ${JSON.stringify(new URL("../src/runtime/config.ts",import.meta.url).href)};console.log(JSON.stringify({projectRoot,sourceRoot,appId,appDirectory,nativeDirectory,prefix}))`],
      {cwd:"/tmp",env,stdout:"pipe",stderr:"pipe"});
    expect(await p.exited).toBe(0);
    const value=JSON.parse(await new Response(p.stdout).text());
    expect(value.projectRoot).toBe(dir);
    expect(value.sourceRoot).not.toBe(dir);
    expect(value.appId).toBe("6789");
    expect(value.appDirectory).toBe(`${dir}/app`);
    expect(value.nativeDirectory).toBe(`${dir}/evidence`);
    expect(value.prefix).toBe(`${dir}/steam/steamapps/compatdata/6789/pfx`);
  } finally {await rm(dir,{recursive:true,force:true});}
});

for(const dir of ["relative/path","/nix/store/not-mutable"])
  test(`rejects a non-mutable data location: ${dir}`,async()=>{
    const p=Bun.spawn([process.execPath,"-e",`await import(${JSON.stringify(new URL("../src/runtime/config.ts",import.meta.url).href)})`],
      {env:{...process.env,HDT_PROJECT_DIR:dir},stdout:"pipe",stderr:"pipe"});
    expect(await p.exited).not.toBe(0);
  });
