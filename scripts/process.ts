import path from "node:path";

export const root = path.resolve(import.meta.dir, "..");

class CommandFailureError extends Error {
  readonly status: number;

  constructor(status: number, command: string) {
    super(`Command failed (${status}): ${command}`);
    this.name = "CommandFailureError";
    this.status = status;
  }
}

export const run = (
  argv: string[],
  options: { capture?: boolean; env?: NodeJS.ProcessEnv; cwd?: string } = {}
): string => {
  const child = Bun.spawnSync(argv, {
    cwd: options.cwd ?? root,
    env: options.env ?? process.env,
    stdin: "inherit",
    stdout: options.capture === true ? "pipe" : "inherit",
    stderr: "inherit",
  });

  if (child.exitCode !== 0) {
    throw new CommandFailureError(child.exitCode || 1, argv[0]);
  }

  return child.stdout?.toString().trimEnd() ?? "";
};

export const main = async (
  action: () => void | Promise<void>
): Promise<void> => {
  try {
    await action();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(error instanceof CommandFailureError ? error.status : 1);
  }
};
