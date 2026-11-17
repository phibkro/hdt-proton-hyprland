#!/usr/bin/env bash
# Compare screen pixels, independently of WPF's declared visibility.
set -euo pipefail
[[ $# == 4 ]] || {
    echo 'Usage: visual-triplet.sh BEFORE SHOWN AFTER NEW_OUTPUT_DIR' >&2
    exit 2
}
before=$1 shown=$2 after=$3 output=$4
mkdir -m 700 -- "$output"
dimensions=$(magick identify -format '%wx%h' "$before")
[[ $(magick identify -format '%wx%h' "$shown") == "$dimensions" && $(magick identify -format '%wx%h' "$after") == "$dimensions" ]]
pixels=$(magick identify -format '%[fx:w*h]' "$before")
compare() {
    local label=$1 image=$2 status=0
    magick compare -metric PDC -fuzz 2% "$before" "$image" "$output/$label-diff.png" 2>"$output/$label-count.txt" || status=$?
    # ImageMagick returns 1 for different images; errors must never pass.
    ((status <= 1)) || return "$status"
}
compare shown "$shown"
compare cleared "$after"
jq -n --argjson pixels "$pixels" \
    --rawfile shown "$output/shown-count.txt" \
    --rawfile remaining "$output/cleared-count.txt" \
    '($shown|split(" ")[0]|tonumber) as $shown | ($remaining|split(" ")[0]|tonumber) as $remaining | {schema:1,pixels:$pixels,colorDistanceTolerancePercent:2,minimumShownFraction:0.01,maximumRemainingFraction:0.001,shownFraction:($shown/$pixels),remainingFraction:($remaining/$pixels),passed:($shown/$pixels>=0.01 and $remaining/$pixels<=0.001)}' >"$output/result.json"
jq -e '.passed' "$output/result.json" >/dev/null
