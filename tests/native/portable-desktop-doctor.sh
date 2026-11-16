#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --client ]]; then
    [[ ${HDT_PORTABLE_DOCTOR_NAMESPACE:-} == 1 && $HOME == /capture/home && $XDG_RUNTIME_DIR == /capture/runtime ]]
    [[ ! -e /run/user && ! -e /dev/dri && ! -e /dev/input ]]
    [[ -n ${DISPLAY:-} && -n ${WAYLAND_DISPLAY:-} ]]
    # The real compositor supplies these client variables. Never inherit a display.
    xprop -root _NET_SUPPORTING_WM_CHECK >/capture/window-manager.txt
    jq -n --arg display "$DISPLAY" --arg wayland "$WAYLAND_DISPLAY" \
        '{xwaylandDisplay:$display,waylandDisplay:$wayland}' >/capture/client.json
    exit 0
fi
if [[ ${1:-} == --inside ]]; then
    [[ ${HDT_PORTABLE_DOCTOR_NAMESPACE:-} == 1 && ! -e /run/user && ! -e /dev/dri && ! -e /dev/input ]]
    [[ -z ${DISPLAY:-} && -z ${WAYLAND_DISPLAY:-} && -z ${DBUS_SESSION_BUS_ADDRESS:-} ]]
    awk -F: 'NR>2 {gsub(/ /,"",$1); if($1!="lo") bad=1} END {exit bad}' /proc/net/dev
    mkdir -p /tmp/.X11-unix
    chmod 1777 /tmp/.X11-unix
    "$HDT_KWIN_BINARY" --version >/capture/kwin-version.txt
    exec dbus-run-session -- "$HDT_KWIN_BINARY" --virtual --xwayland \
        --socket hdt-doctor --width 2560 --height 1440 --no-lockscreen \
        --no-global-shortcuts --exit-with-session 'bash /capture/doctor.sh --client'
fi
[[ $# -eq 0 ]]
: "${HDT_KWIN_BINARY:?Enter the pinned desktop acceptance shell.}"
: "${HDT_MESA_EGL_VENDOR:?Select the pinned Mesa EGL vendor explicitly.}"
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
mkdir -p "$root/evidence"
run=$(mktemp -d "$root/evidence/portable-desktop-XXXXXXXX")
chmod 700 "$run"
mkdir -m 700 "$run/runtime" "$run/home"
cp -- "${BASH_SOURCE[0]}" "$run/doctor.sh"
git -C "$root" rev-parse HEAD >"$run/source-head.txt"
git -C "$root" status --porcelain >"$run/source-status.txt"
sha256sum "$run/doctor.sh" "$HDT_KWIN_BINARY" "$HDT_MESA_EGL_VENDOR" >"$run/source-sha256.txt"
shell=$(readlink -f "$(command -v bash)")
status=0
timeout --kill-after=3s 25 bwrap --unshare-all --die-with-parent --new-session \
    --ro-bind /nix/store /nix/store --ro-bind /etc /etc --dir /bin --ro-bind "$shell" /bin/sh \
    --proc /proc --dev /dev --tmpfs /tmp --bind "$run" /capture --clearenv \
    --setenv PATH "$PATH" --setenv LANG C.UTF-8 \
    --setenv HOME /capture/home --setenv XDG_RUNTIME_DIR /capture/runtime \
    --setenv HDT_PORTABLE_DOCTOR_NAMESPACE 1 --setenv HDT_KWIN_BINARY "$HDT_KWIN_BINARY" \
    --setenv QT_FORCE_STDERR_LOGGING 1 --setenv LIBGL_ALWAYS_SOFTWARE 1 --setenv LP_NUM_THREADS 2 \
    --setenv __EGL_VENDOR_LIBRARY_FILENAMES "$HDT_MESA_EGL_VENDOR" \
    bash /capture/doctor.sh --inside >"$run/compositor.log" 2>&1 || status=$?
ready=false
if ((status == 0)) && [[ -s $run/client.json ]] && grep -q 'WINDOW' "$run/window-manager.txt"; then ready=true; fi
jq -n --argjson status "$status" --argjson ready "$ready" \
    '{kind:"kwin-virtual-prerequisite-doctor",exitStatus:$status,clientReady:$ready,
      desktopAcceptance:false,physicalDevicesExposed:false,hostDesktopExposed:false}' >"$run/result.json"
echo "KWin prerequisite evidence: $run"
if [[ $ready != true ]]; then
    echo 'KWin prerequisite unavailable; inspect compositor.log. No desktop acceptance ran.' >&2
    exit 1
fi
echo 'KWin client prerequisite passed. HDT/WPF input and live desktop acceptance remain untested.'
