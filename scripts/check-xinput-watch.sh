#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --inside ]]; then
    [[ $PWD == /capture && ! -e /home && ! -e /run/user && ! -e /srv ]]
    [[ -z ${DISPLAY:-} && -z ${WAYLAND_DISPLAY:-} && -z ${XAUTHORITY:-} ]]
    mkdir -m 1777 /tmp/.X11-unix
    server_pid='' observer_pid=''
    # shellcheck disable=SC2329 # Invoked by the EXIT trap.
    cleanup() {
        for pid in "$observer_pid" "$server_pid"; do
            if [[ -n $pid ]]; then
                kill "$pid" 2>/dev/null || true
                wait "$pid" 2>/dev/null || true
            fi
        done
    }
    trap cleanup EXIT
    "$2" -displayfd 1 -screen 0 640x480x24 -nolisten tcp -noreset >display.txt 2>server.log &
    server_pid=$!
    for ((i = 0; i < 100; i++)); do
        [[ -s display.txt ]] && break
        sleep 0.05
    done
    number=$(cat display.txt)
    [[ $number =~ ^[0-9]+$ ]]
    export DISPLAY=:$number
    ./hdt-host xinput-watch --output devices.jsonl --seconds 3 >observer.log 2>&1 &
    observer_pid=$!
    for ((i = 0; i < 100; i++)); do
        if [[ -s devices.jsonl ]]; then break; fi
        kill -0 "$observer_pid"
        sleep 0.02
    done
    "$3" -e 'select(.kind=="capture.start")' devices.jsonl >/dev/null
    HDT_XINPUT_REPRO=1 ./xinput-race "$DISPLAY" guarded >fixture.log 2>&1
    # A separate client exits through the native Xlib error handler.
    status=0
    HDT_XINPUT_REPRO=1 ./xinput-race "$DISPLAY" fatal >>fixture.log 2>&1 || status=$?
    [[ $status == 1 ]]
    kill -0 "$observer_pid"
    # Exercise the same exact-entry capture used for the real Wine process.
    HDT_XINPUT_REPRO=1 "$4" -nx -nh -batch -iex 'set auto-load off' -iex 'set debuginfod enabled off' \
        -ex start -x xlib-fatal.gdb -ex continue \
        --args ./xinput-race "$DISPLAY" fatal >fatal-gdb.log 2>&1
    grep -q '^HDT_NATIVE_XLIB_FATAL_BEGIN$' fatal-gdb.log
    grep -q 'XGetDeviceButtonMapping' fatal-gdb.log
    grep -q '^HDT_MAPPING_QUERY .*device=[0-9]' fatal-gdb.log
    grep -q '^HDT_DEVICE_OPEN .*requested=[0-9]' fatal-gdb.log
    grep -q '^HDT_NATIVE_XLIB_FATAL_END$' fatal-gdb.log
    HDT_XINPUT_REPRO=1 "$4" -nx -nh -batch -iex 'set auto-load off' -iex 'set debuginfod enabled off' \
        -ex start -x xlib-fatal.gdb -ex continue \
        --args ./xinput-race "$DISPLAY" guarded >guarded-gdb.log 2>&1
    if grep -q '^HDT_NATIVE_XLIB_FATAL_BEGIN$' guarded-gdb.log; then
        echo 'Guarded request unexpectedly reached the fatal handler.' >&2
        exit 1
    fi
    grep -q 'exited normally' guarded-gdb.log
    # Reaching the trace quota must retain the independent fatal breakpoint.
    # shellcheck disable=SC2016 # The variable belongs to GDB, not the shell.
    HDT_XINPUT_REPRO=1 "$4" -nx -nh -batch -iex 'set auto-load off' -iex 'set debuginfod enabled off' \
        -ex start -x xlib-fatal.gdb -ex 'set $hdt_mapping_queries = 999' -ex continue \
        --args ./xinput-race "$DISPLAY" fatal >quota-gdb.log 2>&1
    [[ $(grep -c '^HDT_MAPPING_QUERY ' quota-gdb.log) == 1 ]]
    grep -q '^HDT_MAPPING_QUERY count=1000 ' quota-gdb.log
    grep -q '^HDT_NATIVE_XLIB_FATAL_END$' quota-gdb.log
    wait "$observer_pid"
    observer_pid=''
    "$3" -se '
      .[0].kind == "capture.start" and .[-1].kind == "capture.stop" and
      any(.[]; .kind == "input.hierarchy" and (.data.flags % 2 == 1)) and
      any(.[]; .kind == "input.hierarchy" and ((.data.flags / 2 | floor) % 2 == 1)) and
      all(.[]; .kind == "capture.start" or .kind == "capture.stop" or
        .kind == "input.hierarchy" or .kind == "input.deviceChanged")
    ' devices.jsonl >/dev/null
    cp devices.jsonl before.jsonl
    if ./hdt-host xinput-watch --output devices.jsonl --seconds 1 >>observer.log 2>&1; then
        echo 'Existing evidence was accepted for overwrite.' >&2
        exit 1
    fi
    cmp devices.jsonl before.jsonl
    echo 'Read-only observer captured device addition/removal and preserved existing evidence.'
    exit 0
fi
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
umask 077
mkdir -p evidence
capture=$(mktemp -d "$PWD/evidence/xinput-watch-XXXXXXXX")
cp "${CARGO_TARGET_DIR:-target}/debug/hdt-host" "$capture/hdt-host"
cp scripts/check-xinput-watch.sh tests/native/xinput-race.c src/diagnostics/xlib-fatal.gdb "$capture/"
read -r -a flags <<<"$(pkg-config --cflags --libs x11 xi)"
cc -Wall -Wextra -Werror "$capture/xinput-race.c" -o "$capture/xinput-race" "${flags[@]}"
(cd "$capture" && sha256sum hdt-host check-xinput-watch.sh xinput-race.c xinput-race xlib-fatal.gdb >sha256.txt)
shell=$(readlink -f "$(command -v bash)")
server=$(readlink -f "$(command -v Xvfb)")
json=$(readlink -f "$(command -v jq)")
gdb=$(readlink -f "$(command -v gdb)")
# Only this private server receives fixture-created input devices.
bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh \
    --bind "$capture" /capture --chdir /capture --setenv PATH "$PATH" \
    "$shell" /capture/check-xinput-watch.sh --inside "$server" "$json" "$gdb"
echo "Evidence: $capture"
