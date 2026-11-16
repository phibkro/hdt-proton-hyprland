# Capture evidence independently of HDT

The native collector records selected log bytes even while HDT stalls or exits;
the exporter turns validated local recorder events into a timeline.

## Sub-features

- `journal`: exact bytes, generations, rotation and producer independence.
- `trace`: validated duration/session events, private output and overwrite refusal.
- `recorder`: dispatcher stall/recovery and rendering observations through the isolated UI host.
- `canonical`: installed HDT event-publisher continuity across collector generations.

## How to get to it (user POV)

Use `hdt-proton observe` beside play, `hdt-proton trace-export` afterward, or
`hdt-replay run --canonical-events --with-plugin` without a live account.

## Driving it with the native CLI

Preconditions: parent doctor; new owned paths; no live account data needed for
synthetic input. Run commands inside the initialized devenv shell.

- **Journal.** Create `$HDT_VERIFY_ROOT/Power.log` containing `first\n`. Start `"$HDT_HOST_BINARY" observe --output "$HDT_VERIFY_ROOT/journal" --from start --max-bytes 1048576 --source "power=$HDT_VERIFY_ROOT/Power.log"` in the background and retain its PID. Wait for a source.bytes record, append `second\n`, wait for those bytes, send SIGTERM to that PID and wait for exit zero. Reconstruct base64 source.bytes records and compare with the file. Require final journal.stop and modes 0700/0600. Retain command/output and reconstructed bytes. Stop the owned collector on every failure too.
- **Trace.** Write one JSONL event with `schema:1`, `session:"verify"`, `seq:1`, `tsUS:1000`, `thread:1`, `name:"ui.dispatch"`, `data:{durationMS:1}`. Run `"$HDT_HOST_BINARY" trace-export "$HDT_VERIFY_ROOT/trace.json" "$HDT_VERIFY_ROOT/events.jsonl"`. Independently inspect one duration event and private permissions. Re-run to the same output, require nonzero, and compare its hash to confirm preservation.
- **Boundary regression cases.** Run `cargo nextest run --locked --lib --test rust-diagnostics --test-threads 2`; inspect the independent producer stop/death/restart, rotation, capacity, duplicate and validation cases. They supplement the CLI drive, not replace it.
- **Canonical publisher pipeline.** With the isolated seed, run `hdt-replay run --canonical-events --with-plugin`. Require the canonical session files and byte-identical reconstruction from journal generations. This path uses HDT's event pipeline; it does not prove all possible live watcher subscriptions.

- **Flight recorder.** In a separate run, use `hdt-replay run --with-plugin --diagnostics`. Inspect recorder events for a dispatcher stall and recovery, a dispatch duration of at least 2000 ms, rendering callbacks, build identity, first-stall capture, and exported `trace.json`. The UI feature's diagnostics replay may supply this same evidence; record both feature IDs.

## Gotchas

The output directory/file must be new. Never remove a failed capture to reuse its
name. `--from end` omits existing bytes by policy. The size limit includes JSON
and base64 expansion. Host observation time and HDT event time are distinct.
Collector liveness proves collection, not HDT consumption. Real log bytes can
contain personal data; only synthetic fixtures are safe to commit unchanged.

The harness rejects `--canonical-events --diagnostics`. Run the canonical and
recorder journeys separately; neither result substitutes for the other.
