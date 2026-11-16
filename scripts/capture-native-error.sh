#!/usr/bin/env bash
# Capture one fatal Xlib error in an explicitly selected existing HDT process.
set -euo pipefail
if (($# != 3)) || [[ ! $1 =~ ^[1-9][0-9]{0,9}$ || ! $3 =~ ^[1-9][0-9]{0,4}$ ]] || ((10#$3 > 86400)); then
    echo 'Usage: capture-native-error.sh HDT_PID NEW_DIRECTORY SECONDS(1..86400)' >&2
    exit 2
fi
tracker_pid=$1 output=$2 duration=$3
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
identity=$(bash "$source_root/scripts/native-target-identity.sh" snapshot "$tracker_pid")
mapfile -t identity_fields <<<"$identity"
executable=${identity_fields[3]}
[[ $(awk '/^TracerPid:/ {print $2}' "/proc/$tracker_pid/status") == 0 ]] || {
    echo 'HDT already has a debugger.' >&2
    exit 1
}
# The register layout used by the capture is the Linux x86-64 ABI.
[[ $(uname -m) == x86_64 ]] || {
    echo 'This capture requires x86-64 Linux.' >&2
    exit 1
}
gdb=$(command -v gdb)
umask 077
mkdir -m 700 -- "$output"
output=$(cd -- "$output" && pwd -P)
cp "$source_root/src/diagnostics/xlib-fatal.gdb" "$output/capture.gdb"
cp "$source_root/src/diagnostics/xlib-identity.gdb" "$output/identity.gdb"
cp "$source_root/scripts/native-target-identity.sh" "$output/identity.sh"
printf '%s\n' "$identity" >"$output/target-identity.txt"
export HDT_NATIVE_CAPTURE_BASH HDT_NATIVE_CAPTURE_GUARD HDT_NATIVE_CAPTURE_PID HDT_NATIVE_CAPTURE_IDENTITY
HDT_NATIVE_CAPTURE_BASH=$(command -v bash)
HDT_NATIVE_CAPTURE_GUARD=$output/identity.sh
HDT_NATIVE_CAPTURE_PID=$tracker_pid
HDT_NATIVE_CAPTURE_IDENTITY=$output/target-identity.txt
cat "/proc/$tracker_pid/stat" >"$output/target-start-stat.txt"
cat "/proc/$tracker_pid/maps" >"$output/target-maps.txt"
printf '%s\n' "$executable" >"$output/target-exe.txt"
sha256sum "$output/capture.gdb" "$output/identity.gdb" "$output/identity.sh" "$gdb" >"$output/capture-sha256.txt"
printf 'Watching HDT PID %s for up to %s seconds. Private evidence: %s\n' "$tracker_pid" "$duration" "$output"
status=0
timeout --signal=TERM --kill-after=5 "$duration" "$gdb" -nx -nh -batch \
    -iex 'set auto-load off' -iex 'set debuginfod enabled off' \
    -ex "set sysroot /proc/$tracker_pid/root" -ex "attach $tracker_pid" \
    -x "$output/identity.gdb" -x "$output/capture.gdb" \
    -ex 'info address _XDefaultError' -ex 'info breakpoints' \
    -ex 'echo HDT_NATIVE_CAPTURE_ARMED\n' -ex continue >"$output/gdb.log" 2>&1 || status=$?
printf '%s\n' "$status" >"$output/debugger-exit.txt"
if ((status == 0)) && grep -q '^HDT_NATIVE_CAPTURE_IDENTITY_VERIFIED$' "$output/gdb.log" \
    && grep -q '^HDT_NATIVE_XLIB_FATAL_END$' "$output/gdb.log"; then
    echo 'Captured a native Xlib fatal-error stack; debugger detached.'
elif grep -q '^HDT_NATIVE_XLIB_FATAL_BEGIN$' "$output/gdb.log"; then
    echo "Partial native stack retained; debugger failed before completing capture (status $status)." >&2
    exit 1
elif ((status == 124)); then
    echo 'Capture deadline reached; no complete fatal-error stack was recorded.'
    exit 124
else
    echo "Capture ended without a complete fatal-error stack (debugger status $status). Inspect the private log." >&2
    # A debugger can exit zero after an unexpected stop; that is not a capture.
    exit 1
fi
