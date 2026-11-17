#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
mkdir -p "$root/evidence"
run=$(mktemp -d "$root/evidence/visual-oracle-XXXXXXXX")
chmod 700 "$run"
magick -size 320x180 xc:'#303030' "$run/before.png"
magick "$run/before.png" -fill navy -draw 'rectangle 80,60 240,120' "$run/shown.png"
bash "$root/tests/replay/visual-triplet.sh" "$run/before.png" "$run/shown.png" "$run/before.png" "$run/positive"
reject() {
    local status=0
    bash "$root/tests/replay/visual-triplet.sh" "$run/before.png" "$1" "$2" "$run/$3" || status=$?
    [[ $status == 1 ]] || {
        echo "Negative control $3 returned $status, expected 1" >&2
        exit 1
    }
    jq -e '.passed==false' "$run/$3/result.json" >/dev/null
}
reject "$run/shown.png" "$run/shown.png" stale-popup
reject "$run/before.png" "$run/before.png" never-shown
# A persistent white surface must not count as successful clearing.
magick -size 320x180 xc:white "$run/white.png"
reject "$run/shown.png" "$run/white.png" white-after
echo "Visual classifier: positive and three negative controls passed. Evidence: $run"
