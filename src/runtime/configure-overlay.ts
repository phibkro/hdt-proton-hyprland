import {
  readFile,
  readdir,
  copyFile,
  writeFile,
  mkdir,
} from "node:fs/promises";
import { join } from "node:path";

import { projectRoot, hdtData } from "./config";

for (const pid of (await readdir("/proc")).filter((x) => /^\d+$/.test(x))) {
  try {
    if (
      (await readFile(`/proc/${pid}/comm`, "utf-8")).trim() ===
      "HearthstoneDeck"
    ) {
      throw Error("Close HDT normally before changing its configuration");
    }
  } catch (error) {
    if (!["ENOENT", "EACCES"].includes((error as any).code)) {
      throw error;
    }
  }
}

const path = join(hdtData, "config.xml");

await mkdir(join(projectRoot, "backups/hdt-before-render"), {
  recursive: true,
  mode: 0o700,
});

try {
  await copyFile(
    path,
    join(
      projectRoot,
      "backups/hdt-before-render/config-before-background-test.xml"
    ),
    1
  );
} catch (error) {
  if ((error as any).code !== "EEXIST") {
    throw error;
  }
}

let xml = await readFile(path, "utf-8");

for (const key of [
  "HideInMenu",
  "HideInBackground",
  "HideMenuOverlayInBackground",
]) {
  const pattern = new RegExp(`<${key}>(true|false)</${key}>`, "g");

  if ([...xml.matchAll(pattern)].length !== 1) {
    throw Error(`Expected exactly one ${key} setting`);
  }
  xml = xml.replaceAll(pattern, `<${key}>false</${key}>`);
}

await writeFile(path, xml);

console.log(
  "Set HDT menu visibility and disabled HDT background hiding; compositor owns overlay stacking for this test."
);
