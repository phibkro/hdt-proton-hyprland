#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
[[ $# == 1 ]] || {
    echo 'Usage: check-native-isolation.sh MANAGED_EVIDENCE_DIRECTORY' >&2
    exit 2
}
fixtures=$(realpath -- "$1")
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/native-isolation-XXXXXXXX")
chmod 700 "$run"
git rev-parse HEAD >"$run/source-head.txt"
git status --porcelain=v1 >"$run/source-status.txt"
for name in native-overlay-observation.json native-overlay-alternate.json; do
    cp -- "$fixtures/$name" "$run/$name"
done
cp -R avalonia/Hdt.Avalonia/bin/Debug/net10.0 "$run/app"
cp -R avalonia/Hdt.Checks/bin/Debug/net10.0 "$run/checks"
build=$(cargo test --locked -j2 --test rust-native-session-isolated --no-run --message-format=json)
executable=$(jq -er 'select(.reason=="compiler-artifact" and .target.name=="rust-native-session-isolated" and .executable!=null) | .executable' <<<"$build")
cargo build --locked -j2
cp -- "$executable" "$run/acceptance"
cp -- "$CARGO_TARGET_DIR/debug/hdt-host" "$run/hdt-host"
(
    cd "$run"
    sha256sum acceptance hdt-host app/Hdt.Avalonia.dll >source-sha256.txt
)
shell=$(readlink -f "$(command -v bash)")
status=0
timeout --kill-after=3s 100 bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh --bind "$run" /capture --chdir /capture \
    --setenv HOME /tmp --setenv HDT_TEST_X11_ISOLATED 1 \
    --setenv HDT_TEST_XVFB "$(readlink -f "$(command -v Xvfb)")" \
    --setenv HDT_TEST_UI_DOTNET "$(readlink -f "$(command -v dotnet)")" \
    --setenv HDT_TEST_UI_XDOTOOL "$(readlink -f "$(command -v xdotool)")" \
    --setenv LD_LIBRARY_PATH "$LD_LIBRARY_PATH" --setenv DOTNET_SYSTEM_GLOBALIZATION_INVARIANT 1 \
    /capture/acceptance --ignored --nocapture --test-threads=1 >"$run/result.txt" 2>&1 || status=$?
cat "$run/result.txt"
for image in "$run"/*.ppm; do
    [[ -f $image ]] && magick "$image" "${image%.ppm}.png"
done
echo "Independent native UI evidence: $run"
exit "$status"
