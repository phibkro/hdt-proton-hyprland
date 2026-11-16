#!/usr/bin/env bash
# Capture one fatal Xlib error in an explicitly selected existing HDT process.
set -euo pipefail
if (($# != 3)) || [[ ! $1 =~ ^[1-9][0-9]{0,9}$ || ! $3 =~ ^[1-9][0-9]{0,4}$ ]] || ((10#$3 > 86400)); then
    echo 'Usage: capture-native-error.sh HDT_PID NEW_DIRECTORY SECONDS(1..86400)' >&2
    exit 2
fi
tracker_pid=$1 output=$2 duration=$3
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
[[ $(cat "/proc/$tracker_pid/comm") == HearthstoneDeck ]] || {
    echo 'Target is not HDT.' >&2
    exit 1
}
[[ $(awk '/^TracerPid:/ {print $2}' "/proc/$tracker_pid/status") == 0 ]] || {
    echo 'HDT already has a debugger.' >&2
    exit 1
}
executable=$(readlink "/proc/$tracker_pid/exe")
case "${executable##*/}" in
    wine64-preloader | wine-preloader | wine64 | wine) ;;
    *)
        echo 'Expected the native Wine executable for HDT.' >&2
        exit 1
        ;;
esac
# The register layout used by the capture is the Linux x86-64 ABI.
[[ $(uname -m) == x86_64 ]] || {
    echo 'This capture requires x86-64 Linux.' >&2
    exit 1
}
gdb=$(command -v gdb)
umask 077
mkdir -m 700 -- "$output"
cp "$source_root/src/diagnostics/xlib-fatal.gdb" "$output/capture.gdb"
cat "/proc/$tracker_pid/stat" >"$output/target-start-stat.txt"
printf '%s\n' "$executable" >"$output/target-exe.txt"
sha256sum "$output/capture.gdb" "$gdb" >"$output/capture-sha256.txt"
printf 'Watching HDT PID %s for up to %s seconds. Private evidence: %s\n' "$tracker_pid" "$duration" "$output"
status=0
timeout --signal=TERM --kill-after=5 "$duration" "$gdb" -nx -nh -batch \
    -iex 'set auto-load off' -iex 'set debuginfod enabled off' \
    -ex "set sysroot /proc/$tracker_pid/root" -ex "attach $tracker_pid" -x "$output/capture.gdb" \
    -ex 'info address _XDefaultError' -ex 'info breakpoints' \
    -ex 'echo HDT_NATIVE_CAPTURE_ARMED\n' -ex continue >"$output/gdb.log" 2>&1 || status=$?
printf '%s\n' "$status" >"$output/debugger-exit.txt"
if grep -q '^HDT_NATIVE_XLIB_FATAL_END$' "$output/gdb.log"; then
    echo 'Captured a native Xlib fatal-error stack; debugger detached.'
elif ((status == 124)); then
    echo 'Capture deadline reached; no complete fatal-error stack was recorded.'
else
    echo "Capture ended without a complete fatal-error stack (debugger status $status). Inspect the private log." >&2
    # A debugger can exit zero after an unexpected stop; that is not a capture.
    exit 1
fi
