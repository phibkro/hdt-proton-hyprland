# External managed-thread capture

Status: the debug-event capture passed the isolated replay checks and recovered
live HDT's UI stack. Some live worker stacks remain unresolved. Full live dumps
are disabled because the provider did not honor cancellation.

## Goal

Capture HDT's managed stacks during a freeze from another process. Keep the
inspector independent of HDT's dispatcher and preserve the running Hearthstone
session. Identify the waiting method and any available monitor-owner evidence
without treating ordinary CLR spinning as the failure signature.

## Contract

- Inspect only an explicitly identified tracker or isolated replay process.
  Verify its executable hash and process identity before capture. Reject the
  game, the inspector itself, missing targets, and ambiguous selection.
- Use the actual installed Framework CLR's matching DAC. Record its hash and
  ClrMD version. Disable implicit downloads and credential-backed symbol lookup.
- Prefer inspection of a process snapshot: no target injection, execution,
  writes, or termination. The capture mechanism may briefly pause HDT; record
  that effect. Do not analyze a running target using unsupported passive mode.
  Surface per-thread read failures and unsupported lock data.
- Keep output private under the project. Use exclusive creation and bounded
  stack/lock enumeration. A failure cannot produce a success-shaped artifact.
- Validate the entire Windows/Proton/DAC boundary inside the existing account-free
  replay namespace before attempting the live tracker. No live application or
  prefix changes during development.
- Prepare the published flight-recorder package from clean source. Activate it
  only at a suitable tracker restart, preserving the current plugin for rollback.

## Acceptance

1. External inspection sees a known managed waiting method in the isolated WPF
   target while its dispatcher is deliberately blocked.
2. The target remains alive and resumes normal UI acknowledgement afterward.
3. Wrong executable identity and an unavailable DAC fail explicitly. Partial
   stacks cannot pass as a complete capture.
4. The native input journal continues through the scenario.
5. Installation of the newer recorder verifies the installed DLL identity and
   new recorder output. This requires a suitable live tracker restart.

## Run the isolated capture checks

Build the inspector from this checkout with the project development environment:

```sh
	devenv shell -- dotnet build src/diagnostics/managed/ManagedInspector.csproj \
		--configuration Release \
		--output "$PWD/local/managed-inspector/build" \
		--property:BaseIntermediateOutputPath="$PWD/local/managed-inspector/obj/" \
		--property:RestoreLockedMode=true \
		--configfile src/diagnostics/managed/NuGet.Config
```

With the [isolated replay environment](../UI-REPLAY.md) prepared for the selected
Proton version, run its managed capture scenario:

```sh
	devenv shell -- hdt-replay run --managed-debug-lease --diagnostics --with-plugin
```

The scenario requires the actual blocked method, the owner's method, and the
monitor's owner thread ID to agree. It rejects a wrong executable hash and a
missing DAC before attachment. The timeout and controller-death probes must
first reach a held debug event, then show target progress after the inspector
exits. Settings must accept native mouse input after the test releases the lock.
The independent journal must continue and stop cleanly.

Each run preserves its source manifest and result under `local/replay/runs`.
These checks establish the capture mechanism on the replay target. They do not
establish the cause of an HDT freeze or authorize unattended live capture.

## Approach and alternatives

Microsoft ClrMD provides the runtime/DAC protocol; reuse it rather than implement
managed stack walking. Current ClrMD has a .NET Standard build compatible with
the Framework runtime. Test that compatibility under Proton rather than assume
Windows support establishes Wine support. Pin dependencies with NuGet's lockfile
and keep restore caches in the project.

Native perf sampling already worked but does not recover Windows CLR managed
stacks. Full process dumps remain a fallback; they cost more storage and need a
validated reader. Do not change GC, synchronization, or rendering configuration
without a discriminating failure observation.

## Revisions

- 2026-11-15: Initial contract after the captured main-window freeze. Both frozen
  and responsive profiles hit CLR monitor-spin loops; managed callers and lock
  owners remain unknown.
- 2026-11-15: Installed ClrMD XML explicitly states that inspection of a running,
  unsuspended process is unsupported and can produce undefined behavior. Removed
  that proposed mechanism before implementation. Test process snapshots first;
  use a validated dump or bounded suspension if snapshot support is unavailable.
- 2026-11-15: Isolated Proton test cannot provide PSS APIs. Full dump capture
  succeeded; ClrMD rejected the DAC through Wine's signature-verification path.
  The reader now grants only an explicit DAC path plus matching caller-supplied
  SHA-256, retains CLR/DAC version matching, and disables alternate file lookup.
  This is byte-identity trust in the selected local DAC, not a claim of verified
  Authenticode provenance. Added PSS preflight because ClrMD's partially-created
  reader finalizer also calls the absent PssFreeSnapshot API.
- 2026-11-15: The isolated dump reader recovered the blocked dispatcher method,
  the monitor owner's method, and the owner's Windows thread ID. Wrong executable
  identity failed before dump creation. The target recovered and accepted Settings
  input after lock release. The independent journal continued during the test.
- 2026-11-15: The cancellation probe failed. A one-millisecond budget did not stop
  capture before the isolated supervisor's ten-second timeout. The result file
  remained empty and the incomplete dump remained private. Full live HDT dumps
  are now rejected before capture. Do not publish the inspector as ready or claim
  that its budget bounds Wine's dump operation. A missing-DAC test is written but
  has not run because the earlier cancellation assertion stops the scenario.
- 2026-11-16: Test a native debug-event lease instead of an uncancellable dump.
  Create the debug object with kill-on-close disabled before attaching. Attach
  through `NtDebugActiveProcess`, avoiding the Win32 wrapper's remote break-in
  thread. A separate reader may use ClrMD only while the controller holds a
  debug event. Detach in cleanup and bound reader lifetime. Keep this path
  restricted to ReplayHost until known stacks, timeout recovery, and controller
  death recovery pass in isolation. Verify the shipped Proton exports and test
  actual behavior rather than assuming Windows or upstream Wine semantics.
- 2026-11-16: The Stable Proton replay passed known-stack and monitor-owner
  assertions, timeout recovery, controller-death recovery, native Settings input,
  and independent journal continuity. A second run also passed wrong-identity
  and missing-DAC rejection. Nested cleanup releases the target even if reader
  cleanup throws. Capture budgets exclude reader startup and may include cleanup
  overhead. Live HDT capture remains untested and blocked.
- 2026-11-16: Added the explicit `hdt-debug-snapshot` operation after the isolated
  safety checks passed. Replay captures and fault probes reject HDT. The live
  operation rejects ReplayHost. Live captures recovered HDT's UI stack during a
  persistent input wait and during a later recovering rendering stall. Threads
  with only unnamed native frames now make the result partial. An independent
  `mouse-state-probe` succeeded while HDT's input call was stuck. This does not
  establish that every worker stack is recoverable under Wine.
