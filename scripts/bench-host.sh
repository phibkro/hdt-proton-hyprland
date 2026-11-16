#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
# alloca's build script adds C LTO when CC is literally "clang"; Nix's default
# GNU linker cannot consume that bitcode. An explicit compiler path emits the
# normal native object and keeps this benchmark independent of linker plugins.
CC=$(command -v clang)
export CC CARGO_BUILD_JOBS=2
exec cargo bench --locked --bench input_protocol -- "$@"
