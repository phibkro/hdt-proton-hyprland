#!/usr/bin/env bash
# Prerequisite doctor only; this does not drive HDT or establish desktop acceptance.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
[[ $# -eq 0 ]] || {
    echo 'Usage: check-portable-desktop.sh (private KWin prerequisite doctor)' >&2
    exit 64
}
exec nix-shell nix/desktop-acceptance.nix --run 'bash tests/native/portable-desktop-doctor.sh'
