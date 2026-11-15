# Debug HDT freezes

The local flight recorder records UI delays, view changes, rendering callbacks, and input observations. A background thread writes the files. The recorder sends no data over the network.

This is a development change. Plugin 0.5.1 contains the recorder. An existing 0.5.0 tracker must be upgraded separately. Building or testing this checkout does not install a plugin.

## Read a recording

After a plugin build that includes the recorder, launches with `HDT_INPUT_REGION_FILE` set create `flight-recorder-<Windows PID>-<session>` beside that file. Each plugin load owns a separate directory so that reloads cannot race an older writer. This is normally inside the private `evidence` directory.

The recorder keeps two rolling JSONL files, each at most 1 MiB. When a dispatcher ping waits at least two seconds, the writer copies the available files to `first-stall-*.jsonl`. The rolling files then continue to record recovery. The queue holds at most 512 events. Samples report the number of dropped events. Files remain after HDT exits. Retention across different sessions requires manual cleanup.

Export a recording:

```sh
devenv shell -- hdt-proton trace-export evidence/hdt-trace.json \
  evidence/flight-recorder-<Windows-PID>-<session>/events.jsonl
```

Replace the directory placeholder with the actual recorder directory. Add `previous.jsonl` as another input when that file exists. To inspect the first stall, export the `first-stall-*.jsonl` files to a different output instead. Do not combine overlapping rolling files and stall captures. The exporter rejects duplicate events and existing output files.

Open the export in [Perfetto](https://perfetto.dev/docs/getting-started/other-formats). Use a local viewer when required by your diagnostic policy. Treat recordings as private. The recorder excludes account names, card names, exception messages, request bodies, and credentials. This exclusion does not apply to HDT's own logs or process dumps.

Set `HDT_FLIGHT_RECORDER=0` before launching HDT to disable the recorder. Changing the variable requires an HDT restart. Other plugin diagnostics remain enabled.

## Interpret events

| Event | Observation | Limit |
| --- | --- | --- |
| `session.start`, `build.identity` | UTC anchor, CLR version, plugin version, binary hash, session ID | The hash identifies the plugin, not the full installation |
| `ui.dispatch` | Delay between posting a ping and its WPF callback | A delay does not establish a deadlock |
| `ui.stall`, `ui.recovered` | A ping exceeded two seconds, then the dispatcher resumed | Shorter stalls may appear only as slow dispatches |
| `render.sample` | Total rendering callbacks and dropped events | A callback does not prove that pixels reached the display |
| `view.state` | Changes to mode, menu flag, and Arena view state | This does not establish HearthMirror or rating-request progress |
| `battlegrounds.minions.pointer.down` | WPF received left-button input at the card-back control | Input receipt does not establish command completion |
| `battlegrounds.tab.changed` | The active Battlegrounds view changed | A view change does not establish successful rendering |
| `main.pointer.down` | WPF received main-window input | Only the target's type is retained |
| `ui.exception` | Exception type and HRESULT | Method stacks remain in `dispatcher-failures-<pid>.jsonl` |

Events include a schema version, session ID, sequence number, managed thread ID, and monotonic timestamp in microseconds. Sessions have separate clocks. The exporter keeps sessions distinct. It does not align independent process clocks.

The watchdog posts at most one outstanding ping. The file writer also checks the watchdog. If that writer blocks on filesystem IO, detection is delayed. Abrupt termination can lose queued events. Plugin unload requests a background drain without waiting on the UI thread.

If the writer fails, HDT's log reports the exception type. The existing input-region snapshot also exposes `state.flightRecorderFailure`, so a failure in the logger cannot erase the writer's failure status. The recorder stops accepting events after a writer failure.

## Run the isolated check

Use an already prepared replay environment:

```sh
devenv shell -- hdt-replay run --with-plugin --plaque-level 5 \
  --battlegrounds-tabs --diagnostics
```

The check blocks only the isolated replay UI for 3.5 seconds. It requires a stall capture, a delayed dispatcher acknowledgment, recovery, rendering samples, and a plugin hash. A separate account-free process checks queue overflow reporting, rotation, shutdown drain, and writer failure isolation.

Each run copies its source, tests, and scripts into a private directory before compilation. The namespace mounts that copy read-only. `source-sha256.txt` records individual file hashes. `result.json` records the hash of that manifest and whether the original checkout was dirty. The fixture uses this captured source rather than reading later edits from the checkout.

The Battlegrounds test raises synthetic WPF pointer events at the actual bound card-back control, executes its stored command, and checks six view changes. It does not establish native desktop input delivery or reproduce a live combat freeze. The normal replay also exercises twelve Arena offers, Settings, resize, a popup, and the New dialog. See [replay boundaries](UI-REPLAY.md).

## Remaining gaps

Existing HearthMirror reads, ArenaSmith request durations, native compositor acknowledgments, and graphics presentation completion do not yet have correlated traces. The recorder does not capture process dumps. Add observations to existing operations rather than polling the game or ratings service again.

The implementation uses .NET Framework APIs and the project's existing Bun tooling. It adds no OpenTelemetry SDK or remote collector. The exporter uses Perfetto's documented trace-event format to keep the initial diagnostic path local.
