#!/usr/bin/env bash
# Drives only the namespace-owned account-free replay display.
set -euo pipefail
root=$1 run=$2
[[ "$run" == "$root/local/replay/runs/"* ]]
jq -e --arg run "$run" '.kind=="hdt-ui-replay-run" and .run==$run' "$run/ownership.json" >/dev/null
jq -e '.sourcePrefixHidden and .hostDesktopVariablesAbsent' "$run/isolation.json" >/dev/null
source_prefix=$(jq -er .sourcePrefix "$root/local/replay/ownership.json")
[[ ! -e "$source_prefix/system.reg" && -z ${WAYLAND_DISPLAY-} && -n ${DISPLAY-} ]]
host=$run/source/hdt-host
state=$run/state.json
ack=$run/evidence/control-input.json
wait_for() {
    local label=$1 filter=$2 file=$3
    for ((attempt = 0; attempt < 70; attempt++)); do
        if [[ -s "$state" && -s "$ack" ]]; then
            jq -cn --arg label "$label" --slurpfile state "$state" --slurpfile ack "$ack" \
                '{label:$label,state:($state[0]|{observedUTC,foregroundHwnd,overlayHwnd,clickthrough,desktopControlHovered,desktopPointer,desktopClicks}),ack:$ack[0]}' \
                >>"$run/input-poll.jsonl"
        fi
        if [[ -s "$file" ]] && jq -e "$filter" "$file" >/dev/null; then return; fi
        sleep 0.1
    done
    echo "Timed out: $label" >&2
    cp "$state" "$run/failed-input-state.json"
    cp "$ack" "$run/failed-input-ack.json"
    xwininfo -root -tree >"$run/failed-input-tree.txt"
    xprop -root _NET_ACTIVE_WINDOW >"$run/failed-input-active.txt"
    xdotool getmouselocation --shell >"$run/failed-input-pointer.txt"
    # Diagnostic only, after failure is established. Never turn this into a pass:
    # find whether a subsequent real movement reaches WPF after native activation.
    xdotool mousemove_relative -- 1 0
    sleep 0.3
    cp "$state" "$run/failed-input-after-motion.json"
    return 1
}
move() {
    local x y
    read -r x y < <(xdotool getmouselocation --shell | awk -F= '/^X=/ {x=$2} /^Y=/ {y=$2} END {print x,y}')
    xdotool mousemove_relative -- "$(($1 - x))" "$(($2 - y))"
}
wait_for 'WPF control layout' '.desktopControl!=null' "$state"
jq -e '.overlayExtendedStyle / 134217728 | floor % 2 == 1' "$state" >/dev/null
for variant in wrong-session stale future wrong-hwnd fractional not-ready; do
    jq -n --arg variant "$variant" --argjson hwnd "$(jq .overlayHwnd "$state")" \
        '{backend:"x11",overlayHwnd:(if $variant=="wrong-hwnd" then $hwnd+1 else $hwnd end),session:(if $variant=="wrong-session" then "wrong" else "isolated-replay" end),nativeReady:($variant!="not-ready"),observedMS:((if $variant=="fractional" then now*1000+0.5 else (now*1000|floor) end)-(if $variant=="stale" then 5000 elif $variant=="future" then -5000 else 0 end))}' >"$ack.next"
    mv "$ack.next" "$ack"
    updates=$(jq .pluginUpdates "$state")
    # Two completed UI cycles exclude an update already in flight at rename.
    wait_for 'plugin consumed invalid acknowledgement' ".pluginUpdates>=$((updates + 2))" "$state"
    jq -e '.overlayExtendedStyle / 134217728 | floor % 2 == 1' "$state" >/dev/null
done
HS_APP_ID=1234567890 HDT_PROJECT_DIR="$run" "$host" x11-control isolated-replay >"$run/portable-controller.log" 2>&1 &
controller=$!
observer=''
cleanup() {
    if [[ -n "$observer" ]]; then
        kill -TERM "$observer" 2>/dev/null || true
        wait "$observer" || true
    fi
    kill -TERM "$controller" 2>/dev/null || true
    wait "$controller" || true
}
trap cleanup EXIT
"$host" x11-desktop windows steam_app_1234567890 >"$run/portable-windows.json"
game=$(jq -er '.[]|select(.title=="Hearthstone")|.address' "$run/portable-windows.json")
overlay=$(jq -er '.[]|select(.title=="HearthstoneOverlay")|.address' "$run/portable-windows.json")
read -r -a observer_flags <<<"$(pkg-config --cflags --libs x11)"
cc -std=c11 -Wall -Wextra -Werror "$run/source/tests/replay/pointer-observer.c" -o "$run/pointer-observer" "${observer_flags[@]}"
HDT_REPLAY_OBSERVER=1 timeout 30 "$run/pointer-observer" "$overlay" >"$run/x11-pointer-events.jsonl" 2>"$run/x11-pointer-events.stderr" &
observer=$!
wait_for 'X11 pointer observer ready' 'select(has("ready")) | .ready==true' "$run/x11-pointer-events.jsonl"
# Retain property types/terminators when Wine's native identity differs.
xwininfo -root -tree | awk '/"HearthstoneOverlay"|"Hearthstone Deck Tracker"/ {print $1}' | while read -r xid; do
    xprop -id "$xid" WM_NAME WM_CLASS _NET_WM_PID
done >"$run/portable-properties.txt"
read -r ox oy ow oh < <(jq -er '.[]|select(.title=="HearthstoneOverlay")|[.at[],.size[]]|@tsv' "$run/portable-windows.json")
xdotool windowsize "$game" "$ow" "$oh"
xdotool windowmove "$game" "$ox" "$oy"
xdotool windowactivate --sync "$game"
wait_for 'fresh native regions' '.fresh and .nativeReady and .controls>0' "$ack"
read -r cx cy < <(jq -r '[.desktopControl.x,.desktopControl.y]|@tsv' "$state")
for ((expected = 1; expected <= 6; expected++)); do
    before=$(jq .gameClicks "$run/game-fixture.json")
    move "$((ox + 5))" "$((oy + 5))"
    sleep 0.25
    xdotool click 1
    wait_for 'empty area reaches game fixture' ".gameClicks==$((before + 1))" "$run/game-fixture.json"
    jq -e ".desktopClicks==$((expected - 1))" "$state" >/dev/null
    move "$cx" "$cy"
    wait_for 'Win32 activation and cached click-through' '.foregroundHwnd==.overlayHwnd and .clickthrough==false' "$state"
    wait_for 'native control focus' '.fresh and .active and .focused and .pointerOverControl' "$ack"
    # Native focus acknowledgement can precede WPF's pointer-event dispatch.
    # Observe the actual control before sending its click; keep the click assertion.
    wait_for 'WPF control pointer delivery' '.desktopControlHovered==true' "$state"
    xdotool click 1
    wait_for 'actual WPF Click event' ".desktopClicks==$expected" "$state"
done
kill -TERM "$controller"
wait "$controller"
kill -TERM "$observer" 2>/dev/null || true
wait "$observer" || true
trap - EXIT
jq -e '.nativeReady==false' "$ack" >/dev/null
wait_for 'controller loss releases Win32 focus and activation style' '.foregroundHwnd!=.overlayHwnd and (.overlayExtendedStyle / 134217728 | floor % 2 == 1)' "$state"
jq -n '{wpfControlClicks:6,gameClickThrough:true,invalidReadinessRejected:true,controllerStopped:true,accountActivity:false}' >"$run/desktop-input-result.json"
echo 'Verified native Rust controller, six WPF clicks, game click-through and invalid-readiness rejection.'
