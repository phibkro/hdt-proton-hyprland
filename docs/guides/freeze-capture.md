# Capture evidence when HDT freezes

Run the native journal alongside HDT. The journal keeps collecting selected log
files when HDT stops responding or exits. It does not restart HDT or control
Hearthstone. See [diagnostic boundaries](diagnostics.md#independent-input-journal)
for the distinction between collected bytes and processed game events.

## Start a capture

1. Open a terminal in the checkout.
2. Set these variables to the current files. Use HDT's own diagnostic log and the
   active Hearthstone session's log directory:

   ```sh
   HDT_LOG='/absolute/path/to/HearthstoneDeckTracker/Logs/hdt_log.txt'
   GAME_LOGS='/absolute/path/to/Hearthstone/Logs/Hearthstone_CURRENT_SESSION'
   RECORDER_ROOT="$PWD/evidence"
   ```

   Set `RECORDER_ROOT` to the directory that contains `flight-recorder-*`
   directories. For an installed package, use your data directory's `evidence`
   directory. See [recorder locations](diagnostics.md#read-a-recording).

3. Start the journal:

   ```sh
   devenv shell -- hdt-proton observe \
     --output "$PWD/evidence/freeze-$(date -u +%Y%m%dT%H%M%S)" \
     --from end \
     --max-bytes 536870912 \
     --recorder-root "$RECORDER_ROOT" \
     --source "hdt=$HDT_LOG" \
     --source "power=$GAME_LOGS/Power.log" \
     --source "loading=$GAME_LOGS/LoadingScreen.log" \
     --source "arena=$GAME_LOGS/Arena.log"
   ```

4. Keep the terminal open while you play. Capture from before the next match
   through the return to the Arena lobby.

`--from end` skips bytes already present at the first poll. Use `--from start`
to capture existing file contents too. The output directory must be new. The
512 MiB limit includes journal metadata and base64 expansion.

## Preserve a failure

1. If HDT freezes, note the visible Hearthstone screen and the approximate time.
2. Leave HDT open if you want to inspect its blocked threads. The journal keeps
   collecting while you continue playing.
3. After the transition, press **Ctrl+C** in the journal terminal.
4. Keep `journal.jsonl` in the printed output directory.

The journal stores original bytes and may contain account or player data. Keep
the capture private. Review and sanitize evidence before sharing it.

## Start another session

If Hearthstone restarts, select its new session directory and start another
capture. If only HDT restarts, the existing capture follows replacement of the
selected `hdt_log.txt` path and records a new source generation.
With `--recorder-root`, the capture also discovers each restarted HDT recorder
and collects that session from its first byte. Leave the collector running
across HDT restarts.

If the journal reports capacity exhaustion, retain the completed directory and
start a capture in a new directory. The command exits nonzero rather than
overwriting existing evidence.

## Native XInput exits

A fatal `XI_BadDevice` / `X_GetDeviceButtonMapping` message comes from the
native Wine/X11 path. The managed recorder may stop without an exception.
Keep the independent journal running across HDT restarts. Recover only HDT with
`hdt-proton restart`; the existing command checks that Hearthstone is outside
its service before restarting it. The watcher already retries nonzero exits
with a bounded backoff and pauses after repeated failures.

For device-lifecycle evidence, build the development host and run a separate
read-only observer from this checkout:

```sh
devenv shell -- cargo build --locked --bin hdt-host
mkdir -p evidence
devenv shell -- hdt-proton xinput-watch \
  --output "$PWD/evidence/xinput-$(date -u +%Y%m%dT%H%M%S).jsonl" \
  --seconds 3600
```

This development command is not available in older installed packages. It
subscribes only to XInput hierarchy and device-change notifications, records
numeric device identities and UTC/monotonic timestamps, and exits after the
explicit duration or at 32 MiB. It does not record keys, button presses,
coordinates or device names, change devices, or depend on HDT's process. A new
output file is required; an existing file is never overwritten. An abrupt
termination can leave the capture without its final stop record.

After a failure, preserve the corresponding native messages using a narrow
local time interval around the incident:

```sh
umask 077
journalctl _COMM=HearthstoneDeck --since '5 minutes ago' \
  --no-pager -o short-iso > evidence/hdt-native-exit.txt
```

Keep raw evidence private. Correlate the device events with the native exit;
a nearby device change alone does not prove causation. This observer does not
capture Wine's native caller stack, button-mapping contents or every possible
mapping change. Do not disable all X11 errors or modify the running game's
Proton driver. The existing scoped-error fixture and the independent observer
can be checked together with `devenv shell -- bash scripts/check-xinput.sh`.

If the observer reports `Connection refused`, check its display namespace.
On the observed desktop, `DISPLAY=:0` had only `/tmp/.X11-unix/X0_` exposed to
this shell. A private mount alias allowed a read-only sample to connect:

```sh
devenv shell -- bwrap --bind / / \
  --tmpfs /tmp/.X11-unix \
  --ro-bind /tmp/.X11-unix/X0_ /tmp/.X11-unix/X0 \
  -- ./local/rust-target/debug/hdt-host xinput-watch \
  --output "$PWD/evidence/xinput-mapped-$(date -u +%Y%m%dT%H%M%S).jsonl" \
  --seconds 3600
```

Use this only for that verified socket layout. The alias exists solely inside
the observer's mount namespace; do not replace the desktop's shared socket or
change Steam's display settings. This command inherits the current shell's
permissions and is not an isolation sandbox. The separate acceptance script
uses a private server with the operator's desktop sockets absent.

## Capture the native Xlib error stack

The device observer provides timing, not a caller stack. For the native
`XI_BadDevice` exit, the development checkout also supplies a bounded GDB
capture. Choose the current HDT Linux PID explicitly, then run:

```sh
HDT_PID=12345 # Replace with the current HDT PID, not Hearthstone's PID.
devenv shell -- bash scripts/capture-native-error.sh "$HDT_PID" \
  "$PWD/evidence/native-xlib-$(date -u +%Y%m%dT%H%M%S)" 1800
```

The script rejects a non-HDT process, an existing debugger and an existing
output directory. It records the Wine executable and process start metadata,
disables debugger startup files and automatic script loading, and attaches to
that PID only. Attachment briefly pauses HDT. It neither follows new tracker
processes nor attaches to Hearthstone or the Wine server. A refused attachment
is a failure; do not lower the machine's ptrace restrictions to bypass it.

After attaching, require `HDT_NATIVE_CAPTURE_ARMED` in the private `gdb.log`,
and check that the hardware breakpoint address matches the displayed
`_XDefaultError` symbol address. The capture explicitly selects the Linux
x86-64 ABI after attachment: Wine's mixed architecture reporting caused a
symbolic prologue breakpoint to truncate its address in the observed session.
The exact-entry hardware breakpoint avoids that prologue selection.

Routine Wine/CLR signals pass through to their existing handlers. At Xlib's
default fatal-error handler, the capture saves three numeric error fields,
register addresses, up to 24 current-thread stack frames without arguments,
and preserves a pre-attachment `/proc` mapping snapshot. It detaches without suppressing the error. On
the explicit deadline it terminates the debugger; the private lifetime test
verified that an attached target remains running and untraced after this path.
The exit status and incomplete-capture message distinguish a deadline or
unexpected stop from a recorded fatal-error stack.

This is a one-shot Linux x86-64 Xlib capture, not general managed exception,
segmentation-fault or freeze diagnosis. Some Wine/managed frames may lack
symbols or unwind information. It records no locals or complete memory dump,
but paths and stack information remain private. After HDT restarts, select
its new PID and use a new output directory.

A debugger can change scheduling and make a race harder to reproduce. Keep the
independent device capture alongside it; lack of a failure while debugging is
not evidence that the bug is fixed.

The hosted Ubuntu acceptance job grants `userns` to its pinned Bubblewrap
executable with a runner-only AppArmor profile, following Ubuntu's
[documented application profile mechanism](https://documentation.ubuntu.com/release-notes/24.04/).
The fixture still runs as the normal runner user with a private network,
process tree and display. This setup changes no operator-machine policy and
does not disable the runner's global namespace restriction.

On 16 November, the live capture reached the fatal handler and saved the Wine
`update_device_mapping` caller stack, then GDB 17.2 aborted in its optional
`info sharedlibrary` command (`find_debug_base_for_solib` assertion). The capture
now omits that command and retains `/proc` mappings outside GDB instead. A
partial stack remains useful evidence, but the command reports it as incomplete.

The capture also traces the numeric device ID passed to
`XGetDeviceButtonMapping`, with the debugger thread identity. After 1,000
queries it disables that query breakpoint; the fatal-handler breakpoint stays
armed. No button values are recorded. This is more intrusive than the independent
observer, so it is bounded diagnostic instrumentation rather than a default
launcher setting.

The bounded native trace also records requested IDs at `XOpenDevice` entry.
Compare these with the subsequent mapping-query IDs on the same thread/display
to detect a changed libXi return value. Each trace point disables after 1,000
entries; the fatal-error breakpoint stays armed. No button values are recorded.
