#!/usr/bin/env bash
set -euo pipefail
on_error() {
    local status=$?
    notify-send 'Hearthstone Deck Tracker' 'Could not open HDT. Check its configuration and the Steam game session.' || true
    exit "$status"
}
trap on_error ERR
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
fail() {
    echo "$*" >&2
    notify-send 'Hearthstone Deck Tracker' "$*" || true
    exit 1
}
action=${1:-open}
if (($# > 1)) || [[ "$action" != open && "$action" != restart ]]; then
    echo 'Usage: desktop.sh [open|restart]' >&2
    exit 2
fi
window_class=$(bash scripts/native-host.sh config windowClass)
clients=$(hyprctl -j clients)
trackers=$(jq -c --arg class "$window_class" '[.[] | select(.class==$class and .title=="Hearthstone Deck Tracker")]' <<<"$clients")
count=$(jq length <<<"$trackers")
if ((count > 1)); then fail 'Multiple HDT windows; refusing to guess.'; fi
if [[ "$action" == open && "$count" == 1 ]]; then
    address=$(jq -er '.[0].address | select(test("^0x[0-9a-fA-F]+$"))' <<<"$trackers")
    pid=$(jq -er '.[0].pid | select(type=="number" and .>0 and .==floor)' <<<"$trackers")
    [[ $(cat "/proc/$pid/comm") == HearthstoneDeck ]] || { fail 'Tracker identity changed.'; }
    exec hyprctl dispatch "hl.dsp.focus({window=\"address:$address\"})"
fi
# The service owns HDT only. Do not restart it if that boundary has changed.
service=$(systemctl --user show hdt-proton-autostart.service --property=ControlGroup --value)
if ((count == 1)); then
    tracker_pid=$(jq -er '.[0].pid | select(type=="number" and .>0 and .==floor)' <<<"$trackers")
    [[ $(cat "/proc/$tracker_pid/comm") == HearthstoneDeck ]] || fail 'Tracker identity changed.'
    owned=false
    while IFS=: read -r hierarchy _ group; do
        if [[ "$hierarchy" == 0 && -n "$service" && ("$group" == "$service" || "$group" == "$service/"*) ]]; then owned=true; fi
    done <"/proc/$tracker_pid/cgroup"
    [[ "$owned" == true ]] || fail 'HDT is outside its desktop service. Close HDT normally, then open it from the launcher.'
fi
games=$(jq -c --arg class "$window_class" '[.[] | select(.class==$class and .title=="Hearthstone") | .pid] | unique' <<<"$clients")
if (($(jq length <<<"$games") > 1)); then fail 'Multiple game processes; refusing a restart.'; fi
while IFS= read -r pid; do
    [[ "$pid" =~ ^[0-9]+$ ]] || { fail 'Invalid game process.'; }
    while IFS=: read -r hierarchy _ group; do
        if [[ "$hierarchy" == 0 && -n "$service" && ("$group" == "$service" || "$group" == "$service/"*) ]]; then
            fail 'Hearthstone belongs to the tracker service; refusing to restart it.'
        fi
    done <"/proc/$pid/cgroup"
done < <(jq -r '.[]' <<<"$games")
if [[ "$games" == '[]' ]]; then
    systemctl --user start hdt-proton-autostart.service
    notify-send 'Hearthstone Deck Tracker' 'Open Hearthstone through Steam. HDT starts when the game window appears.'
else
    exec systemctl --user restart hdt-proton-autostart.service
fi
