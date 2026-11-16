#!/usr/bin/env bash
# Preserve the entrypoint directory when an existing launcher uses a symlink.
HDT_STEAM_WRAPPER="$(dirname -- "${BASH_SOURCE[0]}")/steam-launch.sh" exec bun "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/steam-options.ts" "$@"
