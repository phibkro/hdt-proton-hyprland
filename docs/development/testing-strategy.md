# Testing strategy

The suite combines fast contracts with tests at the actual operating-system
boundary. Passing a model does not establish Wine, compositor or live-game
behavior. Static analysis, generated cases, protocol acceptance and operator
observations remain separate evidence.

| Layer | Maintained entry point | Evidence |
|---|---|---|
| Static checks | `bun scripts/quality.ts check all` | Formats, types, analyzers and dependency policy |
| Rust contracts and properties | `cargo nextest run --locked --config-file tooling/rust/nextest.toml --test-threads 2` | Unit, CLI/file boundary and shrinking generated cases |
| X11 protocol/fault tests | `bash scripts/check-rust-x11.sh` | Real private X server, owner exclusion, freshness, corrupt/missing producer recovery, shape/focus cleanup, C/Rust pixel comparison |
| WPF journey | `bash scripts/replay.sh run --with-plugin --without-particles --desktop-input` | Native clicks through Rust, Wine and the actual HDT overlay |
| Parser fuzzing | `bash scripts/check-fuzz.sh` | Two bounded, coverage-guided smoke runs with retained corpus/artifacts |
| Rust performance | `bash scripts/bench-host.sh` | Criterion distributions for 1–2048 control regions |
| Live acceptance | [Verification workflow](pr-review.md) | Real game subscriptions, ratings, match saves and desktop behavior |

Run commands from the pinned development shell. Measure headroom before builds,
replays, fuzzing and benchmarks. The coordinator owns runtime execution; limit
builds to two jobs and do not run multiple UI drivers against one instance.

## Generated and stateful tests

Proptest exercises the production frame parser with arbitrary bytes and checks
rectangle acceptance against an independent geometric predicate. Generated
producer-operation sequences cover publication, clock advance/rollback, window
resize, session replacement and missing handles. Failures shrink to a smaller
sequence. The default suite runs 256 cases per property and keeps regression
seeds in private ignored regression files until reviewed for publication.

The real X11 fault journey complements those models: competing owners must not
change an existing region; malformed, missing, old, future or wrong-session
producer data must remove input; a later valid snapshot must recover. These
checks touch the actual controller files and X Shape server state.

## Fuzzing

The separate `fuzz/` manifest locks libFuzzer dependencies. Targets call production
input-frame and recorder-event parsers without opening displays or accessing
accounts. `scripts/check-fuzz.sh` copies reviewed seed inputs into a fresh private
evidence directory, bounds time, per-input timeout, input size and memory, and
retains logs/artifacts. A short smoke run is a regression gate, not exhaustive
coverage or proof of memory safety.

Cargo-fuzz uses experimental compiler coverage flags. The pinned
Nix compiler is stable, so the script scopes `RUSTC_BOOTSTRAP=1` to fuzz commands;
normal builds and installed binaries do not receive it. This avoids a second,
unpinned nightly toolchain. The pinned Nix rustc lacks its AddressSanitizer runtime archive; an attempted
ASan build failed at linking. The maintained command explicitly selects
`--sanitizer none`, so it tests coverage-guided behavior without a memory-safety
sanitizer claim. A fully instrumented campaign needs a separately pinned compiler
distribution with sanitizer runtimes; it is not silently substituted. Longer campaigns may reuse a reviewed
private corpus, with an explicitly larger resource budget.

## Performance and reliability

Criterion benchmarks the native parser only. Keep raw results under the private
Cargo target directory and compare on the same machine/toolchain. Shared CI hosts
are unsuitable for a tight wall-clock regression threshold; PR checks compile
benchmark code, while performance runs produce measurements for review.

These microbenchmarks cannot explain WPF menu jitter. That requires end-to-end
dispatch latency, frame timing, memory growth and repeated Settings/Battlegrounds
menu journeys under each Proton version. Long-session soak tests and controlled
KDE/GNOME acceptance remain future work, not implied by generated test counts.

Nextest already supplies process isolation and concurrency control. CI uses no
retries to avoid concealing intermittent failures. Proptest, Criterion and
cargo-fuzz were chosen for shrinking examples, statistical measurements and
coverage-guided input generation respectively. Loom is deferred: current focus
ownership crosses X11/process boundaries rather than shared-memory Rust locks.

Tool behavior references: [Proptest state-machine testing](https://proptest-rs.github.io/proptest/proptest/state-machine.html),
[Cargo-fuzz](https://rust-fuzz.github.io/book/cargo-fuzz.html),
[Criterion](https://criterion-rs.github.io/book/) and
[Nextest configuration](https://nexte.st/docs/configuration/reference/).

The benchmark wrapper resolves Clang to its absolute pinned path. This avoids
alloca's string-triggered C LTO mode, which produced LLVM bitcode that the default
GNU linker rejected during verification. It does not change production linking.
The fuzz wrapper uses one libFuzzer process: an unsanitized two-worker fork run
failed in the native fuzzer supervisor (`free(): invalid size`), while the empty
artifact replay and single-process campaign succeeded. This does not attribute
that supervisor failure to HDT's Rust parser.

## Evaluate the development suite

Run `bash scripts/evaluate.sh` for a sequential evaluation of static gates,
Nextest and subprocess tests. Each run creates a private JSON report with the
Git revision, dirty-source flag, compiler version, elapsed milliseconds and
expected/actual exit results. Known-good and known-bad Rust/compiler and
ShellCheck fixtures verify specific diagnostic codes as positive/negative
controls. `bash scripts/evaluate.sh sentinels` runs only those analyzer controls;
CI runs this mode and writes the timing table into its job summary.

The report treats a correctly rejected bad fixture as a passing control. It does
not reinterpret a real suite failure as an expected rejection. Repeated reports
can compare cold/warm runs on the same runner; no cross-machine timing threshold
or overall "quality score" is invented. Compiler time, test time, fuzzing time
and UI latency remain distinct measurements. Current sentinels test selected
rules, not every Roslyn, Clippy, TS or shell diagnostic.
