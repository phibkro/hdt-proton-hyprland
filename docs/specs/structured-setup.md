# Structured source and Steam startup integration

2026-11-15. Status: frozen for review.

## Goal and journey

The operator can find runtime, plugin, compositor and diagnostic sources by responsibility, apply the existing compatibility plugin through one setup command, and start the existing desktop-owned watcher from the Battle.net Steam shortcut's Launch Options. Steam retains ownership of the game command and Proton choice.

## Constraints and values

Preserve the running game, tracker, installed layout, account data, backups and existing Home Manager service. No automatic .NET installation, prefix migration, Steam configuration rewrite, or Proton switch. Keep the root autostart entry point for the published machine service. Reuse devenv, the tested launcher, Bash/systemd service supervision and the existing installer/configuration commands; no new runtime dependency or alternative game launcher. Record missing service or install prerequisites explicitly. Source layout is project-specific rather than mandated by a universal standard.

## Acceptance / falsifiers

- Runtime TypeScript under src/runtime; C# plugin under src/plugin; compositor files under src/hyprland; native and managed probes under src/diagnostics; shell entry points under scripts; tests under tests. Config and private runtime data remain anchored to the checkout root regardless of caller cwd.
- Existing public devenv commands keep their names. Root autostart.sh remains a forwarding compatibility entry point. Existing evidence paths and running processes are preserved.
- hdt-setup builds the plugin, closes only an identified HDT normally, installs the plugin and configures overlay settings, then prints the next launch command. Failure stops subsequent steps. Already-stopped HDT is supported; ambiguous or unidentified tracker processes fail closed. Setup never force-terminates anything or launches the game.
- Steam wrapper requests the existing user service, then execs Steam's original argv without shell evaluation. A missing service yields an actionable warning but must not prevent the game from launching. Preserve argv and exit status in real subprocess tests. Actual Steam client launch remains an operator-controlled acceptance test.
- hdt-steam-options prints a correctly shell-quoted absolute Launch Options line. No local paths/account IDs are committed. Wrapper/service are one integration path; no second watcher implementation.
- Bounded tests, shellcheck, TypeScript bundling, native helper compilation and real-prefix plugin compilation pass. Verify runtime status without restarting the game. Check the committed clean tree and secret scan before publication.
- Record Experimental as the tested profile. Stable remains untested until a stopped-prefix backup and operator game launch permit a real comparison of startup, logs, draft/ratings and overlay input/geometry. No inference of compatibility from source/build tests.

## Revisions

- 2026-11-15: Initial contract. Portable full HDT/.NET bootstrap is deliberately outside this bounded setup: authentic installer, native CLR and first-run HDT settings remain explicit prerequisites. The existing service is supplied by desktop configuration; the Steam wrapper does not install a competing service.

## Verification

- Ten bounded tests / 33 assertions passed: config validation/root location, Steam argv and exit status through service success/failure, shell quoting and no-command rejection, and setup ordering/failure short-circuiting.
- Shellcheck, seven TypeScript entry bundles and both native helpers passed. The moved plugin compiled with the real native .NET compiler against installed HDT assemblies; compiler exit zero. Steam's inherited overlay preload emitted architecture warnings, which did not prevent compilation.
- The wrapper successfully requested the real already-active user service, then execed a harmless host command. A real Steam-client game launch through the generated option remains untested. No Steam configuration was edited.
- Read-only live status found native CLR, fresh control regions, matching game/overlay origins and fresh Power.log. DeckStats contained two saved Arena wins with distinct start/end times in addition to three Standard entries. No tracker/game restart or stable Proton switch occurred.
- Aggregate setup application remains untested against the live accepted tracker: its ordered boundary behavior and actual plugin compilation are verified separately. This is recorded in the compatibility matrix rather than inferred from those checks.
- Local ignored forwarding symlinks preserve older devenv command paths held by the already-running service. They are not release contents; the root autostart service entry is versioned. Remove local links only after confirming the old service/launcher processes have exited and the service is using the new devenv generation.
