#!/usr/bin/env bash
# Parent-registry prerequisite only: no Hyprland child, game, HDT or host input.
set -euo pipefail
umask 077
if [[ ${1:-} != --inside ]]; then
    root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
    [[ $# == 2 ]] || {
        echo 'Usage: hyprland-probe.sh HYPRLAND_BINARY RENDER_NODE' >&2
        exit 2
    }
    compositor=$(realpath -e -- "$1")
    render=$(realpath -e -- "$2")
    [[ "$compositor" == /nix/store/*/bin/Hyprland && -x "$compositor" ]]
    [[ "$render" =~ ^/dev/dri/renderD[0-9]+$ && -c "$render" ]]
    mkdir -p "$root/evidence"
    run=$(mktemp -d "$root/evidence/hyprland-probe-XXXXXXXX")
    chmod 700 "$run"
    mkdir -m 700 "$run/home" "$run/source"
    cp "$root/tests/replay/hyprland-probe.sh" "$root/tests/replay/wayland-parent-check.sh" "$run/source/"
    (cd "$run/source" && sha256sum ./*.sh) >"$run/source-sha256.txt"
    revision=$(git -C "$root" rev-parse HEAD)
    dirty=false
    [[ -z $(git -C "$root" status --porcelain) ]] || dirty=true
    "$compositor" --version-json >"$run/compositor-version.json"
    jq -e '.buildAquamarine=="0.15.1"' "$run/compositor-version.json" >/dev/null || {
        echo 'This prerequisite validator supports Aquamarine 0.15.1 only.' >&2
        exit 2
    }
    weston --version >"$run/weston-version.txt"
    printf 'hdt-test:x:%s:%s:Private test:%s:/bin/sh\n' "$(id -u)" "$(id -g)" "$run/home" >"$run/passwd"
    printf 'hdt-test:x:%s:\n' "$(id -g)" >"$run/group"
    jq -n --arg run "$run" '{kind:"hdt-hyprland-probe",run:$run}' >"$run/ownership.json"
    status=0
    timeout --kill-after=3s 30 bwrap --die-with-parent --unshare-all \
        --ro-bind /nix/store /nix/store --ro-bind /etc /etc \
        --ro-bind /bin /bin --ro-bind /usr /usr --ro-bind /lib64 /lib64 \
        --ro-bind /sys /sys --ro-bind /run/current-system /run/current-system \
        --ro-bind /run/opengl-driver /run/opengl-driver \
        --bind "$run" "$run" --ro-bind "$run/source" "$run/source" \
        --ro-bind "$run/passwd" /etc/passwd --ro-bind "$run/group" /etc/group \
        --proc /proc --dev /dev --tmpfs /dev/shm --dev-bind "$render" "$render" \
        --tmpfs /run/user --tmpfs /tmp --tmpfs "$HOME" --clearenv \
        --setenv PATH "$PATH" --setenv HOME "$run/home" \
        --setenv XDG_RUNTIME_DIR /tmp/hdt-runtime --setenv LANG C.UTF-8 \
        --setenv LP_NUM_THREADS 2 \
        --setenv HYPRLAND_NO_SD_VARS 1 --setenv HYPRLAND_NO_SD_NOTIFY 1 \
        bash "$run/source/hyprland-probe.sh" --inside "$run" \
        >"$run/supervisor.log" 2>&1 || status=$?
    jq -n --argjson status "$status" --arg phase "$(cat "$run/phase.txt" 2>/dev/null || echo namespace-start)" \
        --arg revision "$revision" --argjson dirty "$dirty" \
        '{schema:1,exitStatus:$status,phase:$phase,passed:($status==0),sourceRevision:$revision,sourceDirty:$dirty,scope:"parent protocol registry only; no child compositor, HDT or game"}' >"$run/result.json"
    echo "Hyprland probe exit $status. Evidence: $run"
    exit "$status"
fi
run=$2
[[ "$run" == /*/evidence/hyprland-probe-* ]]
jq -e --arg run "$run" '.kind=="hdt-hyprland-probe" and .run==$run' "$run/ownership.json" >/dev/null
[[ -z ${DISPLAY-} && -z ${WAYLAND_DISPLAY-} && -z ${HYPRLAND_INSTANCE_SIGNATURE-} && -z ${DBUS_SESSION_BUS_ADDRESS-} && ! -e /dev/dri/card0 && ! -e /dev/dri/card1 ]]
mkdir -m 700 "$XDG_RUNTIME_DIR"
mkdir -p /tmp/.X11-unix
chmod 1777 /tmp/.X11-unix
cleanup() {
    if [[ -n ${parent:-} ]]; then
        kill -TERM "$parent" 2>/dev/null || true
        wait "$parent" 2>/dev/null || true
    fi
}
trap cleanup EXIT
phase() { printf '%s\n' "$1" >"$run/phase.txt"; }
phase weston-start
weston --backend=headless --renderer=gl --fake-seat --shell=kiosk-shell.so \
    --width=1600 --height=900 --socket=parent-wayland --idle-time=0 \
    --no-config --log="$run/weston.log" >"$run/weston-stderr.log" 2>&1 &
parent=$!
for ((i = 0; i < 60; i++)); do
    [[ -S "$XDG_RUNTIME_DIR/parent-wayland" ]] && break
    kill -0 "$parent"
    sleep 0.1
done
[[ -S "$XDG_RUNTIME_DIR/parent-wayland" ]]
export WAYLAND_DISPLAY=parent-wayland
phase parent-registry
wayland-info >"$run/parent-registry.txt" 2>"$run/parent-registry-error.txt"
bash "$run/source/wayland-parent-check.sh" "$run/parent-registry.txt" >"$run/parent-capabilities.json"
phase parent-registry-compatible
