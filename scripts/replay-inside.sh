#!/usr/bin/env bash
set -euo pipefail
root=$1 run=$2 proton=$3 negative=$4 plugin=$5 level=$6 particles=$7
snapshot=$run/source
desktop_input=${8:-false}
battlegrounds_tabs=${9:-false}
diagnostics=${10:-false}
export PATH=$HDT_REPLAY_TOOLS_PATH
[[ "$run" == "$root/local/replay/runs/"* ]]
jq -e --arg run "$run" '.kind=="hdt-ui-replay-run" and .run==$run' "$run/ownership.json" >/dev/null
[[ -z ${DISPLAY-} && -z ${WAYLAND_DISPLAY-} ]]
source_prefix=$(jq -er .sourcePrefix "$root/local/replay/ownership.json")
[[ ! -e "$source_prefix/system.reg" ]]
awk -F: 'NR>2 {gsub(/ /,"",$1); if($1!="lo") bad=1} END {exit bad}' /proc/net/dev
for tool in Xvfb openbox xcompmgr xdotool xwininfo magick; do command -v "$tool" >/dev/null; done
export WINEPREFIX=$run/prefix XDG_RUNTIME_DIR=$run/runtime
export WINEDLLOVERRIDES='mscoree=n,b;d3d9=n' WINEDEBUG=-all
export DXVK_CONFIG='d3d9.shaderModel = 1' DXVK_LOG_PATH=$run
if [[ "$desktop_input" == true ]]; then
    export SteamAppId=1234567890 SteamGameId=1234567890
fi
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
if [[ "$desktop_input" == true ]]; then
    ln -s "$run/evidence/input-region.json" "$run/plugin-input.json"
fi
jq -n --arg net "$(readlink /proc/self/ns/net)" --arg pid "$(readlink /proc/self/ns/pid)" \
    '{networkNamespace:$net,pidNamespace:$pid,sourcePrefixHidden:true,hostDesktopVariablesAbsent:true}' >"$run/isolation.json"
