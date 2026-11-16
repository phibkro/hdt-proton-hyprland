import { openSync, closeSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

export const root = resolve(import.meta.dir, "..");

export class CommandFailure extends Error {
  constructor(
    public readonly status: number,
    command: string
  ) {
    super(`Command failed (${status}): ${command}`);
  }
}

export function run(
  argv: string[],
  options: { capture?: boolean; env?: NodeJS.ProcessEnv; cwd?: string } = {}
): string {
  const child = Bun.spawnSync(argv, {
    cwd: options.cwd ?? root,
    env: options.env ?? process.env,
    stdin: "inherit",
    stdout: options.capture ? "pipe" : "inherit",
    stderr: "inherit",
  });

  if (child.exitCode !== 0) {
    throw new CommandFailure(child.exitCode || 1, argv[0]);
  }
  return child.stdout?.toString().trimEnd() ?? "";
}

export function exec(argv: string[]): never {
  const executable = Bun.which(argv[0]);

  if (!executable) {
    throw new CommandFailure(127, argv[0]);
  }
  const replace = process.execve;

  if (!replace) {
    throw Error(
      "This launcher requires Bun with process.execve support on Linux."
    );
  }
  replace(executable, argv, process.env);
  throw new Error("execve returned unexpectedly");
}

export function execScript(name: string, args: string[] = []): never {
  return exec([
    process.execPath,
    resolve(root, "scripts", `${name}.ts`),
    ...args,
  ]);
}

export function main(action: () => unknown | Promise<unknown>) {
  Promise.resolve()
    .then(action)
    .catch((error) => {
      console.error(error instanceof Error ? error.message : String(error));
      process.exit(error instanceof CommandFailure ? error.status : 1);
    });
}

export function read(path: string) {
  return readFileSync(path, "utf-8").trimEnd();
}

export function pid(value: unknown): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value <= 0) {
    throw Error("Invalid process PID");
  }
  return value;
}

export function lock(path: string): { acquired: boolean; close: () => void } {
  // The open file description stays owned here. Only the flock subprocess gets
  // fd 3 explicitly; Bun opens the parent descriptor CLOEXEC, so launchers cannot
  // retain the watcher lock after this owner exits.
  const fd = openSync(path, "w", 0o600);

  const child = Bun.spawnSync(["flock", "-n", "3"], {
    stdio: ["ignore", "pipe", "pipe", fd],
  });

  if (child.exitCode !== 0 && child.exitCode !== 1) {
    closeSync(fd);
    throw new CommandFailure(
      child.exitCode || 1,
      "Could not acquire the HDT monitor lock"
    );
  }

  return {
    acquired: child.exitCode === 0,
    close: () => {
      closeSync(fd);
    },
  };
}
