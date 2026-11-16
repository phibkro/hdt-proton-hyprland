import { mkdir, writeFile, lstat, readlink, readdir } from "node:fs/promises";
import path from "node:path";

if (
  process.argv.length !== 4 ||
  (process.env.DISPLAY ?? "") !== "" ||
  (process.env.WAYLAND_DISPLAY ?? "") !== "" ||
  (process.env.XAUTHORITY ?? "") !== ""
) {
  throw new Error(
    "Run through scripts/check-xinput.sh with the host desktop hidden."
  );
}

const root = path.resolve(process.argv[2]);
const out = path.join(root, "results");

if (root !== "/capture") {
  throw new Error("The fixture requires the private capture mount.");
}

// oxlint-disable no-await-in-loop -- Check isolation paths before starting the server, with an exact failing path.
for (const hostPath of ["/home", "/srv", "/run/user"]) {
  try {
    await lstat(hostPath);
    throw new Error(`A host directory is visible: ${hostPath}`);
  } catch (error) {
    if (
      !(error instanceof Error) ||
      !("code" in error) ||
      error.code !== "ENOENT"
    ) {
      throw error;
    }
  }
}

// oxlint-enable no-await-in-loop

await mkdir(out, { mode: 0o700 });

await mkdir("/tmp/.X11-unix", { recursive: true, mode: 0o1777 });

const initialSockets = await readdir("/tmp/.X11-unix");

if (initialSockets.length !== 0) {
  throw new Error("The fixture requires an empty private X socket directory.");
}

const isolation = {
  desktopEnvironmentAbsent: true,
  hostDirectoriesAbsent: true,
  initialXSocketCount: 0,
  mountNamespace: await readlink("/proc/self/ns/mnt"),
  networkNamespace: await readlink("/proc/self/ns/net"),
  pidNamespace: await readlink("/proc/self/ns/pid"),
};

const server = Bun.spawn(
  [
    process.argv[3],
    "-displayfd",
    "1",
    "-screen",
    "0",
    "640x480x24",
    "-nolisten",
    "tcp",
    "-noreset",
  ],
  { stdout: "pipe", stderr: "pipe" }
);

let startup: ReturnType<typeof setTimeout> | undefined;

try {
  const reader = server.stdout.getReader();

  const first = await Promise.race([
    reader.read(),
    // oxlint-disable-next-line promise/avoid-new -- Adapt the X-server stream startup deadline; retain its timer handle for unconditional cancellation.
    new Promise<never>((_resolve, reject) => {
      startup = setTimeout(() => {
        reject(new Error("Private X server startup timed out"));
      }, 5000);
    }),
  ]);

  clearTimeout(startup);
  reader.releaseLock();

  const number = Buffer.from(first.value ?? [])
    .toString()
    .trim();

  if (!/^\d+$/u.test(number)) {
    throw new Error("Private display allocation failed");
  }

  const display = `:${number}`;
  const results = [];

  // oxlint-disable no-await-in-loop -- Protocol cases share one private X server; serialize them to preserve error attribution and bounded capture order.
  for (const [mode, expected] of [
    ["fatal", 1],
    ["guarded", 0],
    ["valid", 0],
    ["unrelated", 1],
    ["wrong-serial", 1],
    ["bad-match", 1],
  ] as const) {
    const p = Bun.spawn([path.join(root, "xinput-race"), display, mode], {
      env: { ...process.env, HDT_XINPUT_REPRO: "1" },
      stdout: "pipe",
      stderr: "pipe",
    });

    const timeout = setTimeout(() => {
      p.kill("SIGKILL");
    }, 5000);

    const [stdout, stderr, exit] = await Promise.all([
      new Response(p.stdout).text(),
      new Response(p.stderr).text(),
      p.exited,
    ]);

    clearTimeout(timeout);
    await writeFile(path.join(out, `${mode}.log`), stdout + stderr, {
      mode: 0o600,
    });
    results.push({ mode, exit, expected, stdout, stderr });
    console.log(JSON.stringify({ mode, exit, expected }));

    if (exit !== expected) {
      throw new Error(`Unexpected fixture result: ${mode}`);
    }

    if (
      (mode === "fatal" || mode === "wrong-serial") &&
      !stderr.includes("X_GetDeviceButtonMapping")
    ) {
      throw new Error("Expected mapping failure missing");
    }

    if (mode === "unrelated" && !stderr.includes("X_DestroyWindow")) {
      throw new Error("Unrelated error was suppressed");
    }

    if (mode === "bad-match" && !stderr.includes("BadMatch")) {
      throw new Error("Wrong mapping error was suppressed");
    }

    if (mode === "guarded" && !stdout.includes("caught=1")) {
      throw new Error("Expected mapping error was not caught");
    }

    if (mode === "valid" && !stdout.includes("caught=0")) {
      throw new Error("Valid device was rejected");
    }
  }

  // oxlint-enable no-await-in-loop
  await writeFile(
    path.join(out, "result.json"),
    JSON.stringify({ display, isolation, results }, null, 2),
    { mode: 0o600 }
  );
} finally {
  clearTimeout(startup);
  server.kill("SIGTERM");
  const exit = await server.exited;
  const stderr = await new Response(server.stderr).text();
  await writeFile(path.join(out, "xvfb.log"), stderr, { mode: 0o600 });

  if (exit !== 0 && stderr) {
    console.error(stderr);
  }
}
