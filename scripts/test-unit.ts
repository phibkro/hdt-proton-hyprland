import { readdirSync } from "node:fs";

import { exec, root } from "./process";

process.chdir(root);

exec([
  process.execPath,
  "test",
  ...readdirSync("tests")
    .filter((file) => file.endsWith(".test.ts"))
    .sort()
    .map((file) => `./tests/${file}`),
]);
