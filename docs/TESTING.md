# Test HDT with Hearthstone

Use these procedures to verify one path at a time. Build results and user observations have different coverage. Record the evidence with each result.

Arena entry, picks, and gameplay remain your actions. The test observer reads status and logs without changing Hearthstone.

## Test the running tracker

1. Start Battle.net through the existing Steam shortcut.
2. Start Hearthstone in windowed mode on one monitor.
3. Start HDT with `devenv shell -- hdt-overlay-launch` unless HDT is already open.
4. Run the status command:

   ```sh
   devenv shell -- hdt-status
   ```

5. Confirm that `trackerRuntime` reports `Microsoft CLR loaded`.
6. Confirm that the window list contains Hearthstone, HDT, and `HearthstoneOverlay`.
7. Confirm that `hdtLog.selectedCorrectLogDirectory` is `true`.
8. After a game action that writes a log entry, rerun the command.
9. Confirm that the current `Power.log` timestamp or size advances.

A fresh log file establishes log output. A saved match result requires the separate match test below.

## Test a saved Arena match

1. Play a match in an existing Arena run.
2. Finish the match yourself.
3. Wait for HDT to process the result.
4. Run `devenv shell -- hdt-status`.
5. Confirm that `savedMatchSummary` includes the new Arena result.
6. If the summary is ambiguous, compare the private `DeckStats.xml` start and end times with the match you finished.

Do not count a `Power.log` game-over entry as a saved HDT result. Repeat this test during a longer session to check for the historical UI stall.

## Test live offers and picks

This test requires an active draft or redraft. Enter Arena only if you want to spend the entry cost.

1. Open an offer screen.
2. Pause before making a pick.
3. Compare the displayed options with HDT's detected options.
4. Record whether the options match.
5. Make the pick yourself.
6. Confirm that HDT records the pick and updates to the next offer.
7. Repeat the comparison on the next offer.

If offers are missing, inspect the private HDT logs for HearthMirror IPC or state-read errors. Keep those logs out of commits.

## Test ArenaSmith ratings

This test requires an account with the applicable HSReplay entitlement.

1. Complete the offer-detection check above.
2. Confirm that the ArenaSmith ratings correspond to the current offer.
3. Make a pick yourself.
4. Confirm that ratings update with the next offer.

If offers appear without ratings, inspect ArenaSmith availability and HSReplay errors separately from HearthMirror detection. A detected offer alone does not establish access to ratings.

## Test a fresh draft and a longer session

The completed draft verified the particle workaround after an HDT restart. A fresh draft from initial hero selection and a longer session remain separate checks.

1. Start a fresh draft only when you choose to spend the entry cost.
2. Compare hero choices with HDT before you pick.
3. Enable both **Enable Arenasmith** and **Show Arenasmith Scores**.
4. Repeat the offer, pick, and rating checks through the draft.
5. Open and close HDT Settings after several offers, including an offer with a level-five badge when available.
6. Leave drafting and confirm that draft analysis disappears in the main menu.
7. Continue normal play and repeat the saved-match and overlay checks later in the session.
8. Record elapsed session time, offer count, screen transitions, and the first failing action if one occurs.

Use private `evidence/input-region.json` to compare `arenaParticleEmittersSuppressed` before and after level-five badges appear. The counter establishes that the workaround ran. Changed offers, cleared panels, responsive controls, and saved results establish the corresponding behavior.

## Test the overlay

Begin with windowed Hearthstone on one monitor at scale 1. Leave the overlay itself in place.

1. Click empty space in Hearthstone.
2. Confirm that Hearthstone receives the input and the overlay stays visible.
3. Use an HDT overlay control, such as the close button on an open notice.
4. Confirm that the control responds.
5. Hover an overlay card.
6. Move the pointer to empty game space.
7. Confirm that the preview disappears.
8. Repeat the hover check in HDT's main window.
9. Move only the Hearthstone window.
10. Confirm that the overlay follows Hearthstone.
11. Resize Hearthstone.
12. Confirm that the overlay follows the new bounds without a duplicate image.
13. Alt-Tab away from Hearthstone and back.
14. Repeat the empty-space and control clicks.
15. Confirm that the overlay stays aligned and does not flicker during those interactions.

If a check fails, record the first failing step. Use [Check and diagnose HDT](DEVELOPMENT.md) before changing another option.

## Test automatic startup

Finish play before you close the current game session.

1. Add the generated Steam option with [Start HDT from the Steam shortcut](INSTALL.md#start-hdt-from-the-steam-shortcut).
2. Close HDT, Hearthstone, and Battle.net normally.
3. Start the same Battle.net shortcut through Steam.
4. Start Hearthstone from Battle.net.
5. Confirm that one HDT instance opens after the game window appears.
6. Run `devenv shell -- hdt-status`.
7. Repeat the tracker and overlay checks above.

To test the manual-close behavior, close HDT normally while the game remains open. Confirm that the watcher does not repeatedly reopen HDT in that game session.

Run the isolated recovery checks without opening Hearthstone:

```sh
devenv shell -- bun test ./tests/autostart.test.ts
```

These checks run the watcher and launcher stand-ins as separate processes.
Compositor snapshots and time are simulated. The checks cover failed launches,
retry delays, failure limits, normal exits, process identity, and lock ownership.
They do not establish recovery of the real Wine process or overlay after a crash.

## Reproduce the XInput device race without Hearthstone

Run the isolated native check from the checkout:

```sh
devenv shell -- bash scripts/check-xinput.sh
```

Expect one result line per case and a final `Private XInput reproduction passed`
message. The printed evidence directory contains each case's logs and
`result.json`. The script retains the evidence and stops its private X server.

Use the script rather than running the native fixture against your desktop.
See [fatal XInput mapping errors](specs/xinput-device-race.md) for the demonstrated
mechanism and the remaining gap to a tested Wine fix.

## Test stable Proton

Stable 11.0-2 passed baseline desktop checks but showed submenu slowdowns and temporary freezes. Experimental remains the current selection. Read the [observed comparison](COMPATIBILITY.md#proton-version-comparison) before repeating the test. Valve describes [stable, Next, and Experimental](https://github.com/ValveSoftware/Proton/wiki/Proton-Versions) separately.

1. Finish play.
2. Close HDT, Hearthstone, and Battle.net normally.
3. Stop the watcher service.
4. Confirm that no wineserver remains for the prefix.
5. Copy the shortcut's entire `steamapps/compatdata/<steamAppId>/` directory, including metadata outside `pfx`.
6. Save the shortcut's current compatibility selection and HDT data.

The pre-.NET recovery copy predates later settings. Use a new stopped checkpoint for this test.

Choose one test arrangement:

- For an isolated test, create a separate Steam shortcut and copy the prefix into its compatdata directory. Configure HDT for the new app ID. Allow for extra disk use and possible Battle.net authentication.
- For a test in the original prefix, keep the stopped checkpoint available for full-directory recovery. Do not switch versions without that copy.

Continue with the selected arrangement:

1. Install the desired stable Proton through Steam.
2. Select that version for the test shortcut.
3. Start Battle.net through Steam.
4. Start Hearthstone yourself.
5. Run every tracker, Arena, rating, overlay, and startup check that applies.
6. Record checks that you could not run as untested.

If the test regresses, stop all test processes before restoring the checkpoint and Experimental selection. Never copy registry files or change wineservers underneath a running game.
