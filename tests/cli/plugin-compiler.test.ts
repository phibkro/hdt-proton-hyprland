import { expect, test } from "bun:test";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

// The Framework compiler rejects unknown directives even in inactive #if blocks.
// SDK analysis with LangVersion=5 does not model that legacy preprocessor seam.
const frameworkDirectives = new Set([
  "if",
  "elif",
  "else",
  "endif",
  "define",
  "undef",
  "warning",
  "error",
  "line",
  "region",
  "endregion",
  "pragma",
]);

test("production plugin sources use Framework-supported directives", async () => {
  const directory = path.resolve(import.meta.dir, "../../src/plugin");

  const entries = await readdir(directory);
  const files = entries.filter((name) => name.endsWith(".cs"));

  expect(files.length).toBeGreaterThan(0);

  const sources = await Promise.all(
    files.map(async (name) => ({
      name,
      source: await readFile(path.join(directory, name), "utf-8"),
    }))
  );

  for (const { name, source } of sources) {
    for (const directive of source.matchAll(/^\s*#\s*\S+/gmu)) {
      const keyword = directive[0].replace(/^\s*#\s*/u, "");

      expect({
        file: name,
        directive: keyword,
        supported: frameworkDirectives.has(keyword),
      }).toMatchObject({ supported: true });
    }
  }
});
