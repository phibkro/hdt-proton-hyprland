#!/usr/bin/env bash
set -euo pipefail

[[ $# == 2 ]] || {
    echo 'Usage: ci-scope.sh BASE_SHA HEAD_SHA' >&2
    exit 2
}
# Manual runs and a new branch have no comparable base: run every check.
if [[ -z "$1" || "$1" =~ ^0+$ ]]; then
    echo false
    exit 0
fi
base=$(git rev-parse --verify --end-of-options "$1^{commit}")
head=$(git rev-parse --verify --end-of-options "$2^{commit}")
changes=$(mktemp)
trap 'rm -f -- "$changes"' EXIT
# Disable rename detection so moving production code into docs cannot hide its deletion.
git diff --no-renames --name-only -z "$base" "$head" -- >"$changes"
docs_only=false
while IFS= read -r -d '' file; do
    case "$file" in
        README.md | docs/*.md | .cursor/skills/verify-hdt-proton/*.md) docs_only=true ;;
        *)
            echo false
            exit 0
            ;;
    esac
done <"$changes"
echo "$docs_only"
