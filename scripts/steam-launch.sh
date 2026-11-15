#!/usr/bin/env bash
# Steam Launch Options: bash "/absolute/checkout/scripts/steam-launch.sh" %command%
# Start the desktop-owned watcher; Steam's expanded command remains authoritative.
set -u
if (( $# == 0 )); then
    echo 'Use this script in the Battle.net Steam shortcut Launch Options, followed by %command%.' >&2
    exit 2
fi
if ! systemctl --user start hdt-proton-autostart.service; then
    echo 'HDT startup service unavailable. Install the desktop service described in docs/INSTALL.md; launching the game normally.' >&2
fi
exec "$@"
