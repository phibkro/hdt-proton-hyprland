#!/usr/bin/env bash
# Repeatable suite evaluation; observations never replace correctness gates.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mode=${1:-suite}
[[ "$mode" == suite || "$mode" == sentinels ]] || {
    echo 'Usage: evaluate.sh [suite|sentinels]' >&2
    exit 2
}
mkdir -p evidence
run=$(mktemp -d "$PWD/evidence/tooling-eval-XXXXXXXX")
chmod 700 "$run"
rows=$run/checks.jsonl
: >"$rows"
overall=0
measure() {
    local name=$1 expected=$2 start finish status
    shift 2
    start=$(date +%s%N)
    if "$@" >"$run/$name.log" 2>&1; then status=0; else status=$?; fi
    finish=$(date +%s%N)
    jq -nc --arg name "$name" --argjson expected "$expected" --argjson actual "$status" \
        --argjson elapsedNS "$((finish - start))" \
        '{name:$name,expectedExit:$expected,actualExit:$actual,passed:($actual==$expected),elapsedMS:($elapsedNS/1000000)}' >>"$rows"
    if ((status != expected)); then
        overall=1
        tail -n 20 "$run/$name.log"
    fi
}
mkdir "$run/sentinel"
cat >"$run/sentinel/good.rs" <<'RUST'
fn main() { println!("{}", 1_i32); }
RUST
cat >"$run/sentinel/bad.rs" <<'RUST'
fn main() { let _: i32 = "type mismatch"; }
RUST
measure rust-positive 0 rustc --error-format=json "$run/sentinel/good.rs" -o "$run/sentinel/good"
measure rust-negative 1 rustc --error-format=json "$run/sentinel/bad.rs" -o "$run/sentinel/bad"
measure rust-diagnostic 0 jq -se 'any(.[];.code.code=="E0308")' "$run/rust-negative.log"
cat >"$run/sentinel/good.sh" <<'SHELL'
#!/bin/sh
printf '%s\n' "$1"
SHELL
cat >"$run/sentinel/bad.sh" <<'SHELL'
#!/bin/sh
printf '%s\n' $1
SHELL
measure shell-positive 0 shellcheck --format=json "$run/sentinel/good.sh"
measure shell-negative 1 shellcheck --format=json "$run/sentinel/bad.sh"
measure shell-diagnostic 0 jq -e 'any(.[];.code==2086)' "$run/shell-negative.log"
if [[ "$mode" == suite ]]; then
    measure rust-static 0 bun scripts/quality.ts check rust-fast
    measure typescript-static 0 bun run lint:ts
    measure typescript-types 0 bun run typecheck
    measure shell-static 0 bun scripts/quality.ts check shell
    measure rust-tests 0 cargo nextest run --locked --config-file nextest.toml --profile ci --test-threads 2
    measure boundary-tests 0 bash scripts/test-unit.sh
fi
jq -s --arg revision "$(git rev-parse HEAD)" --argjson dirty "$(if [[ -z $(git status --porcelain) ]]; then echo false; else echo true; fi)" \
    --arg rust "$(rustc --version)" --argjson passed "$((1 - overall))" \
    '{schema:1,revision:$revision,sourceDirty:$dirty,rust:$rust,passed:($passed==1),checks:.}' "$rows" >"$run/report.json"
cat "$run/report.json"
if [[ -n ${GITHUB_STEP_SUMMARY-} ]]; then
    jq -r '"| Check | Result | Milliseconds |\n|---|---|---|", (.checks[] | "| " + .name + " | " + (.passed|tostring) + " | " + (.elapsedMS|floor|tostring) + " |")' "$run/report.json" >>"$GITHUB_STEP_SUMMARY"
fi
echo "Tooling evaluation evidence: $run/report.json"
exit "$overall"
