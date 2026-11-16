import { defineConfig } from "oxfmt";
import ultracite from "ultracite/oxfmt";

export default defineConfig({
  ...ultracite,
  ignorePatterns: [
    ...(ultracite.ignorePatterns ?? []),
    "local/**",
    "reference/**",
    "evidence/**",
    "backups/**",
    "app/**",
    "cache/**",
  ],
});
