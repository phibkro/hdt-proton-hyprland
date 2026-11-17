#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "Replay failed at line $LINENO" >&2' ERR
root=$1 run=$2 proton=$3 negative=$4 plugin=$5 level=$6 particles=$7
snapshot=$run/source
battlegrounds_tabs=${8:-false}
diagnostics=${9:-false}
canonical_events=${10:-false}
desktop_input=${11:-false}
reference_views=${12:-false}
libxi_library=${13:-}
wine_environment=()
if [[ -n "$libxi_library" ]]; then
    libxi_library=$("$snapshot/hdt-host" libxi-check "$libxi_library")
    wine_environment=(env "LD_PRELOAD=$libxi_library")
fi
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
    export SteamAppId=1234567890 SteamGameId=1234567890 HDT_DESKTOP_BACKEND=x11
    mkdir -p "$run/evidence"
fi
Xvfb -displayfd 3 -screen 0 2560x1440x24 -nolisten tcp -ac 3>"$run/display" >"$run/xvfb.log" 2>&1 &
xvfb_pid=$!
cleanup() {
    local status=$?
    trap - EXIT
    if ((status)); then timeout 3 magick import -window root "$run/failure.png" 2>/dev/null || true; fi
    if [[ -n ${journal_pid:-} ]]; then
        kill -TERM "$journal_pid" 2>/dev/null || true
        wait "$journal_pid" 2>/dev/null || true
    fi
    kill "$xvfb_pid" 2>/dev/null || true
    exit "$status"
}
trap cleanup EXIT
for ((i = 0; i < 50; i++)); do
    [[ -s "$run/display" ]] && break
    sleep 0.1
