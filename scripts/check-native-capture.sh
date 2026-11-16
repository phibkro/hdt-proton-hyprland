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
    ./wine64 "$@" >target-ready.txt &
    owned_pid=$!
    for ((i = 0; i < 100; i++)); do
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
cat >mock-bin/gdb <<'SH'
#!/bin/sh
case "$HDT_TEST_RESULT" in
    failed-after-end) echo HDT_NATIVE_XLIB_FATAL_END; exit 1 ;;
    partial) echo HDT_NATIVE_XLIB_FATAL_BEGIN; exit 0 ;;
    unexpected) exit 0 ;;
esac
SH
chmod +x mock-bin/gdb
start_target
for result in failed-after-end partial unexpected; do
    status=0
    HDT_TEST_RESULT=$result PATH="$PWD/mock-bin:$PATH" bash scripts/capture-native-error.sh \
        "$owned_pid" "wrapper-$result" 2 >"wrapper-$result.log" 2>&1 || status=$?
    [[ $status == 1 ]]
    assert_released
done
echo 'Actual wrapper: completion, deadline with target alive/untraced, and failure controls passed.'
