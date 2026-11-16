import assert from "node:assert/strict";

const [binary] = process.argv.slice(2);

if (!binary) {
  throw new Error("Expected the freshly compiled surface probe path");
}

const valid = ["1", "1", "0", "0", "32", "32"];

const invalid = [
  [0, ""],
  [0, "0"],
  [0, "-1"],
  [0, "4294967296"],
  [1, "0"],
  [1, "99999999999999999999999999999999999"],
  [2, "2147483648"],
  [2, "-2147483649"],
  [2, "oops"],
  [3, "2147483648"],
  [4, "0"],
  [4, "8193"],
  [5, "-1"],
] as const;

for (const [index, value] of invalid) {
  const args = [...valid];
  args[index] = value;

  const child = Bun.spawnSync([binary, ...args], {
    env: { ...process.env, DISPLAY: "" },
  });

  assert.equal(child.exitCode, 1);
  assert.equal(
    child.stderr.toString(),
    "HDT surface probe: invalid argument\n"
  );
}

for (const coordinate of ["-2147483648", "2147483647"]) {
  const args = [...valid];
  args[2] = coordinate;

  const child = Bun.spawnSync([binary, ...args], {
    env: { ...process.env, DISPLAY: "" },
  });

  assert.equal(child.exitCode, 1);
  assert.equal(
    child.stderr.toString(),
    "HDT surface probe: cannot open display\n"
  );
}

console.log(
  "Surface probe: 13 malformed/overflowing inputs rejected; both int coordinate boundaries parsed without contacting a display."
);
