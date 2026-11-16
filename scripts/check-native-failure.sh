#!/usr/bin/env bash
# An intentional assertion must fail normally and finish its owned resources.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p local
run=$(mktemp -d "$PWD/local/native-failure-XXXXXXXX")
chmod 700 "$run"
status=0
dotnet run --no-build --project avalonia/Hdt.Checks -- \
    --negative-control avalonia/fixtures/draft.json "$run" >"$run/result.txt" 2>&1 || status=$?
cat "$run/result.txt"
[[ $status == 1 ]] || {
    echo "Negative control returned $status; expected 1. Evidence: $run" >&2
    exit 1
}
grep -Fq 'System.InvalidOperationException: INTENTIONAL NEGATIVE CONTROL' "$run/result.txt"
grep -Fq 'CHECK RUNNER CLEANUP: 1 windows closed; 1 actors and 0 writers completed.' "$run/result.txt"
pid=$(cat "$run/negative-control-helper.sh.pid")
[[ $pid =~ ^[1-9][0-9]*$ && ! -e /proc/$pid ]] || {
    echo "Owned negative-control helper was not reaped. Evidence: $run" >&2
    exit 1
}
echo "Negative control passed: assertion returned 1; full diagnostic retained; exact helper reaped. Evidence: $run"
