#!/usr/bin/env bash
# Aquamarine v0.15.1 Wayland backend binds these exact minimum versions:
# https://github.com/hyprwm/aquamarine/blob/v0.15.1/src/backend/Wayland.cpp
set -euo pipefail
[[ $# == 1 && -r $1 ]] || {
    echo 'Usage: wayland-parent-check.sh WAYLAND_INFO_OUTPUT' >&2
    exit 2
}
report=$(jq -Rn '
    [inputs | select(startswith("interface:"))] as $records |
    [$records[] |
        [capture("^interface: '\''(?<interface>[a-zA-Z0-9_]+)'\'',\\s*version:\\s*(?<version>[0-9]+),\\s*name:\\s*[0-9]+$")] |
        if length==1 then .[0] | .version |= tonumber else null end] as $parsed |
    ($parsed | map(select(.!=null))) as $globals |
    ($parsed | map(select(.==null)) | length) as $parseErrors |
    {wl_seat:9, wl_compositor:6, xdg_wm_base:6, wl_shm:1, zwp_linux_dmabuf_v1:4} |
    to_entries | map(. as $requirement |
        ([$globals[] | select(.interface==$requirement.key) | .version] | max) as $observed |
        {interface:.key, required:.value, observed:$observed,
         passed:($observed!=null and $observed>=.value)}) |
    {schema:1, backend:"aquamarine-0.15.1-wayland", requirements:.,
     malformedRecords:$parseErrors, passed:($parseErrors==0 and all(.[];.passed))}
' <"$1")
printf '%s\n' "$report"
jq -e '.passed' <<<"$report" >/dev/null
