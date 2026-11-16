#!/usr/bin/env bash
# Runs only inside check-xinput-watch.sh's private namespace.
set -euo pipefail
[[ $PWD == /capture && ! -e /home && ! -e /run/user && ! -e /srv ]]
owned_pid=''
# shellcheck disable=SC2329 # Invoked by EXIT trap.
cleanup() {
    if [[ -n $owned_pid ]]; then
        kill "$owned_pid" 2>/dev/null || true
        wait "$owned_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT
start_target() {
    rm -f target-ready.txt
    ./wine64 "$@" >target-ready.txt &
    owned_pid=$!
    for ((i = 0; i < 100; i++)); do
        kill -0 "$owned_pid"
        [[ -s target-ready.txt ]] && return
        sleep 0.02
    done
    return 1
}
assert_released() {
    kill -0 "$owned_pid"
    [[ $(awk '/^TracerPid:/ {print $2}' "/proc/$owned_pid/status") == 0 ]]
}
start_target
status=0
bash scripts/capture-native-error.sh "$owned_pid" wrapper-deadline 1 >wrapper-deadline.log 2>&1 || status=$?
[[ $status == 124 ]]
assert_released
cleanup
owned_pid=''
start_target fatal
bash scripts/capture-native-error.sh "$owned_pid" wrapper-complete 10 >wrapper-complete.log 2>&1
assert_released
cleanup
owned_pid=''
# Protocol controls verify wrapper classification, not debugger or Wine behavior.
mkdir mock-bin
export HDT_TEST_REAL_GDB
HDT_TEST_REAL_GDB=$(command -v gdb)
cat >mock-bin/gdb <<'SH'
#!/bin/sh
case "$HDT_TEST_RESULT" in
    mismatch)
        echo mismatched-identity >"$HDT_NATIVE_CAPTURE_IDENTITY"
        exec "$HDT_TEST_REAL_GDB" "$@"
        ;;
    failed-after-end)
        echo HDT_NATIVE_CAPTURE_IDENTITY_VERIFIED
        echo HDT_NATIVE_XLIB_FATAL_END
        exit 1
        ;;
    partial) echo HDT_NATIVE_XLIB_FATAL_BEGIN; exit 0 ;;
    unexpected) exit 0 ;;
esac
SH
chmod +x mock-bin/gdb
start_target
for result in failed-after-end partial unexpected mismatch; do
    status=0
    HDT_TEST_RESULT=$result PATH="$PWD/mock-bin:$PATH" bash scripts/capture-native-error.sh \
        "$owned_pid" "wrapper-$result" 2 >"wrapper-$result.log" 2>&1 || status=$?
    [[ $status == 1 ]]
    assert_released
done
grep -q '^HDT_NATIVE_CAPTURE_IDENTITY_MISMATCH$' wrapper-mismatch/gdb.log
if grep -Eq 'HDT_NATIVE_CAPTURE_ARMED|Hardware assisted breakpoint|HDT_NATIVE_CAPTURE_IDENTITY_VERIFIED' wrapper-mismatch/gdb.log; then
    echo 'Mismatched identity installed breakpoints or continued.' >&2
    exit 1
fi
echo 'Actual wrapper: completion, deadline with target alive/untraced, and failure controls passed.'
