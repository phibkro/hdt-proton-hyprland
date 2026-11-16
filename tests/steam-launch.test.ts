import { expect, test } from "bun:test";
import { mkdtemp, mkdir, chmod, rm, symlink, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const root = resolve(import.meta.dir, "..");
for (const serviceExit of [0, 1]) {
  test(`Steam argv and exit status survive service status ${serviceExit}`, async () => {
    const temp = await mkdtemp(join(tmpdir(), "hdt-steam-test-"));
    try {
      const ctl = join(temp, "systemctl");
      await Bun.write(ctl, `#!/bin/sh\nprintf '%s\\n' "$@" > "$TEST_SERVICE_CALL"\nexit ${serviceExit}\n`);
      await chmod(ctl, 0o700);
      const argv = ["spaces in path", "literal $(exit 99)", "quote'\"", "", "; exit 98"];
      const output = join(temp, "argv.json");
      const p = Bun.spawn([process.execPath, join(root, "scripts/steam-launch.ts"), process.execPath,
        "-e", 'await Bun.write(process.env.TEST_ARGV!, JSON.stringify({args:process.argv.slice(1),pid:process.pid})); process.exit(23)', ...argv], {
        env: { ...process.env, PATH: `${temp}:${process.env.PATH}`, TEST_SERVICE_CALL: join(temp, "service"), TEST_ARGV: output },
        stdout: "pipe", stderr: "pipe",
      });
      expect(await p.exited).toBe(23);
      expect(JSON.parse(await readFile(output, "utf8"))).toEqual({args:argv,pid:p.pid});
      expect(await readFile(join(temp, "service"), "utf8")).toBe("--user\nstart\nhdt-proton-autostart.service\n");
      expect((await new Response(p.stderr).text()).includes("service unavailable")).toBe(serviceExit !== 0);
    } finally { await rm(temp, { recursive: true, force: true }); }
  });
}

test("Launch Options quote an absolute checkout containing spaces and apostrophes", async () => {
  const temp = await mkdtemp(join(tmpdir(), "hdt-options-"));
  try {
    const checkout = join(temp, "tracker's folder");
    await mkdir(join(checkout, "scripts"), { recursive: true });
    await symlink(join(root, "scripts/steam-options.sh"), join(checkout, "scripts/steam-options.sh"));
    // A harmless stand-in records the parsed path and Steam placeholder argument.
    await Bun.write(join(checkout, "scripts/steam-launch.sh"), 'printf "%s\\n" "$0" "$@"');
    const options = Bun.spawn(["bash", join(checkout, "scripts/steam-options.sh")], { stdout: "pipe" });
    const line = await new Response(options.stdout).text();
    expect(await options.exited).toBe(0);
    const parsed = Bun.spawn(["bash", "-c", line], { stdout: "pipe" });
    expect(await new Response(parsed.stdout).text()).toBe(`${checkout}/scripts/steam-launch.sh\n%command%\n`);
    expect(await parsed.exited).toBe(0);
  } finally { await rm(temp, { recursive: true, force: true }); }
});

test("wrapper without Steam command refuses to start the service", async () => {
  const p = Bun.spawn([process.execPath, join(root, "scripts/steam-launch.ts")], { stdout: "pipe", stderr: "pipe" });
  expect(await p.exited).toBe(2);
});

for (const failingStep of ["none", "build", "install"]) {
  test(`setup composes existing tools and stops at ${failingStep}`, async () => {
    const temp = await mkdtemp(join(tmpdir(), "hdt-setup-test-"));
    try {
      const log = join(temp, "calls");
      for (const tool of ["hdt-launch", "bun"]) {
        const path = join(temp, tool);
        await Bun.write(path, `#!/bin/sh
printf '%s %s\\n' '${tool}' "$*" >> "$TEST_CALLS"
case "$TEST_FAIL:$1" in
  build:--build-plugin|install:src/runtime/install-plugin.ts) exit 31 ;;
esac
`);
        await chmod(path, 0o700);
      }
      const p = Bun.spawn([process.execPath, join(root, "scripts/setup.ts")], {
        cwd: temp,
        env: { ...process.env, PATH: `${temp}:${process.env.PATH}`, TEST_CALLS: log, TEST_FAIL: failingStep },
        stdout: "pipe", stderr: "pipe",
      });
      expect(await p.exited).toBe(failingStep === "none" ? 0 : 31);
      const calls = (await readFile(log, "utf8")).trim().split("\n");
      expect(calls).toEqual([
        "hdt-launch --build-plugin",
        ...(failingStep !== "build" ? ["bun src/runtime/install-plugin.ts"] : []),
        ...(failingStep === "none" ? ["bun src/runtime/configure-overlay.ts"] : []),
      ]);
    } finally { await rm(temp, { recursive: true, force: true }); }
  });
}
