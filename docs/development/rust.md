# Native Linux host

The migration branch routes normal Linux host operations through `hdt-host`, a
single Rust executable. HDT itself remains a Windows .NET Framework application
under the selected Proton runtime. Its compatibility plugin, flight recorder,
and WPF replay host remain C#. Hyprland still executes its Lua rules.

```text
Steam / desktop service
  -> Bash command boundary
    -> hdt-host (Rust)
       config, Proton discovery/launch, input bridge, status/setup,
       independent journal, trace export, X11 transient/pixel helpers
    -> Proton / HDT + C# compatibility plugin
    -> Hyprland Lua rules
```

The installed Nix package supplies the Rust executable and shell/system tools;
it does not require Bun, a TypeScript interpreter, or the old production C
executables. The superseded TypeScript runtime sources were retired after differential
comparison and migration of their regression coverage to Rust. TypeScript now
remains only for development checks and subprocess/protocol test drivers.
The C XInput fixture remains an intentional protocol reproducer, and the C pixel
probe supplies a differential reference in the isolated acceptance test.

## Build and check

Enter the pinned environment before using Cargo. The devenv shell explicitly
selects its pinned C compiler and archiver, so inherited compiler settings cannot
make the benchmark's native dependency emit Clang bitcode for a GCC linker:


```sh
devenv shell -- cargo fetch --locked
devenv shell -- cargo build --locked
devenv shell -- cargo fmt --all -- --check
devenv shell -- cargo clippy --locked --offline --all-targets -- -D warnings
devenv shell -- cargo nextest run --locked --config-file nextest.toml --test-threads 2
devenv shell -- cargo test --locked --doc
devenv shell -- cargo deny --config deny.toml check
devenv shell -- cargo machete
```

[rustfmt](https://github.com/rust-lang/rustfmt) owns Rust formatting;
[Clippy](https://doc.rust-lang.org/clippy/) and compiler warnings check all targets,
including tests. [rust-analyzer](https://rust-analyzer.github.io/book/) provides editor navigation and diagnostics.
[Nextest](https://nexte.st/) runs tests; doctests remain a separate Cargo command.
[cargo-deny](https://embarkstudios.github.io/cargo-deny/) checks
advisories, licenses, dependency bans and sources; its advisory database requires
network access on initial setup. [cargo-machete](https://github.com/bnjbvr/cargo-machete) detects unused direct dependencies.
These are checks with different scopes, not interchangeable proofs of safety.

The central quality driver exposes `rust-fast` for staged format/Clippy checks
and `rust` for the full sequence. Pre-commit retains staged/unstaged isolation;
there is no second hook installer. CI adds an independent Rust job to the
required aggregate. Configurations are ordinary root files: `rustfmt.toml`, `clippy.toml`,
`nextest.toml` and `deny.toml`. Rust and Cargo come from `devenv.lock`,
crate versions from `Cargo.lock`; rustup does not choose a separate toolchain.

Build jobs are capped at two by these check drivers. Resource-heavy checks still
require the operator's measured headroom policy. Per-worktree caches are under
`cache/cargo` and `local/rust-target`.

## Repeatable commands

Existing `hdt-proton`/`hdt-overlay-launch` commands remain the user entry points.
For development, after building:

```sh
devenv shell -- bash scripts/hdt.sh status
devenv shell -- bash scripts/native-host.sh config windowClass
devenv shell -- bash scripts/hdt.sh trace-export /absolute/new-trace.json /absolute/events.jsonl
devenv shell -- bash scripts/hdt.sh observe --output /absolute/new-capture --from start --max-bytes 10485760 --source power=/absolute/Power.log
```

Configuration still requires a verified saved Steam shortcut ID or `HS_APP_ID`.
Automatic shortcut discovery is not implemented. The Rust migration preserves
prefix selection rather than silently guessing a new prefix or Proton version.

## Runtime boundaries and verification

The journal remains a separate native process that can outlive HDT. It preserves
source bytes, offsets and generations; JSON field order and observation clocks
need not match JavaScript serialization byte-for-byte. Rust OS error wording
can differ. Malformed/overflowed JSON numbers are rejected rather than coerced.
No session environment or credential values are included in diagnostics.

`check-rust-x11.sh` creates a private Bubblewrap PID/mount/network environment,
starts its own Xvfb server, and exercises the actual executable. It verifies
identity refusal, transient apply/readback/guarded restore, known ARGB pixels,
and differential output against the original C surface probe. It captures no
host desktop window. Run it only after checking resource headroom:

```sh
devenv shell -- bash scripts/check-rust-x11.sh
```

The default Rust suite deliberately excludes that explicitly isolated test; its
separate result is required evidence. It does not establish real Hyprland,
Steam namespace, Wine, HearthMirror or ArenaSmith behavior. Live end-to-end
acceptance was subsequently recorded in [Observed compatibility](../reference/compatibility.md#rust-host-acceptance-2026-11-16).
Building and isolated testing do not change the installed service or live prefix;
the desktop acceptance used an explicit temporary service override.

See [the migration contract](../specs/rust-host.md) for recorded results and limits.
Tier-five particles remain suppressed; a language migration does not fix the
underlying WPF rendering path or native Wine XInput failures.

## Recorded replay result

Committed revision `e653143` passed the isolated WPF sequence with the plugin,
particle suppression, Battlegrounds tabs, and diagnostics. Its result records a
clean source tree, exit status zero, and a hashed source/binary snapshot. The Rust
collector and exporter ran alongside the actual HDT/WPF controls on Proton 11.0
from the existing account-free seed. This does not overturn the live Stable
submenu observations or change the recommended Experimental baseline.

An earlier Experimental attempt timed out during plugin compilation before the
UI sequence. A second attempt was invalidated by editing its outer script while
it ran and is not accepted evidence. The clean committed rerun above supplies
the accepted result. Clean revision `9c16a00` subsequently passed separate canonical
event and WPF/diagnostics replays, including Battlegrounds tabs, on the isolated
Proton 11.0 seed. Live Steam/Hyprland acceptance then passed on Experimental;
see the compatibility record for its scope. The isolated Experimental startup
timeout remains unresolved.

## Timestamp boundary

`src/host/timestamp.rs` owns RFC3339 parsing, UTC millisecond status/journal
output, native record formatting and signed age calculations. It uses
[`time`](https://docs.rs/time/latest/time/struct.OffsetDateTime.html); Chrono was
removed after its maintenance advisory. The boundary retains existing numeric
offsets, .NET seven-digit fractions, automatic 0/3/6/9-digit native record
precision and leap-second representation. Freshness callers retain their own
explicit inclusive or exclusive thresholds.

The adapter has fixed legacy-input and boundary tests checked against the former
Chrono 0.4.45 behavior. Journal integration tests also inspect the actual serialized
rows. Leap-endpoint duration behavior follows the explicitly retained
[Chrono 0.4.45 rule](https://github.com/chronotope/chrono/blob/v0.4.45/src/naive/time/mod.rs#L741)
(MIT OR Apache-2.0): crossing an endpoint's UTC base second adds or subtracts
a second independently of the date difference. Tests distinguish this from
Unix epoch subtraction. These checks cover timestamp compatibility; they do not establish overlay
or live game behavior. Jiff was considered, but its default
[Temporal parser](https://docs.rs/jiff/latest/jiff/fmt/temporal/) accepts a broader
input language than this RFC3339 boundary needs.
