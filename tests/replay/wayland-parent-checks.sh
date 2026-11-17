#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
run=$(mktemp -d)
trap 'rm -rf -- "$run"' EXIT
cat >"$run/compatible" <<'REGISTRY'
interface: 'wl_seat',              version:  9, name: 1
interface: 'wl_compositor',        version:  6, name: 2
interface: 'xdg_wm_base',          version:  6, name: 3
interface: 'wl_shm',               version:  1, name: 4
interface: 'zwp_linux_dmabuf_v1',   version:  4, name: 5
REGISTRY
bash "$root/tests/replay/wayland-parent-check.sh" "$run/compatible" >"$run/result"
jq -e '.passed and (.requirements|length==5) and all(.requirements[];.observed==.required)' "$run/result" >/dev/null
# A newer duplicate global must not be hidden by an older announcement.
printf "interface: 'wl_seat', version: 7, name: 6\n" >>"$run/compatible"
bash "$root/tests/replay/wayland-parent-check.sh" "$run/compatible" >/dev/null
reject() {
    local status=0
    bash "$root/tests/replay/wayland-parent-check.sh" "$1" >"$run/result" || status=$?
    [[ $status == 1 ]]
    jq -e '.passed==false' "$run/result" >/dev/null
}
# Real Weston 16 registry versions: all four incompatible globals must appear.
sed -e "/wl_seat/s/version:  *9/version:  7/" \
    -e "/wl_compositor/s/version:  *6/version:  5/" \
    -e "/xdg_wm_base/s/version:  *6/version:  5/" \
    -e "/zwp_linux_dmabuf_v1/s/version:  *4/version:  3/" "$run/compatible" >"$run/old"
reject "$run/old"
jq -e '[.requirements[]|select(.passed==false)]|length==4' "$run/result" >/dev/null
sed '/zwp_linux_dmabuf_v1/d' "$run/compatible" >"$run/missing"
reject "$run/missing"
jq -e '.requirements[]|select(.interface=="zwp_linux_dmabuf_v1")|.observed==null' "$run/result" >/dev/null
: >"$run/empty"
reject "$run/empty"
printf "interface: 'wl_seat', version: not-a-number, name: 1\n" >"$run/malformed"
reject "$run/malformed"
jq -e '.malformedRecords==1' "$run/result" >/dev/null
# A malformed record must fail even when all required globals also look valid.
cat "$run/compatible" "$run/malformed" >"$run/mixed"
reject "$run/mixed"
jq -e '.malformedRecords==1 and all(.requirements[];.passed)' "$run/result" >/dev/null
# Partial numeric tokens and missing record fields are malformed, not versions.
sed 's/version:  9/version:  9oops/' "$run/compatible" >"$run/partial"
reject "$run/partial"
sed 's/, name: 5//' "$run/compatible" >"$run/truncated"
reject "$run/truncated"
echo 'Wayland parent validator: minimum versions, duplicate, old, missing, empty, malformed and mixed cases passed.'
