#!/usr/bin/env bash
set -euo pipefail
# Run inside the project devenv. Config remains authoritative for the Steam ID.
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
window_class=$(bash scripts/native-host.sh config windowClass)
: "${XDG_RUNTIME_DIR:?A graphical login is required}"
exec 9>"$XDG_RUNTIME_DIR/hdt-proton-${window_class}.lock"
lock_status=0
flock -n 9 || lock_status=$?
case "$lock_status" in
    0) ;;
    1)
        echo 'HDT automatic startup already has an owner.'
        exit 0
        ;;
    *)
        echo "Could not acquire the HDT monitor lock (status $lock_status)." >&2
        exit "$lock_status"
        ;;
esac
seen_game=''
launcher_pid=''
launched_game=''
launched_at=0
failures=0
next_attempt=0
paused=false
tracker_alive() {
    local pid name key value state
    while IFS= read -r pid; do
        [[ "$pid" =~ ^[1-9][0-9]*$ && -r "/proc/$pid/comm" ]] || continue
        name=$(
            exec 9>&-
            cat "/proc/$pid/comm" 2>/dev/null
        ) || continue
        [[ "$name" == HearthstoneDeck ]] || continue
        # A compositor entry can outlive its process. Zombies are not trackers.
        state=''
        while read -r key value _; do
            if [[ "$key" == State: ]]; then state=$value; fi
        done 2>/dev/null <"/proc/$pid/status" || continue
        case "$state" in '' | Z | X) continue ;; *) return 0 ;; esac
    done < <(
        exec 9>&-
        jq -r --arg class "$window_class" '.[] | select(.class==$class and .title=="Hearthstone Deck Tracker") | .pid' <<<"$clients"
    )
    return 1
}
echo 'HDT automatic startup is watching for Hearthstone.'
while true; do
    clients=$(
        exec 9>&-
        hyprctl -j clients
    )
    game=$(
        exec 9>&-
        jq -er --arg class "$window_class" '
      [.[] | select(.class == $class and .title == "Hearthstone") | .pid |
       if type != "number" or . <= 0 or . != floor then error("Invalid Hearthstone PID") else . end] | unique |
      if length > 1 then error("Multiple Hearthstone processes; refusing to guess")
      elif length == 1 then .[0] else "" end' <<<"$clients"
    )
    now=$(
        exec 9>&-
        date +%s
    )
    if [[ "$game" != "$seen_game" ]]; then
        seen_game=$game
        failures=0
        next_attempt=0
        paused=false
    fi
    if [[ -n "$launcher_pid" ]]; then
        # Bash's job table retains ownership even if the OS reuses an exited PID.
        if [[ $(
            exec 9>&-
            jobs -pr
            jobs -ps
        ) != "$launcher_pid" ]]; then
            status=0
            wait "$launcher_pid" || status=$?
            launcher_pid=''
            if [[ "$launched_game" == "$game" && -n "$game" ]]; then
                if ((status == 0)); then
                    paused=true
                    echo 'HDT launcher exited successfully; automatic recovery paused until the game restarts or the tracker service is restarted.'
                else
                    failures=$((failures + 1))
                    if ((failures >= 3)); then
                        paused=true
                        echo 'HDT failed three times; automatic recovery paused. Restart the tracker service to retry.'
                    else
                        delay=$((5 * failures))
                        next_attempt=$((now + delay))
                        echo "HDT launcher failed (status $status); retrying in at least $delay seconds."
                    fi
                fi
            fi
        elif ((now - launched_at >= 60)); then
            failures=0
        fi
    fi
    if [[ -n "$game" && -z "$launcher_pid" && "$paused" == false ]] && ((now >= next_attempt)); then
        if tracker_alive; then
            paused=true
            echo 'Existing HDT left running; its exit status belongs to its original launcher.'
        else
            echo 'Hearthstone detected; starting the compatibility launcher.'
            # Do not hold the monitor lock in its children. The live launcher
            # keeps custody of its own input bridge and Proton namespace.
            hdt-overlay-launch 9>&- &
            launcher_pid=$!
            launched_game=$game
            launched_at=$now
        fi
    fi
    sleep 2 9>&-
done
