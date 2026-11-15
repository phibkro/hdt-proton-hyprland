#!/usr/bin/env bash
set -euo pipefail
root=$1 run=$2 proton=$3 negative=$4
export PATH=$HDT_REPLAY_TOOLS_PATH
[[ "$run" == "$root/local/replay/runs/"* ]]
jq -e --arg run "$run" '.kind=="hdt-ui-replay-run" and .run==$run' "$run/ownership.json" >/dev/null
[[ -z ${DISPLAY-} && -z ${WAYLAND_DISPLAY-} ]]
source_prefix=$(jq -er .sourcePrefix "$root/local/replay/ownership.json")
[[ ! -e "$source_prefix/system.reg" ]]
awk -F: 'NR>2 {gsub(/ /,"",$1); if($1!="lo") bad=1} END {exit bad}' /proc/net/dev
for tool in Xvfb openbox xcompmgr xdotool xwininfo magick; do command -v "$tool" >/dev/null; done
export WINEPREFIX=$run/prefix XDG_RUNTIME_DIR=$run/runtime
export WINEDLLOVERRIDES='mscoree=n,b' WINEDEBUG=-all
export DXVK_CONFIG='d3d9.shaderModel = 1' DXVK_LOG_PATH=$run
Xvfb -displayfd 3 -screen 0 2560x1440x24 -nolisten tcp -ac 3>"$run/display" >"$run/xvfb.log" 2>&1 &
xvfb_pid=$!
trap 'status=$?; if (( status )); then magick import -window root "$run/failure.png" 2>/dev/null || true; fi; kill "$xvfb_pid" 2>/dev/null || true' EXIT
for ((i=0;i<50;i++)); do [[ -s "$run/display" ]] && break; sleep 0.1; done
[[ -s "$run/display" ]]
kill -0 "$xvfb_pid"
DISPLAY=:$(cat "$run/display")
export DISPLAY
openbox >"$run/openbox.log" 2>&1 &
xcompmgr >"$run/compositor.log" 2>&1 &
jq -n --arg net "$(readlink /proc/self/ns/net)" --arg pid "$(readlink /proc/self/ns/pid)" \
    '{networkNamespace:$net,pidNamespace:$pid,sourcePrefixHidden:true,hostDesktopVariablesAbsent:true}' >"$run/isolation.json"
cd "$run/app"
cp HearthstoneDeckTracker.exe.config ReplayHost.exe.config
winpath() { printf 'Z:%s' "${1//\//\\}"; }
wpf='C:\windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
    /nologo /target:winexe /platform:x64 /out:ReplayHost.exe /r:System.Xaml.dll /r:System.Web.Extensions.dll \
    /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
    "$(winpath "$root/src/replay/ReplayHost.cs")" >"$run/compile.log" 2>&1
timeout 70 "$proton/files/bin/wine" "$run/app/ReplayHost.exe" "$(winpath "$run/host.log")" >"$run/wine.log" 2>&1 &
host_pid=$!
for ((i=0;i<200;i++)); do
    [[ -s "$run/state.json" ]] && break
    kill -0 "$host_pid" 2>/dev/null || break
    sleep 0.1
done
xwininfo -root -tree >"$run/windows.txt"
magick import -window root "$run/startup.png"
[[ -s "$run/state.json" ]]
jq -e '.settingsButton!=null and .commandId==0' "$run/state.json" >/dev/null
id=0
send() {
    id=$((id+1))
    jq -n --arg op "$1" --argjson id "$id" '{id:$id,op:$op}' >"$run/command.json.next"
    mv "$run/command.json.next" "$run/command.json"
    for ((i=0;i<50;i++)); do
        [[ -s "$run/state.json" ]] && jq -e --argjson id "$id" '.commandId==$id' "$run/state.json" >/dev/null && return
        sleep 0.1
    done
    echo "HDT did not acknowledge replay command $id" >&2
    return 1
}
settings() {
    read -r x y < <(jq -r '[.settingsButton.x,.settingsButton.y]|@tsv' "$run/state.json")
    xdotool mousemove --sync "$x" "$y" click 1
    for ((i=0;i<30;i++)); do jq -e '.settingsOpen' "$run/state.json" >/dev/null && break; sleep 0.1; done
    jq -e '.settingsOpen' "$run/state.json" >/dev/null
    cp "$run/state.json" "$run/settings-$id.json"
    send settings-close
}
settings
for phase in arena-lobby draft-1 draft-2 menu; do
    send "$phase"
    sleep 1
    cp "$run/state.json" "$run/$phase.json"
    magick import -window root "$run/$phase.png"
    settings
done
jq -e '.cards==["CS2_029","CS2_032","CS2_172"] and .visibleScores==["60","61","62"]' "$run/draft-1.json" >/dev/null
jq -e '.cards==["CS2_024","CS2_025","CS2_182"] and .visibleScores==["80","81","82"]' "$run/draft-2.json" >/dev/null
jq -e '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]' "$run/menu.json" >/dev/null
mapfile -t mains < <(xdotool search --onlyvisible --name '^Hearthstone Deck Tracker$')
[[ ${#mains[@]} == 1 ]]
xdotool windowsize "${mains[0]}" 1280 720
sleep 0.5
jq -e '.mainWidth>=1250 and .mainHeight>=700' "$run/state.json" >/dev/null
settings
send popup-show
sleep 0.3
jq -e '.popupOpen' "$run/state.json" >/dev/null
magick import -window root "$run/popup.png"
send popup-hide
jq -e '.popupOpen==false' "$run/state.json" >/dev/null
send new
sleep 1
cp "$run/state.json" "$run/new.json"
jq -e '.visibleDialogTypes | any(endswith("DeckTypeDialog"))' "$run/new.json" >/dev/null
magick import -window root "$run/new.png"
if [[ "$negative" == true ]]; then
    echo 'Deliberately asserting that the current offer is an impossible fixture.' >&2
    jq -e '.cards==["INTENTIONAL_ASSERTION_FAILURE"]' "$run/state.json" >/dev/null
fi
send finish
wait "$host_pid"
if rg -q '^(dispatcher|command|startup) ' "$run/host.log"; then
    echo 'The fixture host recorded an exception.' >&2
    exit 1
fi
echo 'Verified fixture transitions, rendered score bindings, native Settings input, popup, resize, New dialog, and host shutdown.'
