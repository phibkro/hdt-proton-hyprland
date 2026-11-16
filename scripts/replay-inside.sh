#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "Replay failed at line $LINENO" >&2' ERR
root=$1 run=$2 proton=$3 negative=$4 plugin=$5 level=$6 particles=$7
snapshot=$run/source
battlegrounds_tabs=${8:-false}
diagnostics=${9:-false}
canonical_events=${10:-false}
managed_snapshot=${11:-false}
managed_debug_lease=${12:-false}
settings_cycles=${13:-0}
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
Xvfb -displayfd 3 -screen 0 2560x1440x24 -nolisten tcp -ac 3>"$run/display" >"$run/xvfb.log" 2>&1 &
xvfb_pid=$!
cleanup() {
    local status=$?
    trap - EXIT
    if (( status )); then timeout 3 magick import -window root "$run/failure.png" 2>/dev/null || true; fi
    if [[ -n ${journal_pid:-} ]]; then kill -TERM "$journal_pid" 2>/dev/null || true; wait "$journal_pid" 2>/dev/null || true; fi
    kill "$xvfb_pid" 2>/dev/null || true
    exit "$status"
}
trap cleanup EXIT
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
if [[ "$managed_debug_lease" == true || "$settings_cycles" != 0 ]]; then
    # Match the live Proton session and explicitly initialize this PRIVATE
    # prefix before a compiler timeout can interrupt a version migration.
    export WINEFSYNC=1
    timeout --kill-after=3s 45 "$proton/files/bin/wine" wineboot.exe --init >"$run/wineboot.log" 2>&1
