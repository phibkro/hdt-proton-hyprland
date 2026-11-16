#!/usr/bin/env bash
# Called inside the Nix build sandbox; never connect to an inherited display.
set -euo pipefail
if (($# != 4)); then
    echo 'Usage: libxi-open-device.sh ORIGINAL_LIB PATCHED_LIB FIXTURE XVFB' >&2
    exit 64
fi
original=$1 patched=$2 fixture=$3 xvfb=$4
# Refuse direct execution and unsandboxed Nix builds before touching X sockets.
[[ ${NIX_BUILD_TOP:-} == /build ]] || {
    echo 'This fixture requires the Nix build sandbox.' >&2
    exit 64
}
for forbidden in /home /srv /run/user; do
    if [[ -e $forbidden ]]; then
        echo 'A host directory is visible; refusing to start an X server.' >&2
        exit 64
    fi
done
shopt -s nullglob
sockets=(/tmp/.X11-unix/*)
((${#sockets[@]} == 0)) || {
    echo 'The fixture requires an empty private X socket directory.' >&2
    exit 64
}
unset DISPLAY WAYLAND_DISPLAY XAUTHORITY
mkdir -p /tmp/.X11-unix
server=''
cleanup() {
    local status=$?
    trap - EXIT
    if [[ -n $server ]]; then
        kill "$server" 2>/dev/null || true
        wait "$server" 2>/dev/null || true
    fi
    if [[ -f server.log ]]; then cat server.log >&2; fi
    exit "$status"
}
trap cleanup EXIT
"$xvfb" -displayfd 3 -screen 0 640x480x24 -nolisten tcp -noreset 3>display.txt >server.log 2>&1 &
server=$!
for ((attempt = 0; attempt < 100; attempt++)); do
    [[ -s display.txt ]] && break
    kill -0 "$server"
    sleep 0.05
done
read -r number <display.txt
[[ $number =~ ^[0-9]+$ ]] || exit 65
display=:$number
for variant in original patched; do
    library=$original
    [[ $variant == patched ]] && library=$patched
    for ((round = 1; round <= 3; round++)); do
        status=0
        HDT_LIBXI_REPRO=1 LD_LIBRARY_PATH="$library" timeout 45 "$fixture" "$display" >"$variant-$round.log" 2>&1 || status=$?
        printf '%s\n' "$status" >"$variant-$round.status"
        printf 'variant=%s round=%d exit=%d\n' "$variant" "$round" "$status"
        cat "$variant-$round.log"
        if [[ $variant == original ]]; then
            [[ $status == 1 ]]
            rg -q '^MISMATCH iteration=[0-9]+ requested=[0-9]+ returned=[0-9]+$' "$variant-$round.log"
        else
            [[ $status == 0 ]]
            rg -q '^completed=50000 mismatch=0$' "$variant-$round.log"
        fi
    done
done
printf 'Three original failures and three patched 50000-cycle passes.\n' >result.txt
