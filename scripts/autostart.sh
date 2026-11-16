#!/usr/bin/env bash
set -euo pipefail
# Run inside the project devenv. Config remains authoritative for the Steam ID.
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
window_class=$(bun -e 'import {windowClass} from "./src/runtime/config"; console.log(windowClass)')
if [[ $(bun src/desktop/cli.ts kind) == x11 ]]; then
    bash scripts/hdt.sh build-desktop-helper
fi
: "${XDG_RUNTIME_DIR:?A graphical login is required}"
exec 9>"$XDG_RUNTIME_DIR/hdt-proton-${window_class}.lock"
flock -n 9 || { echo 'HDT automatic startup already has an owner.'; exit 0; }
seen_game=''
echo 'HDT automatic startup is watching for Hearthstone.'
while true; do
    clients=$(bun src/desktop/cli.ts windows)
    game=$(jq -er --arg class "$window_class" '
      [.[] | select(.class == $class and .title == "Hearthstone") | .pid] | unique |
      if length > 1 then error("Multiple Hearthstone processes; refusing to guess")
      elif length == 1 then .[0] else "" end' <<<"$clients")
    if [[ -z "$game" ]]; then
        seen_game=''
    elif [[ "$game" != "$seen_game" ]]; then
        seen_game=$game
        if jq -e --arg class "$window_class" 'any(.[]; .class == $class and .title == "Hearthstone Deck Tracker")' <<<"$clients" >/dev/null; then
            echo 'Hearthstone detected; existing HDT left running.'
        else
            echo 'Hearthstone detected; starting the compatibility launcher.'
            # Do not hold the monitor lock in its children. The live launcher
            # keeps custody of its own input bridge and Proton namespace.
            hdt-overlay-launch 9>&- &
        fi
    fi
    sleep 2
done
