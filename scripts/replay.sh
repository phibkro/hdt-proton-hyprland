#!/usr/bin/env bash
set -euo pipefail
# Parse the supervisor before it starts waiting. Later source edits cannot
# change unread commands in an already-running Bash process.
main() {
umask 077
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
lab=$root/local/replay
usage() { echo 'Usage: hdt-replay prepare --proton DIR --runtime DIR [--reuse] | run [--inject-failure] [--with-plugin] [--plaque-level 0..5] [--without-particles] [--battlegrounds-tabs] [--diagnostics] [--canonical-events] [--managed-snapshot|--managed-debug-lease]'; }
fail() { echo "HDT replay: $*" >&2; exit 1; }
action=${1:-help}
if (( $# )); then shift; fi
[[ "$action" == prepare || "$action" == run ]] || { usage; exit 0; }
mkdir -p "$lab"
chmod 700 "$lab"
exec 9>"$lab/.lock"
flock -n 9 || fail 'Another replay command owns the environment.'
manifest=$lab/ownership.json
if [[ "$action" == prepare ]]; then
    proton='' runtime='' reuse=false
    while (( $# )); do
        case "$1" in
            --proton|--runtime)
                (( $# >= 2 )) || fail 'Missing directory argument.'
                if [[ "$1" == --proton ]]; then proton=$(realpath -e -- "$2"); else runtime=$(realpath -e -- "$2"); fi
                shift 2 ;;
            --reuse) reuse=true; shift ;;
            *) usage; exit 2 ;;
        esac
    done
    [[ -x "$proton/files/bin/wine" && -x "$runtime/run" ]] || fail 'Select an installed Proton directory and Steam runtime directory.'
    cd "$root"
    paths=$(bun -e 'import {prefix,appDirectory} from "./src/runtime/config"; console.log(JSON.stringify({prefix,appDirectory}))')
    source_prefix=$(jq -er .prefix <<<"$paths")
    source_app=$(realpath -e -- "$(jq -er .appDirectory <<<"$paths")")
    clr=drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/clr.dll
    [[ -f "$source_prefix/$clr" ]] || fail 'Native .NET is required in the source prefix.'
    platform=$(sed -n 's/^dir=\(steamrt[a-zA-Z0-9_.-]*\)$/\1/p' "$runtime/run")
    [[ -n "$platform" && -d "$runtime/$platform" ]] || fail 'Unsupported Steam runtime layout.'
    if [[ -e "$lab/seed" ]]; then
        [[ "$reuse" == true && -f "$manifest" ]] || fail 'Seed exists. Use --reuse to verify and adopt this project-owned seed.'
        jq -e --arg project "$root" '.kind=="hdt-ui-replay" and .project==$project' "$manifest" >/dev/null || fail 'Seed ownership does not match this project.'
    else
        mkdir -p "$lab/seed/prefix/drive_c" "$lab/seed/app"
        cp -a --reflink=auto "$source_prefix/drive_c/windows" "$lab/seed/prefix/drive_c/" >"$lab/prepare.log" 2>&1 || fail "Windows copy failed; inspect $lab/prepare.log"
        for file in system.reg userdef.reg; do
            before=$(sha256sum "$source_prefix/$file"); before=${before%% *}
            cp --reflink=auto "$source_prefix/$file" "$lab/seed/prefix/$file"
            after=$(sha256sum "$source_prefix/$file"); after=${after%% *}
            [[ "$before" == "$after" ]] || fail 'Source registry changed during copy. Preparation is incomplete.'
        done
        # Match the already-initialized Windows files with their Wine update marker.
        # Without it Wine reruns prefix update actions against the stripped profile.
        cp -a "$source_prefix/.update-timestamp" "$lab/seed/prefix/.update-timestamp"
        # Keep Wine preferences, excluding arbitrary application and account keys.
        awk 'BEGIN {keep=1} /^\[/ {keep=index($0,"[Software\\\\Wine")==1} keep {print}' "$source_prefix/user.reg" >"$lab/seed/prefix/user.reg"
        mkdir -p "$lab/seed/prefix/dosdevices" "$lab/seed/prefix/drive_c/users/steamuser"
        ln -s ../drive_c "$lab/seed/prefix/dosdevices/c:"
        ln -s / "$lab/seed/prefix/dosdevices/z:"
        rsync -a --exclude=/Plugins --exclude='/Crash Reports' --exclude='*.log' "$source_app/" "$lab/seed/app/"
    fi
    cmp "$source_prefix/$clr" "$lab/seed/prefix/$clr" || fail 'Seed CLR differs from the selected source.'
    [[ -f "$lab/seed/prefix/.update-timestamp" ]] || fail 'Seed is missing its Wine initialization marker.'
    cmp "$source_app/HearthstoneDeckTracker.exe" "$lab/seed/app/HearthstoneDeckTracker.exe" || fail 'Seed HDT differs from the selected source.'
    if [[ ! -d "$lab/runtime" ]]; then
        mkdir "$lab/runtime"
        for file in run pressure-vessel "$platform"; do
            cp -a --reflink=auto "$runtime/$file" "$lab/runtime/" >>"$lab/prepare.log" 2>&1 || fail "Runtime copy failed; inspect $lab/prepare.log"
        done
    fi
    cmp "$runtime/run" "$lab/runtime/run" || fail 'Private runtime differs from the selected runtime.'
    hdt_hash=$(sha256sum "$lab/seed/app/HearthstoneDeckTracker.exe"); hdt_hash=${hdt_hash%% *}
    jq -n --arg project "$root" --arg prefix "$source_prefix" --arg proton "$proton" --arg runtime "$runtime" --arg hash "$hdt_hash" \
        '{schema:2,kind:"hdt-ui-replay",project:$project,sourcePrefix:$prefix,proton:$proton,runtime:$runtime,hdtSHA256:$hash}' >"$manifest.next"
    mv "$manifest.next" "$manifest"
    echo 'Prepared isolated HDT seed. No game or HDT account data copied.'
    exit 0
fi
expected=false
plugin=false
level=mixed
particles=true
battlegrounds_tabs=false
diagnostics=false
canonical_events=false
managed_snapshot=false
managed_debug_lease=false
settings_cycles=0
proton_override='' seed_override=''
while (( $# )); do
    case "$1" in
        --inject-failure) expected=true ;;
        --with-plugin) plugin=true ;;
        --battlegrounds-tabs) battlegrounds_tabs=true ;;
        --diagnostics) diagnostics=true ;;
        --canonical-events) canonical_events=true ;;
        --managed-snapshot) managed_snapshot=true ;;
        --managed-debug-lease) managed_debug_lease=true ;;
        --settings-cycles) (( $# >= 2 )) && [[ "$2" =~ ^([1-9]|10)$ ]] || fail 'Settings cycles must be 1..10.'; settings_cycles=$2;shift ;;
        --proton|--seed)
            (( $# >= 2 )) || fail 'Missing comparison directory.'
            if [[ "$1" == --proton ]]; then proton_override=$(realpath -e -- "$2"); else seed_override=$(realpath -e -- "$2"); fi
            shift ;;
        --without-particles) particles=false ;;
        --plaque-level) (( $# >= 2 )) && [[ "$2" =~ ^[0-5]$ ]] || fail 'Plaque level must be 0..5.'; level=$2; shift ;;
        *) usage; exit 2 ;;
    esac
    shift
done
[[ "$diagnostics" == false || "$plugin" == true ]] || fail '--diagnostics requires --with-plugin.'
[[ "$managed_snapshot" == false || ( "$diagnostics" == true && "$canonical_events" == false ) ]] || fail '--managed-snapshot requires --diagnostics --with-plugin and excludes --canonical-events.'
[[ "$managed_debug_lease" == false || ( "$diagnostics" == true && "$canonical_events" == false && "$managed_snapshot" == false ) ]] || fail '--managed-debug-lease requires --diagnostics --with-plugin and excludes other managed/canonical scenarios.'
[[ "$settings_cycles" == 0 || ( "$plugin" == true && "$diagnostics" == false && "$canonical_events" == false && "$managed_snapshot" == false && "$managed_debug_lease" == false ) ]] || fail 'Settings comparison requires only --with-plugin.'
[[ ( -z "$proton_override" && -z "$seed_override" ) || ( -n "$proton_override" && -n "$seed_override" && "$settings_cycles" != 0 ) ]] || fail 'Comparison --proton and --seed must be specified together.'
[[ ( "$managed_snapshot" == false && "$managed_debug_lease" == false ) || -f "$root/local/managed-inspector/build/HdtManagedInspector.exe" ]] || fail 'Build the managed inspector first.'
[[ -f "$manifest" ]] || fail 'Run prepare first.'
jq -e --arg root "$root" '.schema==2 and .kind=="hdt-ui-replay" and .project==$root' "$manifest" >/dev/null || fail 'Invalid environment ownership.'
proton=$(jq -er .proton "$manifest")
seed=$lab/seed
if [[ -n "$proton_override" ]]; then
    [[ "$seed_override" == "$lab/seed" || "$seed_override" == "$lab/seed-experimental-retained" ]] || fail 'Select a retained project-owned seed.'
    [[ -x "$proton_override/files/bin/wine" ]] || fail 'Selected Proton is not installed.'
    proton=$proton_override seed=$seed_override
fi
cmp "$proton/files/lib/wine/x86_64-windows/ntdll.dll" "$seed/prefix/drive_c/windows/system32/ntdll.dll" || fail 'Seed and selected Proton ntdll differ.'
[[ $(sha256sum "$seed/app/HearthstoneDeckTracker.exe" | cut -d ' ' -f 1) == "$(jq -er .hdtSHA256 "$manifest")" ]] || fail 'Seed HDT identity differs.'
source_prefix=$(jq -er .sourcePrefix "$manifest")
revision=$(git -C "$root" rev-parse HEAD)
source_dirty=false
[[ -z $(git -C "$root" status --porcelain) ]] || source_dirty=true
run=$lab/runs/$(date -u +%Y%m%dT%H%M%S)-$$
mkdir -p "$run/home" "$run/runtime"
mkdir "$run/source"
cp -R "$root/src" "$root/tests" "$root/scripts" "$run/source/"
(
    cd "$run/source"
    rg --files --hidden --no-ignore -0 | sort -z | xargs -0 sha256sum
) >"$run/source-sha256.txt"
snapshot_hash=$(sha256sum "$run/source-sha256.txt"); snapshot_hash=${snapshot_hash%% *}
chmod 700 "$run" "$run/runtime"
for part in prefix app; do cp -a --reflink=auto "$seed/$part" "$run/$part"; done
if [[ "$managed_snapshot" == true || "$managed_debug_lease" == true ]]; then cp -a "$root/local/managed-inspector/build" "$run/inspector"; fi
[[ $(realpath "$run/prefix") == "$run/prefix" && "$run/prefix" != "$source_prefix" ]] || fail 'Unsafe replay prefix.'
jq -n --arg run "$run" --arg root "$root" '{kind:"hdt-ui-replay-run",run:$run,project:$root}' >"$run/ownership.json"
# The namespace owns all test descendants. Host desktop sockets and user files
# are masked; the only Windows prefix available to the test is its own copy.
set +e
timeout --kill-after=5s 120 bwrap --die-with-parent --unshare-net --unshare-pid \
    --ro-bind /nix/store /nix/store --ro-bind /etc /etc \
    --ro-bind /bin /bin --ro-bind /usr /usr --ro-bind /lib64 /lib64 \
    --ro-bind /sys /sys --ro-bind /run/current-system /run/current-system \
    --ro-bind /run/opengl-driver /run/opengl-driver --ro-bind "$root" "$root" \
    --proc /proc --dev /dev --tmpfs /dev/shm --dev-bind /dev/dri /dev/dri \
    --bind "$lab" "$lab" --ro-bind "$seed" "$seed" \
    --ro-bind "$run/source" "$run/source" \
    --tmpfs "$HOME" --ro-bind "$proton" "$proton" --tmpfs "$source_prefix" \
    --tmpfs /run/user --tmpfs /tmp --clearenv \
    --setenv PATH "$PATH" --setenv HDT_REPLAY_TOOLS_PATH "$PATH" \
    --setenv HOME "$run/home" --setenv USER steamuser --setenv LANG C.UTF-8 \
    --setenv PRESSURE_VESSEL_VARIABLE_DIR "$run/pv-var" \
    steam-run "$lab/runtime/run" -- bash "$run/source/scripts/replay-inside.sh" "$root" "$run" "$proton" "$expected" "$plugin" "$level" "$particles" "$battlegrounds_tabs" "$diagnostics" "$canonical_events" "$managed_snapshot" "$managed_debug_lease" "$settings_cycles" \
    >"$run/supervisor.log" 2>&1
status=$?
set -e
jq -n --argjson status "$status" --argjson negative "$expected" --arg run "$run" \
    --arg revision "$revision" --arg snapshot "$snapshot_hash" --argjson dirty "$source_dirty" --argjson plugin "$plugin" --arg level "$level" --argjson particles "$particles" --argjson bgs "$battlegrounds_tabs" --argjson diagnostics "$diagnostics" --argjson canonical "$canonical_events" --argjson managed "$managed_snapshot" --argjson lease "$managed_debug_lease" --argjson cycles "$settings_cycles" --arg proton "$proton" --arg version "$(cat "$proton/version")" \
    '{exitStatus:$status,negativeAssertionTest:$negative,compatibilityPlugin:$plugin,plaqueLevel:$level,particles:$particles,battlegroundsTabs:$bgs,diagnostics:$diagnostics,canonicalEvents:$canonical,managedSnapshot:$managed,managedDebugLease:$lease,settingsCycles:$cycles,proton:$proton,protonVersion:$version,evidence:$run,sourceRevision:$revision,sourceDirtyAtStart:$dirty,sourceManifestSHA256:$snapshot}' >"$run/result.json"
echo "Replay exit status: $status. Evidence: $run"
exit "$status"
}
main "$@"
