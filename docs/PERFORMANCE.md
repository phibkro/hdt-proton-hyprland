# Measure HDT performance

Use these checks to distinguish tracker CPU use from game, graphics, and system
delays. Keep the game and tracker running during the first capture. Do not change
their configuration during a match.

## Capture process activity

1. Identify the current Linux process IDs for HDT and Hearthstone.
2. Export those IDs as `HDT_PID` and `HEARTHSTONE_PID`. Do not reuse IDs from an
   earlier capture.
3. Run the bounded capture from the checkout:

   ```sh
	devenv shell -- bash -c '
	set -euo pipefail
	umask 077
	: "${HDT_PID:?Export the current HDT Linux PID}"
	: "${HEARTHSTONE_PID:?Export the current Hearthstone Linux PID}"
	[[ "$HDT_PID" =~ ^[1-9][0-9]*$ && "$HEARTHSTONE_PID" =~ ^[1-9][0-9]*$ ]]
	[[ $(cat "/proc/$HDT_PID/comm") == HearthstoneDeck ]]
	[[ $(cat "/proc/$HEARTHSTONE_PID/comm") == Hearthstone.exe ]]
	capture=$(mktemp -d "$PWD/evidence/performance-XXXXXXXX")
	printf "%s\n" "$capture"
	pidstat -H -u -r -d -w -o JSON -p "$HDT_PID,$HEARTHSTONE_PID" 1 15 > "$capture/processes.json"
	'
   ```

4. Record the visible game mode, approximate time of the slowdown, and whether
   the game, tracker, or both felt slow. Keep account and card names out of notes.
5. Compare the capture timestamps with the [flight recorder](DIAGNOSTICS.md).

`pidstat` is supplied by `sysstat` in the development environment. The capture
contains fifteen one-second intervals. It measures process activity, not game
frame times. A process restart requires new IDs and a new capture. The initial
name check does not prevent PID reuse during capture, so reject intervals that
span a restart.

## Capture HDT thread activity

With the same validated tracker ID, run this command inside `devenv shell`:

```sh
	umask 077
	capture=$(mktemp -d "$PWD/evidence/performance-threads-XXXXXXXX")
	pidstat -H -u -w -t -o JSON -p "$HDT_PID" 1 10 > "$capture/threads.json"
```

Compare busy thread IDs across the ten samples. Linux thread IDs do not identify
managed methods. Use a matching runtime profile before naming a caller.

## Interpret a capture

Use [pidstat's field definitions](https://man7.org/linux/man-pages/man1/pidstat.1.html)
when reading the output. CPU percentages use one logical CPU as 100 percent.
Do not divide by the machine's CPU count when comparing process costs.

Check process CPU, scheduler wait, context switches, major faults, and physical
IO together. Check system memory pressure, swap-in deltas, and device utilization
over the same interval before attributing a delay to storage or memory.

The read and write counters in `/proc/<pid>/io` include operations that do not
reach disk. A high call count with low physical IO can indicate IPC or cached
reads. It does not identify the callers or prove that the calls are unnecessary.

An HDT dispatcher callback measures UI scheduling delay. It does not measure
Hearthstone frame presentation. GPU utilization snapshots also cannot establish
frame-time consistency. Capture game frame times separately at a later launch
if visible game stutters remain unexplained.

## Compare a change

Finish the match before changing HDT configuration or restarting the tracker.

1. Select a reproducible scene or an isolated replay with fixed input.
2. Record the game version, Proton version, installed plugin hash, and options.
3. Capture a baseline with the commands above.
4. Change one tracker variable.
5. Repeat the same workload and measurement window.
6. Recheck tracking, overlay geometry, click-through, and control behavior.

Keep HDT's data processing, overlay rendering, and diagnostic overhead as separate
comparisons. A shared wineserver serves other prefix processes too. Its entire
CPU use cannot be charged to HDT without a controlled comparison.

Use measured hot methods to choose a change. Polling and redundant serialization
may benefit from event subscriptions, coalescing, or caching. Lock contention
requires fewer competing owners or shorter critical sections. Data layout work
needs evidence of hot iteration or allocation costs. None of those techniques
substitutes for repeating the workload after the change.

HDT runs .NET Framework under Proton. Modern .NET diagnostic commands are not
assumed to support this runtime. The external managed inspector remains under
development. Do not use its live dump path until cancellation is verified.
