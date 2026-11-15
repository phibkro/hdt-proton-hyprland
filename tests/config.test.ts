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
