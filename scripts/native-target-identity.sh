#!/usr/bin/env bash
# Linux process identity checked again while GDB has the inferior stopped.
set -euo pipefail
[[ $# == 2 || $# == 3 ]]
mode=$1 pid=$2
case "$mode:$#" in
    snapshot:2 | verify:3) ;;
    *) exit 2 ;;
esac
[[ $pid =~ ^[1-9][0-9]{0,9}$ ]]
snapshot() {
    local before after tail executable inode comm
    local -a fields
    before=$(cat "/proc/$pid/stat")
    tail=${before##*) }
    read -r -a fields <<<"$tail"
    [[ ${fields[19]} =~ ^[0-9]+$ ]]
    executable=$(readlink "/proc/$pid/exe")
    inode=$(stat -Lc '%d:%i' "/proc/$pid/exe")
    comm=$(cat "/proc/$pid/comm")
    [[ $comm == HearthstoneDeck ]] || {
        echo 'Target is not HDT.' >&2
        return 1
    }
    case "${executable##*/}" in
        wine64-preloader | wine-preloader | wine64 | wine) ;;
        *)
            echo 'Expected the native Wine executable for HDT.' >&2
            return 1
            ;;
    esac
    after=$(cat "/proc/$pid/stat")
    tail=${after##*) }
    local -a after_fields
    read -r -a after_fields <<<"$tail"
    [[ ${fields[19]} == "${after_fields[19]}" ]]
    printf '%s\n' "$pid" "${fields[19]}" "$inode" "$executable" "$comm"
}
case "$mode" in
    snapshot) snapshot ;;
    verify)
        current=$(snapshot)
        [[ $(awk '/^TracerPid:/ {print $2}' "/proc/$pid/status") != 0 ]]
        [[ $current == "$(cat -- "$3")" ]] || {
            echo 'Attached process identity differs from the selected process.' >&2
            exit 1
        }
        ;;
    *) exit 2 ;;
esac
