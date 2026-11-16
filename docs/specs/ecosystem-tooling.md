# Source tooling

## Goal

Contributors can format and analyze repository-owned TypeScript, C#, C, and Lua
without starting Hearthstone or changing its prefix. Local commands and CI use
the same configuration. Checks fail on actionable findings rather than silently
excluding a language or treating formatting as type or runtime verification.

## Constraints

- Preserve the .NET Framework/WPF plugin ABI and the existing native X11 ABI.
- Keep private logs, backups, downloaded HDT files, and third-party source outside
  formatting and lint discovery.
- Pin JavaScript dependencies with Bun and native tools with the Nix development
  environment. No dependency downloads in an offline Nix check.
- Use Ultracite's Oxlint and Oxfmt presets, including the requested anti-slop
  rules. Explain narrowly scoped exceptions instead of disabling a preset.
- Assess Zig's C frontend without changing the shipped compiler ABI by accident.
- Keep tier-five particles suppressed; this change cannot change runtime defaults.

## Acceptance

1. Check and fix commands identify their language coverage and propagate errors.
2. TypeScript is linted with Ultracite core and anti-slop, formatted with Oxfmt,
   and checked separately with a TypeScript compiler.
3. C uses Clang formatting and static analysis with actual X11 include flags.
4. Lua uses StyLua and Selene with an explicit compositor host API declaration.
5. C# formatting works without a game installation. Semantic analysis either
   resolves the real Framework/HDT references or fails with an explicit setup
   requirement. An unresolved build cannot be reported as a clean analysis.
6. Existing process tests still pass after any mechanical formatting changes.
7. Document tool choices, commands, pinned sources, and verification limits.

## Revisions

- 2026-11-16: Initial contract; user requested Ultracite anti-slop, Lua, Valibot
  for schemas, and consideration of Zig's C compiler.
- 2026-11-16: User requested fast staged pre-commit checks and full PR/main-merge
  checks. Use pre-commit's existing index protection rather than implementing a
  second stash mechanism. Keep full lint strict while existing debt is repaired.

## Verification recorded on 2026-11-16

- Formatter checks, strict TypeScript 7, C static analysis, and Lua checks passed.
- New quality/hook/input-test TypeScript passes Ultracite core, anti-slop, and
  type-aware rules without overrides.
- C# recommended Roslyn analysis passed with no warnings for both explicit core
  scope and full scope using the original local HDT assemblies. Framework 4.7.2
  references are locked; production remains on the prefix compiler.
- Native surface-probe tests reject 13 malformed/overflowing arguments and accept
  both signed-int coordinate boundaries before failing to connect to an absent
  display. Rectangle clipping uses wider arithmetic before narrowing to Xlib.
- Zig compiled all three C programs. Desktop behavior with Zig-built helpers is
  untested; the production compiler was not switched.
- The real Git hook accepted good staged Lua while preserving malformed unstaged
  Lua, then rejected that malformed version when staged. Failed commit left HEAD
  unchanged. A disposable repository was used, not the operator's dirty checkout.
- Source checks passed 47 process/file tests with 156 assertions plus bundles and
  native builds. Nix package, tests, and native-quality checks passed.
- Actual isolated Wine/WPF replay passed UI transitions, Settings, Battlegrounds
  tabs, popup cleanup, resize, diagnostics, and shutdown after the C# adjustments.
  Particle suppression remained active. No live game was started or changed.

## Open gate: existing TypeScript lint debt

The full strict lint command currently fails. It is not suppressed or replaced
with a passing formatter/compiler result. Remaining findings include unsafe JSON
and compositor dictionaries in `src/runtime/status.ts`, `src/hyprland/control-input.ts`
and replay drivers; assertions/unknown boundaries in `scripts/process.ts` and the
replay scripts; serial async loops, callback-owned signal state, and style rules
throughout existing source/tests. Each finding needs a semantic fix or a narrowly
justified exception. Valibot is the operator's accepted schema option for boundary
work; adding it requires an offline runtime-package dependency strategy first.

Do not merge this tooling branch until the full workflow is green. The remote
workflow has not run for this local branch. Full HDT-dependent C# and WPF replay
remain local checks because the public repository does not distribute HDT or
Microsoft runtime binaries.
