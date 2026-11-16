#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export HS_APP_ID=1234567890
bash scripts/test-unit.sh
shellcheck autostart.sh scripts/*.sh
bun build src/runtime/launch.ts src/hyprland/control-input.ts src/runtime/status.ts \
    src/hyprland/overlay-rule.ts src/diagnostics/surface-check.ts \
    src/runtime/install-plugin.ts src/runtime/configure-overlay.ts src/diagnostics/trace-export.ts src/diagnostics/input-journal.ts \
    --target=bun --outdir=evidence/check-build
read -r -a x11_flags <<< "$(pkg-config --cflags --libs x11)"
cc -Wall -Wextra -Werror src/hyprland/overlay-link.c -o evidence/overlay-link-check "${x11_flags[@]}"
hdt-build-surface-probe
