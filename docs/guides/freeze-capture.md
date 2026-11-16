# Capture evidence when HDT freezes

Run the native journal alongside HDT. The journal keeps collecting selected log
files when HDT stops responding or exits. It does not restart HDT or control
Hearthstone. See [diagnostic boundaries](diagnostics.md#independent-input-journal)
for the distinction between collected bytes and processed game events.

## Start a capture

1. Open a terminal in the checkout.
2. Set these variables to the current files. Use HDT's own diagnostic log and the
   Hearthstone log root containing session directories:

   ```sh
   HDT_LOG='/absolute/path/to/HearthstoneDeckTracker/Logs/hdt_log.txt'
   GAME_LOGS='/absolute/path/to/Hearthstone/Logs'
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
     --game-log-root "$GAME_LOGS" \
     --game-log Power.log \
     --game-log LoadingScreen.log \
     --game-log Arena.log
   ```

4. Keep the terminal open while you play. Capture from before the next match
   through the return to the Arena lobby.

For files discovered under the selected roots, `--from end` records each
existing file's end offset during startup; files appearing afterward start at
zero. Explicit `--source` files establish their initial offset at the first
poll. Use `--from start` to capture existing contents too. The output directory
must be new. The 512 MiB limit includes journal metadata and base64 expansion.

## Preserve a failure

1. If HDT freezes, note the visible Hearthstone screen and the approximate time.
2. Leave HDT open if you want to inspect its blocked threads. The journal keeps
   collecting while you continue playing.
3. After the transition, press **Ctrl+C** in the journal terminal.
4. Keep `journal.jsonl` in the printed output directory.

The journal stores original bytes and may contain account or player data. Keep
the capture private. Review and sanitize evidence before sharing it.

## Start another session

With `--game-log-root`, leave the collector running when Hearthstone restarts:
each new immediate session directory gets separate cursors, and old streams keep
draining. Newly discovered sessions/files start at byte zero even with `--from end`.
The root and selected log names are explicit; no active game or Steam prefix is
inferred. For a flat directory use the existing `--source name=/absolute/file`
form. Symlink path components fail; missing roots/files can recover. See the
[discovery contract](../specs/independent-log-session-discovery.md) for supported
names, limits and acquisition boundaries.

If only HDT restarts, the existing capture follows replacement of the
selected `hdt_log.txt` path and records a new source generation.
With `--recorder-root`, the capture also discovers each restarted HDT recorder
and collects that session from its first byte. Leave the collector running
across HDT restarts.

If the journal reports capacity exhaustion, retain the completed directory and
start a capture in a new directory. The command exits nonzero rather than
overwriting existing evidence.
