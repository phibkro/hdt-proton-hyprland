#!/usr/bin/env bash
set -euo pipefail
if (( $# )); then echo 'Usage: hdt-steam-options' >&2; exit 2; fi
if [[ ${HDT_INSTALLED:-0} == 1 ]]; then
    printf '%s\n' 'hdt-proton steam-run %command%'
    exit 0
fi
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
# POSIX single quoting survives Steam's shell, including spaces and apostrophes.
wrapper=${root}/scripts/steam-launch.sh
quoted=${wrapper//\'/\'\\\'\'}
printf "bash '%s' %%command%%\n" "$quoted"