cd "$run/app"
cp HearthstoneDeckTracker.exe.config ReplayHost.exe.config
winpath() { printf 'Z:%s' "${1//\//\\}"; }
wpf='C:\windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
if [[ "$plugin" == true ]]; then
    plugin_sources=()
    for source in "$snapshot"/src/plugin/*.cs; do plugin_sources+=("$(winpath "$source")"); done
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:library /platform:x64 /out:ProtonCompatibility.dll /r:HearthstoneDeckTracker.exe \
        /r:HearthMirror.dll /r:MahApps.Metro.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll \
        /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
        "${plugin_sources[@]}" >"$run/plugin-compile.log" 2>&1
    export HDT_ALPHA_FLOOR=1 HDT_INPUT_SESSION=isolated-replay HDT_INPUT_REGION_FILE=$run/plugin-input.json
    if [[ "$desktop_input" == true ]]; then
        export HDT_HOVER_DIAGNOSTICS_FILE=$run/hover-state.json HDT_DESKTOP_BACKEND=x11 HDT_INPUT_REGION_FILE=$run/evidence/input-region.json
    fi
fi
if [[ "$diagnostics" == true ]]; then
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:exe /platform:x64 /out:FlightRecorderChecks.exe /r:System.Web.Extensions.dll \
        "$(winpath "$snapshot/tests/FlightRecorderChecks.cs")" "$(winpath "$snapshot/src/plugin/FlightRecorder.cs")" >"$run/recorder-compile.log" 2>&1
    timeout --kill-after=3s 10 "$proton/files/bin/wine" "$run/app/FlightRecorderChecks.exe" "$(winpath "$run/recorder-checks")" >"$run/recorder-checks.log" 2>&1
fi
timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
    /nologo /target:winexe /platform:x64 /out:ReplayHost.exe /r:System.Xaml.dll /r:System.Web.Extensions.dll \
    /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
    "$(winpath "$snapshot/src/replay/ReplayHost.cs")" >"$run/compile.log" 2>&1
if [[ "$desktop_input" == true ]]; then
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:winexe /platform:x64 /out:Hearthstone.exe "$(winpath "$snapshot/src/replay/GameWindow.cs")" >"$run/game-compile.log" 2>&1
    timeout 70 "$proton/files/bin/wine" "$run/app/Hearthstone.exe" "$(winpath "$run/game-fixture.json")" >"$run/game-fixture.log" 2>&1 &
fi
timeout 70 "$proton/files/bin/wine" "$run/app/ReplayHost.exe" "$(winpath "$run/host.log")" "$plugin" "$level" "$particles" >"$run/wine.log" 2>&1 &
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
if [[ "$diagnostics" == true ]]; then
    # Give the independent writer time to post a ping before blocking WPF.
    sleep 1.2
    send diagnostic-ui-stall
    sleep 1.5
    recorder_root=$(dirname "$HDT_INPUT_REGION_FILE")
    recorder_files=("$recorder_root"/flight-recorder-*/events.jsonl)
    [[ ${#recorder_files[@]} == 1 && -f ${recorder_files[0]} ]]
    jq -se 'any(.[]; .name=="ui.stall") and any(.[]; .name=="ui.recovered") and any(.[]; .name=="ui.dispatch" and .data.durationMS>=2000) and any(.[]; .name=="render.sample" and .data.callbacks>0) and any(.[]; .name=="build.identity")' "${recorder_files[0]}" >/dev/null
    [[ -s $(dirname "${recorder_files[0]}")/first-stall-events.jsonl ]]
fi
if [[ "$battlegrounds_tabs" == true ]]; then
    send bgs-start
    for ((toggle=0;toggle<6;toggle++)); do
        send bgs-minions-toggle
        sleep 0.5
        cp "$run/state.json" "$run/bgs-tab-$toggle.json"
        if (( toggle % 2 == 0 )); then
            jq -e '.battlegroundsActiveTab=="BattlegroundsMinionsViewModel" and .battlegroundsContent.visible and .battlegroundsContent.height>0 and .battlegroundsContent.minionViews==1 and .battlegroundsContent.groupViews>0' "$run/state.json" >/dev/null
        else
            jq -e '.battlegroundsActiveTab==null' "$run/state.json" >/dev/null
        fi
        magick import -window root "$run/bgs-tab-$toggle.png"
        settings
    done
    send bgs-end
fi
if [[ "$desktop_input" == true ]]; then
    send desktop-control
    export HS_APP_ID=1234567890 HDT_PROJECT_DIR=$run HDT_NATIVE_DIR=$run/tools HDT_DESKTOP_BACKEND=x11
    bun "$snapshot/tests/wpf-desktop-input.ts" >"$run/desktop-input.log" 2>&1
    send desktop-control-hide
fi
for phase in arena-lobby draft-1 draft-2 draft-1 draft-2 draft-1 draft-2 draft-1 draft-2 draft-1 draft-2 draft-1 draft-2 menu; do
    send "$phase"
    sleep 1
    cp "$run/state.json" "$run/$phase.json"
    jq -c '{observedUTC,commandId,scene,cards,plaqueLevels,visibleScores}' "$run/state.json" >>"$run/transitions.jsonl"
    magick import -window root "$run/$phase.png"
    settings
done
jq -se '[.[] | select(.scene | startswith("draft-"))] | length==12' "$run/transitions.jsonl" >/dev/null
levels='[1,3,5]' emitters=1
if [[ "$level" != mixed ]]; then
    levels="[$level,$level,$level]"; emitters=0
    [[ "$level" != 5 ]] || emitters=3
fi
[[ "$particles" == true ]] || emitters=0
[[ "$plugin" == false ]] || emitters=0
jq -e --argjson levels "$levels" --argjson emitters "$emitters" '.cards==["CS2_029","CS2_032","CS2_172"] and .visibleScores==["60","61","62"] and .plaqueLevels==$levels and .visibleEmitters==$emitters' "$run/draft-1.json" >/dev/null
jq -e --argjson levels "$levels" --argjson emitters "$emitters" '.cards==["CS2_024","CS2_025","CS2_182"] and .visibleScores==["80","81","82"] and .plaqueLevels==$levels and .visibleEmitters==$emitters' "$run/draft-2.json" >/dev/null
jq -e '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]' "$run/menu.json" >/dev/null
if [[ "$plugin" == true ]]; then
    jq -e '.pluginUpdates>0' "$run/draft-2.json" >/dev/null
    jq -e '.state.arenaClientState=="None"' "$run/plugin-input.json" >/dev/null
    if [[ "$particles" == true && ( "$level" == mixed || "$level" == 5 ) ]]; then
        jq -e '.state.arenaParticleEmittersSuppressed>=1' "$run/plugin-input.json" >/dev/null
    fi
fi
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
if [[ "$diagnostics" == true ]]; then
    if [[ "$battlegrounds_tabs" == true ]]; then
        jq -se '([.[]|select(.name=="battlegrounds.tab.changed")]|length)==6 and ([.[]|select(.name=="battlegrounds.minions.pointer.down")]|length)==6' "${recorder_files[0]}" >/dev/null
    fi
    bun "$snapshot/src/diagnostics/trace-export.ts" "$run/trace.json" "${recorder_files[0]}"
fi
if rg -q '^(dispatcher|command|startup) ' "$run/host.log"; then
    echo 'The fixture host recorded an exception.' >&2
    exit 1
fi
echo 'Verified fixture transitions, rendered score bindings, native Settings input, popup, resize, New dialog, and host shutdown.'
