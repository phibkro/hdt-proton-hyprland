import { expect, test } from "bun:test";
import { parseAppId } from "./config";

test("accepts Steam's uint32 shortcut IDs without coercion",()=>{
  expect(parseAppId("1")).toBe("1");
  expect(parseAppId("4294967295")).toBe("4294967295");
});
test("rejects IDs that could target another prefix or inject compositor code",()=>{
  for(const value of [undefined,null,1,"","0","01","-1","4294967296","1/pfx","1\n","1;error()"])
    expect(()=>parseAppId(value)).toThrow();
});
