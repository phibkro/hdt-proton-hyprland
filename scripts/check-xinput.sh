#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/xinput-race-XXXXXXXX")
chmod 700 "$run"
cp tests/xinput-race.c tests/xinput-race.ts "$run/"
read -r -a flags <<<"$(pkg-config --cflags --libs x11 xi)"
cc -Wall -Wextra -Werror "$run/xinput-race.c" -o "$run/xinput-race" "${flags[@]}"
(
    cd "$run"
    sha256sum xinput-race.c xinput-race.ts xinput-race >source-sha256.txt
)
bun=$(readlink -f "$(command -v bun)")
xvfb=$(readlink -f "$(command -v Xvfb)")
shell=$(readlink -f "$(command -v bash)")
# The host's X sockets, Wayland sockets, game prefix, and credentials are absent.
# All device creation/removal happens on the server owned by this test.
bwrap --unshare-all --die-with-parent --new-session --clearenv \
    --ro-bind /nix/store /nix/store --proc /proc --dev /dev --tmpfs /tmp \
    --dir /bin --ro-bind "$shell" /bin/sh \
    --bind "$run" /capture --chdir /capture --setenv HOME /tmp \
    "$bun" /capture/xinput-race.ts /capture "$xvfb"
echo "Private XInput reproduction passed. Evidence: $run/results"
