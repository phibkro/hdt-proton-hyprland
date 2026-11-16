#!/usr/bin/env bash
# Check all versioned F# source, including tests; restore tools before running.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
files=()
while IFS= read -r -d '' file; do
    files+=("$PWD/$file")
done < <(git ls-files -z -- '*.fs' '*.fsi' '*.fsx')
[[ ${#files[@]} -gt 0 ]] || {
    echo 'No versioned F# sources found.' >&2
    exit 1
}
cd avalonia
dotnet fantomas check "${files[@]}"
