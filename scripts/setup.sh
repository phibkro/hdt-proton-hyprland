#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
if (($#)); then
    echo 'Usage: hdt-setup (inside devenv; Battle.net running through Steam, HDT initialized once)' >&2
    exit 2
fi
# Compile first: a missing CLR or assembly must not close the working tracker.
hdt-launch --build-plugin
bun src/runtime/install-plugin.ts
bun src/runtime/configure-overlay.ts
printf '%s\n' 'Compatibility setup complete. Start HDT with: devenv shell -- hdt-overlay-launch' \
    'For automatic startup through your existing desktop service, run: devenv shell -- hdt-steam-options'
