#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/rust-x11-XXXXXXXX")
chmod 700 "$run"
build=$(CARGO_BUILD_JOBS=2 cargo test --locked --test rust-x11-isolated --no-run --message-format=json)
executable=$(jq -er 'select(.reason=="compiler-artifact" and .target.name=="rust-x11-isolated" and .executable!=null) | .executable' <<<"$build")
CARGO_BUILD_JOBS=2 cargo build --locked
cp "$executable" "$run/acceptance"
cp "${CARGO_TARGET_DIR:-target}/debug/hdt-host" "$run/hdt-host"
read -r -a flags <<<"$(pkg-config --cflags --libs x11 xcomposite xrender)"
cc -Wall -Wextra -Werror tests/reference/x-surface-probe.c -o "$run/c-surface" "${flags[@]}"
(
    cd "$run"
    sha256sum acceptance hdt-host c-surface >source-sha256.txt
)
xvfb=$(readlink -f "$(command -v Xvfb)")
shell=$(readlink -f "$(command -v bash)")
timeout --kill-after=3s 30 bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh \
    --bind "$run" /capture --chdir /capture --setenv HOME /tmp \
    --setenv HDT_TEST_X11_ISOLATED 1 --setenv HDT_TEST_XVFB "$xvfb" \
    --setenv HDT_TEST_HOST_BINARY /capture/hdt-host --setenv HDT_TEST_C_SURFACE /capture/c-surface \
    /capture/acceptance --ignored --nocapture --test-threads=1 >"$run/result.txt" 2>&1
cat "$run/result.txt"
echo "Rust X11 acceptance passed. Evidence: $run"