fi
if [[ "$plugin" == true ]]; then
    plugin_sources=()
    for source in "$snapshot"/src/plugin/*.cs; do plugin_sources+=("$(winpath "$source")"); done
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:library /platform:x64 /out:ProtonCompatibility.dll /r:HearthstoneDeckTracker.exe \
        /r:HearthMirror.dll /r:MahApps.Metro.dll /r:System.Drawing.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll \
        /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
        "${plugin_sources[@]}" >"$run/plugin-compile.log" 2>&1
    export HDT_ALPHA_FLOOR=1 HDT_INPUT_SESSION=isolated-replay HDT_INPUT_REGION_FILE=$run/plugin-input.json
fi
if [[ "$diagnostics" == true ]]; then
    timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
        /nologo /target:exe /platform:x64 /out:FlightRecorderChecks.exe /r:System.Web.Extensions.dll \
        "$(winpath "$snapshot/tests/FlightRecorderChecks.cs")" "$(winpath "$snapshot/src/plugin/FlightRecorder.cs")" >"$run/recorder-compile.log" 2>&1
    timeout --kill-after=3s 10 "$proton/files/bin/wine" "$run/app/FlightRecorderChecks.exe" "$(winpath "$run/recorder-checks")" >"$run/recorder-checks.log" 2>&1
fi
host_sources=()
for source in "$snapshot"/src/replay/*.cs; do host_sources+=("$(winpath "$source")"); done
timeout --kill-after=3s 35 "$proton/files/bin/wine" 'C:\windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' \
    /nologo /target:winexe /platform:x64 /out:ReplayHost.exe /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:Newtonsoft.Json.dll \
    /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" \
    "${host_sources[@]}" >"$run/compile.log" 2>&1
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
if [[ "$settings_cycles" != 0 ]]; then
    send menu
    for ((cycle=0;cycle<settings_cycles;cycle++)); do
        for expected in true false; do
            field=settingsButton
            [[ "$expected" == true ]] || field=settingsBackButton
            read -r x y < <(jq -er --arg field "$field" '.[$field]|[.x,.y]|@tsv' "$run/state.json")
            [[ "$x" =~ ^[0-9]+$ && "$y" =~ ^[0-9]+$ ]]
            began=$(date +%s%N)
            xdotool mousemove --sync "$x" "$y" click 1
            acknowledged=false
            for ((poll=0;poll<50;poll++)); do
                if jq -e --argjson expected "$expected" '.settingsOpen==$expected' "$run/state.json" >/dev/null; then acknowledged=true;break; fi
                sleep 0.1
            done
            ended=$(date +%s%N)
            jq -n --argjson cycle "$cycle" --argjson opened "$expected" --argjson acknowledged "$acknowledged" --argjson elapsed "$(((ended-began)/1000000))" '{cycle:$cycle,open:$opened,acknowledged:$acknowledged,acknowledgmentMS:$elapsed}' >>"$run/settings-cycles.jsonl"
            [[ "$acknowledged" == true ]]
            # Let the real flyout animation run before its next native click.
            sleep 1
        done
    done
    send finish
    wait "$host_pid"
    jq -se --argjson cycles "$settings_cycles" 'length==$cycles*2 and all(.[];.acknowledged)' "$run/settings-cycles.jsonl" >/dev/null
    echo 'Native Settings open/close cycles acknowledged; inspect timings and recorder for delays.'
    exit 0
fi
if [[ "$managed_debug_lease" == true ]]; then
    target_pid=$(jq -er .windowsProcessId "$run/state.json")
    target_hash=$(sha256sum "$run/app/ReplayHost.exe");target_hash=${target_hash%% *}
    dac="$run/prefix/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/mscordacwks.dll"
    dac_hash=$(sha256sum "$dac");dac_hash=${dac_hash%% *}
    inspector="$run/inspector/HdtManagedInspector.exe"
    # Identity and dependency failures must reject before any debugger attach.
    for invalid in identity dac kind; do
        selected_hash=$target_hash selected_dac=$dac expected_error=System.IO.InvalidDataException selected_mode=debug-snapshot
        if [[ "$invalid" == identity ]]; then selected_hash=$(printf '%064d' 0); else selected_dac=$run/missing-dac.dll;expected_error=System.IO.FileNotFoundException; fi
        if [[ "$invalid" == kind ]]; then selected_mode=hdt-debug-snapshot;selected_dac=$dac;expected_error=System.ArgumentException; fi
        if timeout --kill-after=3s 10 "$proton/files/bin/wine" "$inspector" "$selected_mode" "$target_pid" "$selected_hash" \
            "$(winpath "$selected_dac")" "$dac_hash" "$(winpath "$run/rejected-$invalid.json")" 1000 >"$run/rejected-$invalid.log" 2>&1; then
            echo "Invalid $invalid was accepted." >&2;exit 1
        fi
        jq -e --arg error "$expected_error" '.status=="failed" and .errorType==$error' "$run/rejected-$invalid.json" >/dev/null
        [[ ! -e "$run/rejected-$invalid.json.lease-held.json" ]]
    done
    bun "$snapshot/src/diagnostics/input-journal.ts" --output "$run/lease-journal" --from start --max-bytes 8388608 \
        --source "state=$run/state.json" --source "owner=$run/managed-owner-heartbeat.txt" >"$run/journal.log" 2>&1 &
    journal_pid=$!
    send managed-lock-stall
    capture_status=0
    timeout --kill-after=3s 20 "$proton/files/bin/wine" "$inspector" debug-snapshot "$target_pid" "$target_hash" \
        "$(winpath "$dac")" "$dac_hash" "$(winpath "$run/lease-result.json")" 10000 >"$run/lease.log" 2>&1 || capture_status=$?
    [[ "$capture_status" == 0 || "$capture_status" == 3 ]]
    jq -e --argjson code "$capture_status" '.killOnClose==false and .status==.reader.status and .status==(if $code==0 then "captured" else "partial" end) and any(.reader.threads[]; .status=="captured" and any(.frames[]?;.method|strings|contains("ManagedSnapshotFixture.EnterContendedMonitor"))) and any(.reader.threads[]; .status=="captured" and any(.frames[]?;.method|strings|contains("ManagedSnapshotFixture.HoldMonitorUntilRelease")))' "$run/lease-result.json" >/dev/null
    jq -e '.reader | ([.threads[]|select(any(.frames[]?;.method|strings|contains("HoldMonitorUntilRelease")))|.osThreadId]) as $owners | any(.heldMonitors[]; .waitingThreads>0 and (.ownerOSThreadId as $id | $owners|index($id)!=null))' "$run/lease-result.json" >/dev/null
    for mode in debug-timeout debug-crash; do
        if timeout --kill-after=3s 20 "$proton/files/bin/wine" "$inspector" "$mode" "$target_pid" "$target_hash" \
            "$(winpath "$dac")" "$dac_hash" "$(winpath "$run/$mode.json")" 1000 >"$run/$mode.log" 2>&1; then
            echo "Expected $mode failure was not observed." >&2;exit 1
        fi
        jq -e --argjson pid "$target_pid" '.status=="held" and .killOnClose==false and .targetPid==$pid' "$run/$mode.json.lease-held.json" >/dev/null
        if [[ "$mode" == debug-timeout ]]; then jq -e '.status=="failed" and .errorType=="System.TimeoutException"' "$run/$mode.json" >/dev/null; fi
        # Read AFTER inspector exit. Progress before attachment cannot satisfy
        # this recovery assertion if the target remained paused on cleanup.
        before=$(cat "$run/managed-owner-heartbeat.txt")
        for ((i=0;i<50;i++)); do [[ $(cat "$run/managed-owner-heartbeat.txt") != "$before" ]] && break; sleep 0.1; done
        [[ $(cat "$run/managed-owner-heartbeat.txt") != "$before" ]]
    done
    touch "$run/release-managed-lock"
    settings
    send finish
    wait "$host_pid"
    kill -TERM "$journal_pid";wait "$journal_pid";journal_pid=''
    jq -se 'any(.[];.kind=="journal.heartbeat") and .[-1].kind=="journal.stop"' "$run/lease-journal/journal.jsonl" >/dev/null
    echo 'Managed debug lease, timeout recovery, controller-death recovery, and Settings verified.'
    exit 0
fi
if [[ "$managed_snapshot" == true ]]; then
    target_pid=$(jq -er .windowsProcessId "$run/state.json")
    target_hash=$(sha256sum "$run/app/ReplayHost.exe");target_hash=${target_hash%% *}
    dac="$run/prefix/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/mscordacwks.dll"
    dac_hash=$(sha256sum "$dac");dac_hash=${dac_hash%% *}
    inspector="$run/inspector/HdtManagedInspector.exe"
    bun "$snapshot/src/diagnostics/input-journal.ts" --output "$run/managed-journal" --from start --max-bytes 8388608 \
        --source "state=$run/state.json" --source "plugin=$run/plugin-input.json" >"$run/journal.log" 2>&1 &
    journal_pid=$!
    send managed-lock-stall
    sleep 1
    if timeout --kill-after=3s 10 "$proton/files/bin/wine" "$inspector" dump "$target_pid" "$target_hash" \
        "$(winpath "$run/cancelled.dmp")" "$(winpath "$run/cancel-result.json")" 1 >"$run/cancel.log" 2>&1; then
        echo 'Dump provider did not enforce the cancellation budget.' >&2;exit 1
    fi
    jq -e '.status=="failed" and .errorType=="System.ComponentModel.Win32Exception"' "$run/cancel-result.json" >/dev/null
    if timeout --kill-after=3s 15 "$proton/files/bin/wine" "$inspector" snapshot "$target_pid" "$target_hash" \
        "$(winpath "$dac")" "$dac_hash" "$(winpath "$run/snapshot-result.json")" >"$run/snapshot.log" 2>&1; then
        report=$run/snapshot-result.json
    else
        # Accept only the documented unavailable PSS capability, not arbitrary
        # loader, identity, or DAC errors as a reason to switch mechanisms.
        jq -e '.status=="failed" and .errorType=="System.PlatformNotSupportedException"' "$run/snapshot-result.json" >/dev/null
        timeout --kill-after=3s 30 "$proton/files/bin/wine" "$inspector" dump "$target_pid" "$target_hash" \
            "$(winpath "$run/replay-target.dmp")" "$(winpath "$run/dump-result.json")" 20000 >"$run/dump.log" 2>&1
        jq -e '.status=="captured" and .dumpBytes>0' "$run/dump-result.json" >/dev/null
        timeout --kill-after=3s 25 "$proton/files/bin/wine" "$inspector" read "$(winpath "$run/replay-target.dmp")" \
            "$(winpath "$dac")" "$dac_hash" "$(winpath "$run/managed-result.json")" >"$run/managed-read.log" 2>&1
        report=$run/managed-result.json
    fi
    jq -e '.status=="captured" and any(.threads[];.frames[]?.method|strings|contains("ManagedSnapshotFixture.EnterContendedMonitor")) and any(.threads[];.frames[]?.method|strings|contains("ManagedSnapshotFixture.HoldMonitorUntilRelease"))' "$report" >/dev/null
    jq -e '([.threads[]|select(any(.frames[]?;.method|strings|contains("HoldMonitorUntilRelease")))|.osThreadId]) as $owners | any(.heldMonitors[]; .waitingThreads>0 and (.ownerOSThreadId as $id | $owners|index($id)!=null))' "$report" >/dev/null
    if timeout --kill-after=3s 10 "$proton/files/bin/wine" "$inspector" dump "$target_pid" "$(printf '%064d' 0)" \
        "$(winpath "$run/forbidden.dmp")" "$(winpath "$run/wrong-identity.json")" 1000 >"$run/wrong-identity.log" 2>&1; then
        echo 'Wrong executable hash was accepted.' >&2;exit 1
    fi
    jq -e '.status=="failed" and .errorType=="System.IO.InvalidDataException"' "$run/wrong-identity.json" >/dev/null
    [[ ! -e "$run/forbidden.dmp" ]]
    if timeout --kill-after=3s 10 "$proton/files/bin/wine" "$inspector" read "$(winpath "$run/replay-target.dmp")" \
        "$(winpath "$run/missing-dac.dll")" "$dac_hash" "$(winpath "$run/missing-dac.json")" >"$run/missing-dac.log" 2>&1; then
        echo 'Missing DAC was accepted.' >&2;exit 1
    fi
    jq -e '.status=="failed" and .errorType=="System.IO.FileNotFoundException"' "$run/missing-dac.json" >/dev/null
    touch "$run/release-managed-lock"
    for ((i=0;i<50;i++)); do
        [[ $(jq -r .pluginUpdates "$run/state.json") -gt 10 ]] && break
        sleep 0.1
    done
    settings
    send finish
    wait "$host_pid"
    kill -TERM "$journal_pid";wait "$journal_pid";journal_pid=''
    jq -se 'any(.[];.kind=="journal.heartbeat") and .[-1].kind=="journal.stop"' "$run/managed-journal/journal.jsonl" >/dev/null
    echo 'External managed stacks, monitor owner, identity rejection, and UI recovery verified in isolation.'
    exit 0
fi
if [[ "$canonical_events" == true ]]; then
    # The native journal is independently owned by this supervisor. Its sources
    # are the synthetic fixture and actual HDT publishers, not interpreted markers.
    touch "$run/canonical-input.json"
    bun "$snapshot/src/diagnostics/input-journal.ts" --output "$run/journal" --from start --max-bytes 8388608 \
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
    for ((i=0;i<30;i++)); do
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
    (( after>before ))
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
    timeout 30 "$proton/files/bin/wine" "$run/app/ReplayHost.exe" "$(winpath "$run/host-second.log")" "$plugin" "$level" "$particles" >"$run/wine-second.log" 2>&1 &
    host_pid=$!
    for ((i=0;i<100;i++)); do
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
