#!/usr/bin/env bash
# Drive the real F# restart loop in committed source on an owned private display.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p local
run=$(mktemp -d "$PWD/local/native-watch-XXXXXXXX")
chmod 700 "$run"
git rev-parse HEAD >"$run/source-head.txt"
git status --porcelain >"$run/source-status.txt"
mkdir "$run/source"
git archive HEAD avalonia | tar -x -C "$run/source"
[[ -d ${NUGET_PACKAGES:?Enter the pinned Avalonia shell first.} ]]
cat >"$run/drive.sh" <<'DRIVE'
#!/usr/bin/env bash
set -euo pipefail
watcher=
xpid=
cleanup() {
    result=$?
    trap - EXIT
    if [[ -n $watcher ]]; then kill -TERM "$watcher" 2>/dev/null || true; wait "$watcher" 2>/dev/null || true; fi
    if [[ -n $xpid ]]; then kill -TERM "$xpid" 2>/dev/null || true; wait "$xpid" 2>/dev/null || true; fi
    exit "$result"
}
trap cleanup EXIT
mkdir -p /tmp/.X11-unix /capture/home
chmod 1777 /tmp/.X11-unix
Xvfb -displayfd 3 -screen 0 1280x900x24 -nolisten tcp 3>/capture/display-number >/capture/xvfb.log 2>&1 &
xpid=$!
for ((attempt=0;attempt<100;attempt++)); do
    [[ -s /capture/display-number ]] && break
    kill -0 "$xpid"
    sleep 0.1
done
[[ -s /capture/display-number ]]
export DISPLAY=":$(cat /capture/display-number)"
cd /capture/source
dotnet --version >/capture/sdk-version.txt 2>&1
bash avalonia/dev.sh watch >/capture/watch.log 2>&1 &
watcher=$!
window_for_color() {
    local color=$1 image=$2 windows
    for ((attempt=0;attempt<300;attempt++)); do
        kill -0 "$watcher" || { cat /capture/watch.log >&2; return 1; }
        windows=$(xdotool search --onlyvisible --name '^Hearthstone Deck Tracker' 2>/dev/null || true)
        if [[ $windows =~ ^[0-9]+$ ]]; then
            if magick import -window "$windows" "$image" 2>/dev/null; then
                magick "$image" -depth 8 -format %c histogram:info:- >/capture/palette.txt
                if rg -qi -- "$color" /capture/palette.txt; then
                    printf '%s\n' "$windows"
                    return 0
                fi
            fi
        fi
        sleep 0.1
    done
    for status in /proc/[0-9]*/status; do
        [[ -r $status ]] && sed -n '1,7p' "$status" >>/capture/processes.txt
    done
    cat /capture/watch.log >&2
    echo "Timed out waiting for rendered palette $color" >&2
    return 1
}
first=$(window_for_color '#2489BD' /capture/before.png)
first_pid=$(xdotool getwindowpid "$first")
printf '%s\n' "$first_pid" >/capture/before-pid.txt
sha256sum avalonia/Hdt.Views/bin/Debug/net10.0/Hdt.Views.dll >/capture/before-assembly.txt
# Only the archived, run-owned F# source is edited, never the developer checkout.
sed -i 's/#2489BD/#D95C90/g' avalonia/Hdt.Views/TrackerLayout.fs
second=$(window_for_color '#D95C90' /capture/after.png)
second_pid=$(xdotool getwindowpid "$second")
printf '%s\n' "$second_pid" >/capture/after-pid.txt
sha256sum avalonia/Hdt.Views/bin/Debug/net10.0/Hdt.Views.dll >/capture/after-assembly.txt
[[ $first_pid != "$second_pid" ]]
! cmp -s /capture/before-assembly.txt /capture/after-assembly.txt
if xdotool getwindowname "$first" >/dev/null 2>&1; then
    echo 'Previous development window survived the restart.' >&2
    exit 1
fi
[[ ! -d /proc/$first_pid ]]
kill -0 "$second_pid"
echo 'PASS real F# edit -> rebuild -> new process/window -> changed palette pixels; old process/window gone.'
DRIVE
shell=$(readlink -f "$(command -v bash)")
status=0
timeout --kill-after=5s 150 bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh --bind "$run" /capture \
    --bind "$NUGET_PACKAGES" /packages --chdir /capture \
    --setenv PATH "$PATH" --setenv HOME /capture/home \
    --setenv DOTNET_ROOT "$DOTNET_ROOT" --setenv DOTNET_CLI_HOME /capture/dotnet --setenv NUGET_PACKAGES /packages \
    --setenv LD_LIBRARY_PATH "$LD_LIBRARY_PATH" --setenv DOTNET_PROCESSOR_COUNT 2 \
    --setenv DOTNET_CLI_TELEMETRY_OPTOUT 1 --setenv DOTNET_NOLOGO 1 \
    --setenv DOTNET_SKIP_FIRST_TIME_EXPERIENCE 1 --setenv DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 0 \
    --setenv DOTNET_CLI_USE_MSBUILD_SERVER 0 --setenv DOTNET_WATCH_SUPPRESS_EMOJIS 1 \
    --setenv DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER 1 \
    "$shell" /capture/drive.sh >"$run/result.txt" 2>&1 || status=$?
cat "$run/result.txt"
echo "Native watch evidence: $run"
exit "$status"
