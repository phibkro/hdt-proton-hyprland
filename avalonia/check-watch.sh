#!/usr/bin/env bash
# Drive the real F# restart loop in committed source on an owned private display.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
[[ $# -eq 0 && ${HDT_HOST_BINARY:-} == /* && -x ${HDT_HOST_BINARY:-} ]] || {
    echo 'Build the Rust host and set HDT_HOST_BINARY to its absolute executable path before running check-watch.sh without arguments.' >&2
    exit 64
}
mkdir -p local
run=$(mktemp -d "$PWD/local/native-watch-XXXXXXXX")
chmod 700 "$run"
git rev-parse HEAD >"$run/source-head.txt"
git status --porcelain >"$run/source-status.txt"
printf '%s\n' "$HDT_HOST_BINARY" >"$run/host-source-path.txt"
cp -- "$HDT_HOST_BINARY" "$run/hdt-host"
sha256sum "$run/hdt-host" >"$run/host-sha256.txt"
mkdir "$run/source"
printf 'developer:x:%s:%s:Native development test:/capture/home:/bin/sh\n' "$(id -u)" "$(id -g)" >"$run/passwd"
printf 'developer:x:%s:\n' "$(id -g)" >"$run/group"
printf 'passwd: files\ngroup: files\nhosts: files\n' >"$run/nsswitch.conf"
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
# Dependencies were restored explicitly before the network-free drive. NuGet's
# vulnerability-feed lookup is not a source of facts for this offline UI test.
cat > NuGet.Config <<'CONFIG'
<configuration><packageSources><clear /></packageSources><auditSources><clear /></auditSources></configuration>
CONFIG
export NuGetAudit=false
dotnet --version >/capture/sdk-version.txt 2>&1
bash avalonia/dev.sh watch >/capture/watch.log 2>&1 &
watcher=$!
window_for_color() {
    local color=$1 image=$2 previous_pid=${3:-} windows window_pid
    for ((attempt=0;attempt<300;attempt++)); do
        kill -0 "$watcher" || { cat /capture/watch.log >&2; return 1; }
        windows=$(xdotool search --onlyvisible --name '^Hearthstone Deck Tracker' 2>/dev/null || true)
        printf '%(%s)T color=%s attempt=%d windows=%q\n' -1 "$color" "$attempt" "$windows" >>/capture/window-search.log
        if [[ $windows =~ ^[0-9]+$ ]]; then
            window_pid=$(timeout 2 xdotool getwindowpid "$windows" 2>/dev/null || true)
            printf 'pid=%s\n' "$window_pid" >>/capture/window-search.log
            # Do not capture the retiring process: import can wait indefinitely
            # when its target disappears during the X11 image request.
            if [[ $window_pid =~ ^[0-9]+$ && $window_pid != "$previous_pid" ]] &&
                timeout --kill-after=1s 2 magick import -window "$windows" "$image" 2>/dev/null; then
                magick "$image" -depth 8 -format %c histogram:info:- >/capture/palette.txt
                # The cyan card borders belong to loaded replay choices. The
                # empty/error page also has the brand palette, so it cannot
                # satisfy this independent content assertion.
                if rg -qi -- "$color" /capture/palette.txt &&
                    awk '/#067F93/ { pixels += $1 } END { exit pixels < 1000 }' /capture/palette.txt; then
                    cp /capture/palette.txt "${image%.png}-palette.txt"
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
second=$(window_for_color '#D95C90' /capture/after.png "$first_pid")
second_pid=$(xdotool getwindowpid "$second")
printf '%s\n' "$second_pid" >/capture/after-pid.txt
sha256sum avalonia/Hdt.Views/bin/Debug/net10.0/Hdt.Views.dll >/capture/after-assembly.txt
[[ $first_pid != "$second_pid" ]]
! cmp -s /capture/before-assembly.txt /capture/after-assembly.txt
if [[ $(xdotool getwindowpid "$first" 2>/dev/null || true) == "$first_pid" ]]; then
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
    --dir /etc --ro-bind "$run/passwd" /etc/passwd --ro-bind "$run/group" /etc/group \
    --ro-bind "$run/nsswitch.conf" /etc/nsswitch.conf \
    --bind "$NUGET_PACKAGES" /packages --chdir /capture \
    --setenv PATH "$PATH" --setenv HOME /capture/home \
    --setenv DOTNET_CLI_HOME /capture/dotnet --setenv NUGET_PACKAGES /packages \
    --setenv HDT_HOST_BINARY /capture/hdt-host \
    --setenv LD_LIBRARY_PATH "$LD_LIBRARY_PATH" --setenv DOTNET_PROCESSOR_COUNT 2 \
    --setenv DOTNET_CLI_TELEMETRY_OPTOUT 1 --setenv DOTNET_NOLOGO 1 \
    --setenv DOTNET_SKIP_FIRST_TIME_EXPERIENCE 1 --setenv DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 0 \
    --setenv NuGetAudit false --setenv DOTNET_CLI_USE_MSBUILD_SERVER 0 --setenv DOTNET_WATCH_SUPPRESS_EMOJIS 1 \
    --setenv DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER 1 \
    "$shell" /capture/drive.sh >"$run/result.txt" 2>&1 || status=$?
cat "$run/result.txt"
echo "Native watch evidence: $run"
exit "$status"