done
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
if [[ "$plugin" == true ]]; then
    plugin_sources=()
    for source in "$snapshot"/src/plugin/*.cs; do plugin_sources+=("$(winpath "$source")"); done
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:library /platform:x64 /out:ProtonCompatibility.dll /r:HearthstoneDeckTracker.exe \
        /r:HearthMirror.dll /r:HearthWatcher.dll /r:MahApps.Metro.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll \
        /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
        "${plugin_sources[@]}" >"$run/plugin-compile.log" 2>&1
    export HDT_ALPHA_FLOOR=1 HDT_INPUT_SESSION=isolated-replay HDT_INPUT_REGION_FILE=$run/plugin-input.json
    if [[ "$desktop_input" == true ]]; then export HDT_INPUT_REGION_FILE=$run/evidence/input-region.json; fi
fi
if [[ "$diagnostics" == true ]]; then
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:exe /platform:x64 /out:FlightRecorderChecks.exe /r:System.Web.Extensions.dll \
        "$(winpath "$snapshot/tests/replay/FlightRecorderChecks.cs")" "$(winpath "$snapshot/src/plugin/FlightRecorder.cs")" >"$run/recorder-compile.log" 2>&1
    timeout --kill-after=3s 10 "$proton/files/bin/wine" "$run/app/FlightRecorderChecks.exe" "$(winpath "$run/recorder-checks")" >"$run/recorder-checks.log" 2>&1
fi
timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
    /nologo /target:winexe /platform:x64 /out:ReplayHost.exe /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:Newtonsoft.Json.dll \
    /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
    "$(winpath "$snapshot/src/replay/ReplayHost.cs")" "$(winpath "$snapshot/src/replay/CanonicalReplay.cs")" >"$run/compile.log" 2>&1
if [[ "$desktop_input" == true ]]; then
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:winexe /platform:x64 /out:Hearthstone.exe "$(winpath "$snapshot/src/replay/GameWindow.cs")" >"$run/game-compile.log" 2>&1
    timeout 70 "$proton/files/bin/wine" "$run/app/Hearthstone.exe" "$(winpath "$run/game-fixture.json")" >"$run/game-fixture.log" 2>&1 &
fi
timeout 70 "${wine_environment[@]}" "$proton/files/bin/wine" "$run/app/ReplayHost.exe" "$(winpath "$run/host.log")" "$plugin" "$level" "$particles" >"$run/wine.log" 2>&1 &
host_pid=$!
for ((i = 0; i < 200; i++)); do
    [[ -s "$run/state.json" ]] && break
    kill -0 "$host_pid" 2>/dev/null || break
    sleep 0.1
done
xwininfo -root -tree >"$run/windows.txt"
magick import -window root "$run/startup.png"
[[ -s "$run/state.json" ]]
jq -e '.settingsButton!=null and .commandId==0' "$run/state.json" >/dev/null
if [[ -n "$libxi_library" ]]; then
    tracker_pids=()
    for comm in /proc/[0-9]*/comm; do
        if [[ $(cat "$comm" 2>/dev/null || true) == ReplayHost.exe ]]; then
            tracker_pids+=("${comm%/comm}")
        fi
    done
    ((${#tracker_pids[@]} == 1))
    cat "${tracker_pids[0]}/maps" >"$run/tracker-native-maps.txt"
    grep -F "$libxi_library" "$run/tracker-native-maps.txt" >"$run/selected-libxi-maps.txt"
    sha256sum "$libxi_library" >"$run/selected-libxi-sha256.txt"
fi
id=0
send() {
    id=$((id + 1))
    jq -n --arg op "$1" --argjson id "$id" --arg page "${2:-}" --argjson offset "${3:-0}" '{id:$id,op:$op,page:$page,offset:$offset}' >"$run/command.json.next"
    mv "$run/command.json.next" "$run/command.json"
    for ((i = 0; i < 50; i++)); do
        [[ -s "$run/state.json" ]] && jq -e --argjson id "$id" '.commandId==$id' "$run/state.json" >/dev/null && return
        sleep 0.1
    done
    echo "HDT did not acknowledge replay command $id" >&2
    return 1
}
settings() {
    read -r x y < <(jq -r '[.settingsButton.x,.settingsButton.y]|@tsv' "$run/state.json")
    xdotool mousemove --sync "$x" "$y" click 1
    for ((i = 0; i < 30; i++)); do
        jq -e '.settingsOpen' "$run/state.json" >/dev/null && break
        sleep 0.1
    done
    jq -e '.settingsOpen' "$run/state.json" >/dev/null
    cp "$run/state.json" "$run/settings-$id.json"
    send settings-close
}
capture_main_crop() {
    local x y width height
    read -r x y width height < <(xdotool getwindowgeometry --shell "$1" | awk -F= '/^X=/{x=$2} /^Y=/{y=$2} /^WIDTH=/{w=$2} /^HEIGHT=/{h=$2} END{print x,y,w,h}')
    if [[ "$2" == popup ]]; then
        x=$((x + width / 2 - 160))
        y=$((y + height / 2 - 90))
    else
        x=$((x + 40))
        y=$((y + 120))
    fi
    magick import -window root -crop "320x180+$x+$y" +repage "$3"
}
if [[ "$canonical_events" == true ]]; then
    # The native journal is independently owned by this supervisor. Its sources
    # are the synthetic fixture and actual HDT publishers, not interpreted markers.
    touch "$run/canonical-input.json"
    "$snapshot/hdt-host" observe --output "$run/journal" --from start --max-bytes 8388608 \
        --source "input=$run/canonical-input.json" --source "hdt=$run/canonical.jsonl" >"$run/journal.log" 2>&1 &
    journal_pid=$!
    send canonical-start
    jq -n '["DRAFT","HUB","BACON","HUB"] | to_entries | map({ns:"LoadingScreen",line:("D 00:00:00.0000000 LoadingScreen.OnSceneLoaded() - prevMode=" + (if .key==0 then "HUB" else ["DRAFT","HUB","BACON"][.key-1] end) + " currMode=" + .value)})' >"$run/canonical-input.json"
    send canonical-input
    jq -se '[.[]|select(.kind=="hdt.event" and .data.name=="OnModeChanged")|.data.value]==["DRAFT","HUB","BACON","HUB"]' "$run/canonical.jsonl" >/dev/null
    cp "$run/canonical.jsonl" "$run/canonical-first.jsonl"
    send canonical-input
    jq -se '([.[]|select(.kind=="hdt.event" and .data.name=="OnModeChanged")|.data.value]) as $events | ($events|length)==8 and $events[0:4]==$events[4:8] and ([.[]|select(.kind=="parser.return")]|length)==8 and all(.[];.kind!="parser.error" and .kind!="hdt.event.capture-failed")' "$run/canonical.jsonl" >/dev/null
    # Freeze the actual application process in this isolated PID namespace.
    replay_pid=$(pgrep -P "$host_pid")
    [[ "$replay_pid" =~ ^[0-9]+$ ]]
    tr '\0' '\n' <"/proc/$replay_pid/cmdline" | rg -q 'ReplayHost[.]exe'
    kill -STOP "$replay_pid"
    for ((i = 0; i < 30; i++)); do
        [[ $(sed -n 's/^.*) \([A-Za-z]\) .*$/\1/p' "/proc/$replay_pid/stat") == T ]] && break
        sleep 0.01
    done
    [[ $(sed -n 's/^.*) \([A-Za-z]\) .*$/\1/p' "/proc/$replay_pid/stat") == T ]]
    before=$(wc -l <"$run/journal/journal.jsonl")
    printf '\n' >>"$run/canonical-input.json"
    sleep 1
    after=$(wc -l <"$run/journal/journal.jsonl")
    bun -e 'const fs=require("node:fs"),p=process.argv[1]; const rows=fs.readFileSync(p+"/journal/journal.jsonl","utf8").trim().split("\n").map(JSON.parse); const bytes=Buffer.concat(rows.filter(r=>r.kind==="source.bytes" && r.data.source==="input").map(r=>Buffer.from(r.data.base64,"base64"))); if(!bytes.equals(fs.readFileSync(p+"/canonical-input.json"))) throw Error("Input capture stalled with the HDT host");' "$run"
    kill -CONT "$replay_pid"
    ((after > before))
    kill -KILL "$replay_pid"
    if wait "$host_pid"; then
        echo 'The deliberately killed replay host unexpectedly succeeded.' >&2
        exit 1
    else
        first_status=$?
        [[ "$first_status" != 124 && "$first_status" != 0 ]]
        printf '%s\n' "$first_status" >"$run/canonical-killed-status.txt"
    fi
    [[ ! -d /proc/$replay_pid ]]
    # A new application process must not own or restart the native journal.
    mv "$run/canonical.jsonl" "$run/canonical-session1.jsonl"
    rm "$run/command.json" "$run/state.json"
    id=0
    timeout 30 "${wine_environment[@]}" "$proton/files/bin/wine" "$run/app/ReplayHost.exe" "$(winpath "$run/host-second.log")" "$plugin" "$level" "$particles" >"$run/wine-second.log" 2>&1 &
    host_pid=$!
    for ((i = 0; i < 100; i++)); do
        [[ -s "$run/state.json" ]] && break
        sleep 0.1
    done
    [[ -s "$run/state.json" ]]
    send canonical-start
    send canonical-input
    jq -se '[.[]|select(.kind=="hdt.event" and .data.name=="OnModeChanged")|.data.value]==["DRAFT","HUB","BACON","HUB"] and all(.[];.kind!="parser.error" and .kind!="hdt.event.capture-failed")' "$run/canonical.jsonl" >/dev/null
    send finish
    wait "$host_pid"
    sleep 0.5
    kill -TERM "$journal_pid"
    wait "$journal_pid"
    journal_pid=''
    jq -se 'any(.[];.kind=="source.bytes" and .data.source=="input") and any(.[];.kind=="source.bytes" and .data.source=="hdt" and .data.generation==1) and any(.[];.kind=="source.replaced") and .[-1].kind=="journal.stop" and .[-1].data.reason=="signal"' "$run/journal/journal.jsonl" >/dev/null
    bun -e 'const fs=require("node:fs"),p=process.argv[1]; const rows=fs.readFileSync(p+"/journal/journal.jsonl","utf8").trim().split("\n").map(JSON.parse); for(const [generation,file] of [[0,"canonical-session1.jsonl"],[1,"canonical.jsonl"]]) {const captured=Buffer.concat(rows.filter(r=>r.kind==="source.bytes" && r.data.source==="hdt" && r.data.generation===generation).map(r=>Buffer.from(r.data.base64,"base64"))); if(!captured.equals(fs.readFileSync(p+"/"+file))) throw Error("Canonical publisher bytes differ from independent journal");}' "$run"
    echo 'Canonical installed-parser mode transitions and independent journal survival verified in isolation.'
    exit 0
fi
settings
if [[ "$reference_views" == true ]]; then
    mkdir "$run/reference"
    mapfile -t mains < <(xdotool search --onlyvisible --name '^Hearthstone Deck Tracker$')
    [[ ${#mains[@]} == 1 ]]
    main_window=${mains[0]}
    xdotool windowsize "$main_window" 1280 900
    send reference-options-index
    mapfile -t pages < <(jq -er '.[].name' "$run/reference-options-index.json")
    ((${#pages[@]} > 0))
    for page in "${pages[@]}"; do
        [[ "$page" =~ ^TreeViewItem[A-Za-z0-9]+$ ]]
        send reference-options-page "$page"
        for ((attempt = 0; attempt < 30; attempt++)); do
            jq -e --arg page "$page" '.settingsOpen and .settingsReference.page==$page and .settingsReference.visible and .settingsReference.width>0 and .settingsReference.viewportHeight>0' "$run/state.json" >/dev/null && break
            sleep 0.1
        done
        jq -e --arg page "$page" '.settingsOpen and .settingsReference.page==$page and .settingsReference.visible and .settingsReference.viewportHeight>0' "$run/state.json" >/dev/null
        offset=0 tile=0
        while :; do
            sleep 0.15
            stem=$page-$tile
            cp "$run/state.json" "$run/reference/$stem.json"
            magick import -window "$main_window" "$run/reference/$stem.png"
            jq -c --arg image "$stem.png" '{kind:"settings",image:$image,state:.settingsReference}' "$run/reference/$stem.json" >>"$run/reference/index.jsonl"
            read -r extent viewport < <(jq -r '[.settingsReference.scrollableHeight,.settingsReference.viewportHeight]|map(floor)|@tsv' "$run/state.json")
            ((offset < extent)) || break
            ((tile < 31)) || {
                echo 'Original settings page exceeds 32 reference tiles; capture incomplete.' >&2
                exit 1
            }
            step=$((viewport * 4 / 5))
            ((step > 0))
            offset=$((offset + step))
            ((offset <= extent)) || offset=$extent
            send reference-options-scroll "$page" "$offset"
            for ((attempt = 0; attempt < 30; attempt++)); do
                jq -e --argjson offset "$offset" '.settingsReference.offset >= ($offset - 1)' "$run/state.json" >/dev/null && break
                sleep 0.1
            done
            jq -e --argjson offset "$offset" '.settingsReference.offset >= ($offset - 1)' "$run/state.json" >/dev/null
            tile=$((tile + 1))
        done
    done
    send settings-close
    for phase in menu arena-lobby draft-1 draft-2 bgs-start bgs-minions-toggle bgs-end; do
        if [[ "$phase" == bgs-start ]]; then
            send menu
            for ((attempt = 0; attempt < 30; attempt++)); do
                jq -e '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]' "$run/state.json" >/dev/null && break
                sleep 0.1
            done
            jq -e '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]' "$run/state.json" >/dev/null
        fi
        send "$phase"
        sleep 0.4
        cp "$run/state.json" "$run/reference/$phase.json"
        magick import -window root "$run/reference/$phase.png"
        jq -c --arg image "$phase.png" '{kind:"synthetic-scene",image:$image,scene,cards,arenaVisibility,battlegroundsActiveTab}' "$run/state.json" >>"$run/reference/index.jsonl"
    done
    send new
    sleep 0.5
    jq -e '.visibleDialogTypes|any(endswith("DeckTypeDialog"))' "$run/state.json" >/dev/null
    magick import -window "$main_window" "$run/reference/new-deck-dialog.png"
    jq -c '{kind:"dialog",image:"new-deck-dialog.png",types:.visibleDialogTypes}' "$run/state.json" >>"$run/reference/index.jsonl"
    jq -s '.' "$run/reference/index.jsonl" >"$run/reference/index.json"
    jq -r '
        "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Original HDT reference atlas</title>" +
        "<style>body{font:16px system-ui;background:#151b21;color:#eef3f7;max-width:1320px;margin:auto;padding:24px}a{color:#71caff}details{background:#20262d;margin:12px 0;padding:14px}summary{cursor:pointer}img{display:block;max-width:100%;height:auto;margin-top:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style>" +
        "<h1>Original HDT reference atlas</h1><p>Private original-WPF captures; synthetic game models; no account session. Not every HDT state.</p>" +
        "<p>" + (length|tostring) + " images; " + ([.[]|select(.kind=="settings")|.state.page]|unique|length|tostring) +
        " settings pages. <a href=\"index.json\">View manifest</a> · <a href=\"../result.json\">Run identity and outcome</a></p>" +
        (map("<details><summary>" + ((.state.title // .scene // .kind)|@html) + " — " + (.image|@html) +
            "</summary><a href=\"" + (.image|@html) + "\"><img loading=\"lazy\" src=\"" + (.image|@html) +
            "\" alt=\"Original HDT reference\"></a><pre>" + (tojson|@html) + "</pre></details>")|join("\n")) + "</html>"
    ' "$run/reference/index.json" >"$run/reference/index.html"
    send finish
    wait "$host_pid"
    if rg -q '^(dispatcher|command|startup) ' "$run/host.log"; then
        echo 'Reference capture recorded an original WPF exception; atlas incomplete.' >&2
        exit 1
    fi
    echo "Captured ${#pages[@]} original settings pages, scroll tiles, synthetic scenes and New dialog. This is not every HDT state."
    exit 0
fi
if [[ "$desktop_input" == true ]]; then
    send desktop-control
    bash "$snapshot/tests/replay/portable-wpf.sh" "$root" "$run" >"$run/desktop-input.log" 2>&1
    send desktop-control-hide
fi
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
    for ((toggle = 0; toggle < 6; toggle++)); do
        send bgs-minions-toggle
        sleep 0.5
        cp "$run/state.json" "$run/bgs-tab-$toggle.json"
        if ((toggle % 2 == 0)); then
            jq -e '.battlegroundsActiveTab=="BattlegroundsMinionsViewModel" and .battlegroundsContent.visible and .battlegroundsContent.height>0 and .battlegroundsContent.minionViews==1 and .battlegroundsContent.groupViews>0' "$run/state.json" >/dev/null
        else
            jq -e '.battlegroundsActiveTab==null' "$run/state.json" >/dev/null
        fi
        magick import -window root "$run/bgs-tab-$toggle.png"
        settings
    done
    send bgs-end
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
    levels="[$level,$level,$level]"
    emitters=0
    [[ "$level" != 5 ]] || emitters=3
fi
[[ "$particles" == true ]] || emitters=0
[[ "$plugin" == false ]] || emitters=0
jq -e --argjson levels "$levels" --argjson emitters "$emitters" '.cards==["CS2_029","CS2_032","CS2_172"] and .visibleScores==["60","61","62"] and .plaqueLevels==$levels and .visibleEmitters==$emitters' "$run/draft-1.json" >/dev/null
jq -e --argjson levels "$levels" --argjson emitters "$emitters" '.cards==["CS2_024","CS2_025","CS2_182"] and .visibleScores==["80","81","82"] and .plaqueLevels==$levels and .visibleEmitters==$emitters' "$run/draft-2.json" >/dev/null
jq -e '.cards==[] and .arenaVisibility=="Collapsed" and .visibleScores==[]' "$run/menu.json" >/dev/null
if [[ "$plugin" == true ]]; then
    jq -e '.pluginUpdates>0' "$run/draft-2.json" >/dev/null
    jq -e '.state.arenaClientState=="None"' "$HDT_INPUT_REGION_FILE" >/dev/null
    if [[ "$particles" == true && ("$level" == mixed || "$level" == 5) ]]; then
        jq -e '.state.arenaParticleEmittersSuppressed>=1' "$HDT_INPUT_REGION_FILE" >/dev/null
    fi
fi
mapfile -t mains < <(xdotool search --onlyvisible --name '^Hearthstone Deck Tracker$')
[[ ${#mains[@]} == 1 ]]
xdotool windowsize "${mains[0]}" 1280 720
sleep 0.5
jq -e '.mainWidth>=1250 and .mainHeight>=700' "$run/state.json" >/dev/null
settings
# Native Settings input plus actual screen-pixel appearance and clearing.
# State acknowledgements alone cannot detect a white or stale rendered surface.
sleep 0.7
capture_main_crop "${mains[0]}" settings "$run/settings-before.png"
read -r x y < <(jq -r '[.settingsButton.x,.settingsButton.y]|@tsv' "$run/state.json")
xdotool mousemove --sync "$x" "$y" click 1
sleep 0.7
jq -e '.settingsOpen' "$run/state.json" >/dev/null
capture_main_crop "${mains[0]}" settings "$run/settings-shown.png"
send settings-close
sleep 0.7
capture_main_crop "${mains[0]}" settings "$run/settings-after.png"
bash "$snapshot/tests/replay/visual-triplet.sh" "$run/settings-before.png" "$run/settings-shown.png" "$run/settings-after.png" "$run/settings-visual"
capture_main_crop "${mains[0]}" popup "$run/popup-before.png"
send popup-show
sleep 0.7
jq -e '.popupOpen' "$run/state.json" >/dev/null
magick import -window root "$run/popup.png"
capture_main_crop "${mains[0]}" popup "$run/popup-shown.png"
send popup-hide
jq -e '.popupOpen==false' "$run/state.json" >/dev/null
sleep 0.7
capture_main_crop "${mains[0]}" popup "$run/popup-after.png"
bash "$snapshot/tests/replay/visual-triplet.sh" "$run/popup-before.png" "$run/popup-shown.png" "$run/popup-after.png" "$run/popup-visual"
send preview-card
xdotool windowactivate --sync "${mains[0]}"
read -r cx cy < <(jq -er '[.previewTarget.x,.previewTarget.y]|@tsv' "$run/state.json")
xdotool mousemove --sync "$((cx - 80))" "$((cy - 100))"
sleep 0.7
jq -e '.cardPreviews==[] and .previewTarget.cardId=="CS2_029"' "$run/state.json" >/dev/null
magick import -window root "$run/card-preview-before-root.png"
xdotool mousemove --sync "$cx" "$cy"
for ((attempt = 0; attempt < 40; attempt++)); do
    jq -e '.cardPreviews|length==1' "$run/state.json" >/dev/null && break
    sleep 0.1
done
cp "$run/state.json" "$run/card-preview-shown.json"
jq -e '.cardPreviews|length==1 and .[0].cardId=="CS2_029" and .[0].width>0 and .[0].height>0 and .[0].x>=0 and .[0].y>=0 and (.[0].x+.[0].width)<=2560 and (.[0].y+.[0].height)<=1440' "$run/card-preview-shown.json" >/dev/null
sleep 0.7
magick import -window root "$run/card-preview-shown-root.png"
xdotool mousemove --sync "$((cx - 80))" "$((cy - 100))"
for ((attempt = 0; attempt < 40; attempt++)); do
    jq -e '.cardPreviews==[]' "$run/state.json" >/dev/null && break
    sleep 0.1
done
jq -e '.cardPreviews==[]' "$run/state.json" >/dev/null
cp "$run/state.json" "$run/card-preview-after.json"
sleep 0.7
magick import -window root "$run/card-preview-after-root.png"
geometry=$(jq -er '.cardPreviews[0]|"\(.width)x\(.height)+\(.x)+\(.y)"' "$run/card-preview-shown.json")
for phase in before shown after; do
    magick "$run/card-preview-$phase-root.png" -crop "$geometry" +repage "$run/card-preview-$phase.png"
done
bash "$snapshot/tests/replay/visual-triplet.sh" "$run/card-preview-before.png" "$run/card-preview-shown.png" "$run/card-preview-after.png" "$run/card-preview-visual"
# The real shown frame retained as "after" must fail the same visual assertion.
negative_status=0
bash "$snapshot/tests/replay/visual-triplet.sh" "$run/card-preview-before.png" "$run/card-preview-shown.png" "$run/card-preview-shown.png" "$run/card-preview-retained-negative" || negative_status=$?
[[ $negative_status == 1 ]]
jq -e '.passed==false' "$run/card-preview-retained-negative/result.json" >/dev/null
send preview-card-remove
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
    "$snapshot/hdt-host" trace-export "$run/trace.json" "${recorder_files[0]}"
fi
if rg -q '^(dispatcher|command|startup) ' "$run/host.log"; then
    echo 'The fixture host recorded an exception.' >&2
    exit 1
fi
echo 'Verified fixture transitions, rendered score bindings, native Settings input, popup, resize, New dialog, and host shutdown.'
