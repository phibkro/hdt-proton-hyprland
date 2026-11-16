#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
lab=$root/local/replay
usage() { echo 'Usage: hdt-replay prepare --proton DIR --runtime DIR [--reuse] | run [--inject-failure] [--with-plugin] [--plaque-level 0..5] [--without-particles] [--battlegrounds-tabs] [--diagnostics] [--canonical-events] [--desktop-input] [--reference-views]'; }
fail() {
    echo "HDT replay: $*" >&2
    exit 1
}
action=${1:-help}
if (($#)); then shift; fi
[[ "$action" == prepare || "$action" == run ]] || {
    usage
    exit 0
}
mkdir -p "$lab"
chmod 700 "$lab"
exec 9>"$lab/.lock"
flock -n 9 || fail 'Another replay command owns the environment.'
manifest=$lab/ownership.json
if [[ "$action" == prepare ]]; then
    proton='' runtime='' reuse=false
    while (($#)); do
        case "$1" in
            --proton | --runtime)
                (($# >= 2)) || fail 'Missing directory argument.'
                if [[ "$1" == --proton ]]; then proton=$(realpath -e -- "$2"); else runtime=$(realpath -e -- "$2"); fi
                shift 2
                ;;
            --reuse)
                reuse=true
                shift
                ;;
            *)
                usage
                exit 2
                ;;
        esac
    done
    [[ -x "$proton/files/bin/wine" && -x "$runtime/run" ]] || fail 'Select an installed Proton directory and Steam runtime directory.'
    cd "$root"
    paths=$(bash scripts/native-host.sh config)
    source_prefix=$(realpath -e -- "$(jq -er .prefix <<<"$paths")")
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
            before=$(sha256sum "$source_prefix/$file")
            before=${before%% *}
            cp --reflink=auto "$source_prefix/$file" "$lab/seed/prefix/$file"
            after=$(sha256sum "$source_prefix/$file")
            after=${after%% *}
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
    hdt_hash=$(sha256sum "$lab/seed/app/HearthstoneDeckTracker.exe")
    hdt_hash=${hdt_hash%% *}
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
desktop_input=false
reference_views=false
while (($#)); do
    case "$1" in
        --inject-failure) expected=true ;;
        --with-plugin) plugin=true ;;
        --battlegrounds-tabs) battlegrounds_tabs=true ;;
        --diagnostics) diagnostics=true ;;
        --canonical-events) canonical_events=true ;;
        --desktop-input) desktop_input=true ;;
        --reference-views) reference_views=true ;;
        --without-particles) particles=false ;;
        --plaque-level)
            (($# >= 2)) && [[ "$2" =~ ^[0-5]$ ]] || fail 'Plaque level must be 0..5.'
            level=$2
            shift
            ;;
        *)
            usage
            exit 2
            ;;
    esac
    shift
done
[[ "$diagnostics" == false || "$plugin" == true ]] || fail '--diagnostics requires --with-plugin.'
[[ "$canonical_events" == false || "$diagnostics" == false ]] || fail '--canonical-events and --diagnostics require separate replay runs.'
[[ "$desktop_input" == false || "$plugin" == true ]] || fail '--desktop-input requires --with-plugin.'
[[ "$desktop_input" == false || "$canonical_events" == false ]] || fail '--desktop-input and --canonical-events require separate replay runs.'
[[ "$reference_views" == false || ("$canonical_events" == false && "$desktop_input" == false && "$diagnostics" == false && "$expected" == false && "$battlegrounds_tabs" == false) ]] || fail '--reference-views requires its own replay run.'
[[ "$reference_views" == false || "$particles" == false ]] || fail '--reference-views requires --without-particles to keep the known renderer failure out of reference capture.'
[[ -f "$manifest" ]] || fail 'Run prepare first.'
jq -e --arg root "$root" '.schema==2 and .kind=="hdt-ui-replay" and .project==$root' "$manifest" >/dev/null || fail 'Invalid environment ownership.'
proton=$(jq -er .proton "$manifest")
source_prefix=$(jq -er .sourcePrefix "$manifest")
revision=$(git -C "$root" rev-parse HEAD)
source_dirty=false
[[ -z $(git -C "$root" status --porcelain) ]] || source_dirty=true
run=$lab/runs/$(date -u +%Y%m%dT%H%M%S)-$$
mkdir -p "$run/home" "$run/runtime"
mkdir "$run/source"
cp -R "$root/src" "$root/tests" "$root/scripts" "$run/source/"
host=${HDT_HOST_BINARY:-${CARGO_TARGET_DIR:-$root/target}/debug/hdt-host}
[[ -x "$host" ]] || fail 'Build the native host before replay.'
cp "$host" "$run/source/hdt-host"
(
    cd "$run/source"
    rg --files --hidden --no-ignore -0 | sort -z | xargs -0 sha256sum
) >"$run/source-sha256.txt"
snapshot_hash=$(sha256sum "$run/source-sha256.txt")
snapshot_hash=${snapshot_hash%% *}
chmod 700 "$run" "$run/runtime"
for part in prefix app; do cp -a --reflink=auto "$lab/seed/$part" "$run/$part"; done
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
    --bind "$lab" "$lab" --ro-bind "$lab/seed" "$lab/seed" \
    --ro-bind "$run/source" "$run/source" \
    --tmpfs "$HOME" --ro-bind "$proton" "$proton" --tmpfs "$source_prefix" \
    --tmpfs /run/user --tmpfs /tmp --clearenv \
    --setenv PATH "$PATH" --setenv HDT_REPLAY_TOOLS_PATH "$PATH" \
    --setenv HOME "$run/home" --setenv USER steamuser --setenv LANG C.UTF-8 \
    --setenv PRESSURE_VESSEL_VARIABLE_DIR "$run/pv-var" \
    steam-run "$lab/runtime/run" -- bash "$run/source/scripts/replay-inside.sh" "$root" "$run" "$proton" "$expected" "$plugin" "$level" "$particles" "$battlegrounds_tabs" "$diagnostics" "$canonical_events" "$desktop_input" "$reference_views" \
    >"$run/supervisor.log" 2>&1
status=$?
set -e
jq -n --argjson status "$status" --argjson negative "$expected" --arg run "$run" \
    --arg revision "$revision" --arg snapshot "$snapshot_hash" --argjson dirty "$source_dirty" --argjson plugin "$plugin" --arg level "$level" --argjson particles "$particles" --argjson bgs "$battlegrounds_tabs" --argjson diagnostics "$diagnostics" --argjson canonical "$canonical_events" --argjson desktop "$desktop_input" --argjson reference "$reference_views" \
    '{exitStatus:$status,negativeAssertionTest:$negative,compatibilityPlugin:$plugin,plaqueLevel:$level,particles:$particles,battlegroundsTabs:$bgs,diagnostics:$diagnostics,canonicalEvents:$canonical,desktopInput:$desktop,referenceViews:$reference,evidence:$run,sourceRevision:$revision,sourceDirtyAtStart:$dirty,sourceManifestSHA256:$snapshot}' >"$run/result.json"
echo "Replay exit status: $status. Evidence: $run"
exit "$status"
