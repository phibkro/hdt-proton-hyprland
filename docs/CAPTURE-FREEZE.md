# Capture evidence when HDT freezes

Run the native journal alongside HDT. The journal keeps collecting selected log
files when HDT stops responding or exits. It does not restart HDT or control
Hearthstone. See [diagnostic boundaries](DIAGNOSTICS.md#independent-input-journal)
for the distinction between collected bytes and processed game events.

## Start a capture

1. Open a terminal in the checkout.
2. Set these variables to the current files. Use HDT's own diagnostic log and the
   active Hearthstone session's log directory:

   ```sh
   HDT_LOG='/absolute/path/to/HearthstoneDeckTracker/Logs/hdt_log.txt'
   GAME_LOGS='/absolute/path/to/Hearthstone/Logs/Hearthstone_CURRENT_SESSION'
   ```

3. Start the journal:

   ```sh
   devenv shell -- hdt-proton observe \
     --output "$PWD/evidence/freeze-$(date -u +%Y%m%dT%H%M%S)" \
     --from end \
     --max-bytes 536870912 \
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

If the journal reports capacity exhaustion, retain the completed directory and
start a capture in a new directory. The command exits nonzero rather than
overwriting existing evidence.
