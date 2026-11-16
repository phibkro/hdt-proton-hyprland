#!/usr/bin/env bash
# Bounded, offline-after-fetch coverage-guided parser checks. No display needed.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/fuzz-XXXXXXXX")
chmod 700 "$run"
cargo metadata --locked --manifest-path fuzz/Cargo.toml --format-version 1 >"$run/dependencies.json"
cargo fmt --manifest-path fuzz/Cargo.toml --all -- --check
for target in input_frame recorder_event; do
    mkdir "$run/$target" "$run/$target-artifacts"
    cp "tooling/rust/corpus/$target/valid" "$run/$target/valid"
    # The pinned Nix compiler is stable. Enable its experimental coverage
    # flags only for this isolated development build, never production.
    RUSTC_BOOTSTRAP=1 CARGO_NET_OFFLINE=true cargo fuzz run "$target" "$run/$target" \
        --sanitizer none --jobs 1 -- \
        -max_total_time=10 -timeout=2 -rss_limit_mb=1024 -max_len=70000 \
        "-artifact_prefix=$run/$target-artifacts/" >"$run/$target.log" 2>&1
    tail -n 8 "$run/$target.log"
done
sha256sum fuzz/Cargo.lock fuzz/fuzz_targets/*.rs >"$run/source-sha256.txt"
echo "Bounded coverage-guided fuzz smoke passed. Evidence: $run"
