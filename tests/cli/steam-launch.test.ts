/* eslint-disable no-await-in-loop -- Fixture setup serializes file creation before launching the subprocess under test. */
import { expect, test } from "bun:test";
import { mkdtemp, mkdir, chmod, rm, symlink, readFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import nodePath from "node:path";

const root = nodePath.resolve(import.meta.dir, "../..");

for (const serviceExit of [0, 1]) {
  test(`Steam argv and exit status survive service status ${serviceExit}`, async () => {
    const temp = await mkdtemp(nodePath.join(tmpdir(), "hdt-steam-test-"));

    try {
      const ctl = nodePath.join(temp, "systemctl");
      await Bun.write(
        ctl,
        `#!/bin/sh\nprintf '%s\\n' "$@" > "$TEST_SERVICE_CALL"\nexit ${serviceExit}\n`
      );
      await chmod(ctl, 0o700);

      const argv = [
        "spaces in path",
        "literal $(exit 99)",
        "quote'\"",
        "",
        "; exit 98",
      ];

      const output = nodePath.join(temp, "argv.json");

      const p = Bun.spawn(
        [
          "bash",
          nodePath.join(root, "scripts/steam-launch.sh"),
          process.execPath,
          "-e",
          "await Bun.write(process.env.TEST_ARGV!, JSON.stringify({args:process.argv.slice(1),pid:process.pid})); process.exit(23)",
          ...argv,
        ],
        {
          env: {
            ...process.env,
            PATH: `${temp}:${process.env.PATH}`,
            TEST_SERVICE_CALL: nodePath.join(temp, "service"),
            TEST_ARGV: output,
          },
          stdout: "pipe",
          stderr: "pipe",
        }
      );

      expect(await p.exited).toBe(23);
      expect(JSON.parse(await readFile(output, "utf-8"))).toEqual({
        args: argv,
        pid: p.pid,
      });
      expect(await readFile(nodePath.join(temp, "service"), "utf-8")).toBe(
        "--user\nstart\nhdt-proton-autostart.service\n"
      );
      const stderr = await new Response(p.stderr).text();
      expect(stderr.includes("service unavailable")).toBe(serviceExit !== 0);
    } finally {
      await rm(temp, { recursive: true, force: true });
    }
  });
}

test("Launch Options quote an absolute checkout containing spaces and apostrophes", async () => {
  const temp = await mkdtemp(nodePath.join(tmpdir(), "hdt-options-"));

  try {
    const checkout = nodePath.join(temp, "tracker's folder");
    await mkdir(nodePath.join(checkout, "scripts"), { recursive: true });
    await symlink(
      nodePath.join(root, "scripts/steam-options.sh"),
      nodePath.join(checkout, "scripts/steam-options.sh")
    );
    // A harmless stand-in records the parsed path and Steam placeholder argument.
    await Bun.write(
      nodePath.join(checkout, "scripts/steam-launch.sh"),
      'printf "%s\\n" "$0" "$@"'
    );

    const options = Bun.spawn(
      ["bash", nodePath.join(checkout, "scripts/steam-options.sh")],
      { stdout: "pipe" }
    );

    const line = await new Response(options.stdout).text();
    expect(await options.exited).toBe(0);
    const parsed = Bun.spawn(["bash", "-c", line], { stdout: "pipe" });
    expect(await new Response(parsed.stdout).text()).toBe(
      `${checkout}/scripts/steam-launch.sh\n%command%\n`
    );
    expect(await parsed.exited).toBe(0);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});

test("wrapper without Steam command refuses to start the service", async () => {
  const p = Bun.spawn(
    ["bash", nodePath.join(root, "scripts/steam-launch.sh")],
    {
      stdout: "pipe",
      stderr: "pipe",
    }
  );

  expect(await p.exited).toBe(2);
});

for (const failingStep of ["none", "build", "install"]) {
  test(`setup composes existing tools and stops at ${failingStep}`, async () => {
    const temp = await mkdtemp(nodePath.join(tmpdir(), "hdt-setup-test-"));

    try {
      const log = nodePath.join(temp, "calls");

      for (const tool of ["hdt-launch", "hdt-native"]) {
        const path = nodePath.join(temp, tool);
        await Bun.write(
          path,
          `#!/bin/sh
printf '%s %s\\n' '${tool}' "$*" >> "$TEST_CALLS"
case "$TEST_FAIL:$1" in
  build:--build-plugin|install:install-plugin) exit 31 ;;
esac
`
        );
        await chmod(path, 0o700);
      }

      const p = Bun.spawn(["bash", nodePath.join(root, "scripts/setup.sh")], {
        cwd: temp,
        env: {
          ...process.env,
          PATH: `${temp}:${process.env.PATH}`,
          TEST_CALLS: log,
          HDT_HOST_BINARY: nodePath.join(temp, "hdt-native"),
          TEST_FAIL: failingStep,
        },
        stdout: "pipe",
        stderr: "pipe",
      });

      expect(await p.exited).toBe(failingStep === "none" ? 0 : 31);
      const contents = await readFile(log, "utf-8");
      const calls = contents.trim().split("\n");
      expect(calls).toEqual([
        "hdt-launch --build-plugin",
        ...(failingStep === "build" ? [] : ["hdt-native install-plugin"]),
        ...(failingStep === "none" ? ["hdt-native configure-overlay"] : []),
      ]);
    } finally {
      await rm(temp, { recursive: true, force: true });
    }
  });
}
