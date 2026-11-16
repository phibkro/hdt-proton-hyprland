#!/usr/bin/env bash
set -euo pipefail
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
export HDT_SOURCE_ROOT="${HDT_SOURCE_ROOT:-$source_root}"
host=${HDT_HOST_BINARY:-${CARGO_TARGET_DIR:-$source_root/target}/debug/hdt-host}
if [[ ! -x "$host" ]]; then
    echo 'Build the native host first: devenv shell -- cargo build --locked' >&2
    exit 127
fi
exec "$host" "$@"
