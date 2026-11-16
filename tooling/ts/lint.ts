import { defineConfig } from "oxlint";
import antiSlop from "ultracite/oxlint/anti-slop";
import core from "ultracite/oxlint/core";

export default defineConfig({
  env: { ...core.env, browser: false, node: true },
  extends: [core, antiSlop],
  ignorePatterns: [
    ...core.ignorePatterns,
    "local/**",
    "reference/**",
    "evidence/**",
    "backups/**",
    "app/**",
    "cache/**",
  ],
  options: { typeAware: true },
});
