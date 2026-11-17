#!/usr/bin/env bash
# Verify the diagnostics boundary without starting licensed tooling or a game.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p local
run=$(mktemp -d "$PWD/local/native-tooling-XXXXXXXX")
dotnet restore avalonia/Hdt.Development/Hdt.Development.csproj --locked-mode
dotnet build avalonia/Hdt.Development/Hdt.Development.csproj --no-restore -m:2 -p:UseSharedCompilation=false
dotnet format whitespace avalonia/Hdt.Development/Hdt.Development.csproj --no-restore --verify-no-changes
for project in Hdt.Avalonia Hdt.Checks; do
    output=$(dotnet msbuild "avalonia/$project/$project.csproj" -getProperty:TargetDir)
    jq -e '.libraries | keys | any(startswith("AvaloniaUI.DiagnosticsSupport/")) | not' "$output/$project.deps.json" >/dev/null
    diagnostics=$(rg --files --hidden --no-ignore -g 'AvaloniaUI.DiagnosticsSupport*' "$output") || [[ $? == 1 ]]
    [[ -z $diagnostics ]] || {
        echo "Diagnostics leaked into $project output." >&2
        exit 1
    }
done
status=0
dotnet build avalonia/Hdt.Development/Hdt.Development.csproj --no-restore -c Release -m:2 -p:UseSharedCompilation=false >"$run/release-refusal.log" 2>&1 || status=$?
[[ $status == 1 ]] && rg -q 'Debug-only diagnostics host' "$run/release-refusal.log"
status=0
env -u AVALONIA_TOOLS_LICENSE_KEY bash avalonia/dev.sh mcp >"$run/license-refusal.log" 2>&1 || status=$?
[[ $status == 2 ]] && rg -q 'requires a paid Avalonia tools license' "$run/license-refusal.log"
dotnet watch --project avalonia/Hdt.Development/Hdt.Development.csproj --list >"$run/watch-files.txt"
rg -q '/Hdt.Views/ArenaChoices.fs$' "$run/watch-files.txt"
rg -q '/Hdt.Views/SettingsView.axaml$' "$run/watch-files.txt"
echo "Development host build, ordinary dependency exclusion, Release refusal, license guard and F#/AXAML watch discovery passed. Evidence: $run"
