#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
frame=
native_ui=false
watcher_events=false
while (($#)); do
    case $1 in
        --native-frame)
            [[ $# -ge 2 ]] || exit 2
            frame=$(realpath -- "$2")
            [[ -f $frame && $(stat -c %s -- "$frame") -le 2097152 ]] || exit 2
            shift 2
            ;;
        --native-ui)
            native_ui=true
            shift
            ;;
        --watcher-events)
            watcher_events=true
            shift
            ;;
        *)
            echo 'Usage: check-rust-x11.sh [--native-frame FILE [--native-ui [--watcher-events]]]' >&2
            exit 2
            ;;
    esac
done
[[ $native_ui == false || -n $frame ]] || exit 2
[[ $watcher_events == false || $native_ui == true ]] || exit 2
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/rust-x11-XXXXXXXX")
chmod 700 "$run"
git rev-parse HEAD >"$run/source-head.txt"
git status --porcelain=v1 >"$run/source-status.txt"
native_env=()
if [[ -n $frame ]]; then
    cp -- "$frame" "$run/native-frame.json"
    native_env=(--setenv HDT_TEST_AVALONIA_FRAME /capture/native-frame.json)
fi
if [[ $native_ui == true ]]; then
    managed=$(dotnet msbuild avalonia/Hdt.Checks/Hdt.Checks.csproj -getProperty:TargetDir "-p:WatcherAcceptance=$watcher_events")
    [[ -f "$managed/Hdt.Checks.dll" ]] || {
        echo 'Build the selected native checks variant first.' >&2
        exit 2
    }
    if [[ $watcher_events == false ]]; then
        vendor_files=$(rg --files --hidden --no-ignore -g HearthWatcher.dll -g HearthMirror.dll "$managed") || [[ $? == 1 ]]
        if [[ -n $vendor_files ]]; then
            echo 'Ordinary native checks contain optional vendor assemblies; refusing to bundle them.' >&2
            exit 2
        fi
    fi
    cp -R "$managed" "$run/managed"
    cp -- "$(dirname -- "$frame")/native-overlay-observation.json" "$run/ui-observation.json"
    native_env+=(--setenv HDT_TEST_NATIVE_UI_DLL /capture/managed/Hdt.Checks.dll
        --setenv HDT_TEST_UI_DOTNET "$(readlink -f "$(command -v dotnet)")"
        --setenv HDT_TEST_UI_XDOTOOL "$(readlink -f "$(command -v xdotool)")"
        --setenv LD_LIBRARY_PATH "$LD_LIBRARY_PATH"
        --setenv DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 1)
fi
if [[ $watcher_events == true ]]; then native_env+=(--setenv HDT_TEST_WATCHER_ACCEPTANCE 1); fi
build=$(CARGO_BUILD_JOBS=2 cargo test --locked --test rust-x11-isolated --no-run --message-format=json)
executable=$(jq -er 'select(.reason=="compiler-artifact" and .target.name=="rust-x11-isolated" and .executable!=null) | .executable' <<<"$build")
CARGO_BUILD_JOBS=2 cargo build --locked
cp "$executable" "$run/acceptance"
cp "${CARGO_TARGET_DIR:-target}/debug/hdt-host" "$run/hdt-host"
read -r -a flags <<<"$(pkg-config --cflags --libs x11 xcomposite xrender)"
cc -Wall -Wextra -Werror tests/native/reference/x-surface-probe.c -o "$run/c-surface" "${flags[@]}"
(
    cd "$run"
    sha256sum acceptance hdt-host c-surface >source-sha256.txt
    if [[ $native_ui == true ]]; then
        sha256sum managed/Hdt.Checks.dll managed/Hdt.Avalonia.dll >>source-sha256.txt
    fi
)
xvfb=$(readlink -f "$(command -v Xvfb)")
shell=$(readlink -f "$(command -v bash)")
status=0
timeout --kill-after=3s 90 bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh \
    --bind "$run" /capture --chdir /capture --setenv HOME /tmp \
    --setenv HDT_TEST_X11_ISOLATED 1 --setenv HDT_TEST_XVFB "$xvfb" \
    --setenv HDT_TEST_HOST_BINARY /capture/hdt-host --setenv HDT_TEST_C_SURFACE /capture/c-surface \
    "${native_env[@]}" \
    /capture/acceptance --ignored --nocapture --test-threads=1 >"$run/result.txt" 2>&1 || status=$?
cat "$run/result.txt"
if ((status != 0)); then
    echo "Rust X11 acceptance failed ($status). Evidence: $run" >&2
    exit "$status"
fi
echo "Rust X11 acceptance passed. Evidence: $run"
