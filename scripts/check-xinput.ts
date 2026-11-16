import {
  mkdtempSync,
  mkdirSync,
  chmodSync,
  copyFileSync,
  writeFileSync,
  readFileSync,
  realpathSync,
} from "node:fs";
import { join } from "node:path";

import { main, root, run } from "./process";

main(() => {
  const evidence = join(root, "evidence");
  mkdirSync(evidence, { recursive: true });
  const capture = mkdtempSync(join(evidence, "xinput-race-"));
  chmodSync(capture, 0o700);

  for (const file of ["xinput-race.c", "xinput-race.ts"]) {
    copyFileSync(join(root, "tests", file), join(capture, file));
  }
  const flags = run(["pkg-config", "--cflags", "--libs", "x11", "xi"], {
    capture: true,
  })
    .trim()
    .split(/\s+/)
    .filter(Boolean);

  run([
    "cc",
    "-Wall",
    "-Wextra",
    "-Werror",
    join(capture, "xinput-race.c"),
    "-o",
    join(capture, "xinput-race"),
    ...flags,
  ]);

  const manifest = ["xinput-race.c", "xinput-race.ts", "xinput-race"]
    .map(
      (file) =>
        `${new Bun.CryptoHasher("sha256").update(readFileSync(join(capture, file))).digest("hex")}  ${file}\n`
    )
    .join("");

  writeFileSync(join(capture, "source-sha256.txt"), manifest);

  const tool = (name: string) => {
    const path = Bun.which(name);

    if (!path) {
      throw Error(`Required tool missing: ${name}`);
    }
    return realpathSync(path);
  };

  // Only this private capture and Nix store enter the namespace. No host
  // desktop sockets, game prefix, credentials, or input devices are bound.
  run([
    "bwrap",
    "--unshare-all",
    "--die-with-parent",
    "--new-session",
    "--clearenv",
    "--ro-bind",
    "/nix/store",
    "/nix/store",
    "--proc",
    "/proc",
    "--dev",
    "/dev",
    "--tmpfs",
    "/tmp",
    "--dir",
    "/bin",
    "--ro-bind",
    tool("bash"),
    "/bin/sh",
    "--bind",
    capture,
    "/capture",
    "--chdir",
    "/capture",
    "--setenv",
    "HOME",
    "/tmp",
    tool("bun"),
    "/capture/xinput-race.ts",
    "/capture",
    tool("Xvfb"),
  ]);
  console.log(
    `Private XInput reproduction passed. Evidence: ${capture}/results`
  );
});
