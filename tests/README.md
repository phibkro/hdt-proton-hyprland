# Test suites

| Directory | Responsibility | Runner |
| --- | --- | --- |
| `host/` | Rust host configuration, launcher, journal and X11 boundaries | Cargo Nextest; targets declared in the root Cargo manifest |
| `cli/` | Actual launcher subprocesses, configuration refusal and CI classification | `bash scripts/test-unit.sh` |
| `replay/` | Real WPF recorder checks and private-display input journey | `hdt-replay run`; see the isolated replay guide |
| `native/` | C reference probes, XInput reproducer and their TypeScript drivers | `scripts/check-rust-x11.sh`, `scripts/check-xinput.sh` and the C quality gate |

Run commands from the repository root in its development shell. Native fixtures use private display namespaces; they do not drive the logged-in desktop. Benchmarks live in `benches/`, coverage-guided targets in `fuzz/`, and reviewed fuzzer seeds in `tooling/rust/corpus/`.

See [test strategy](../docs/development/testing-strategy.md) for what each result establishes, and [UI replay](../docs/guides/ui-replay.md) for prerequisites and cleanup. Generated captures and private runtime data stay outside these suites.
