#!/usr/bin/env bash
# Run inside this directory's pinned shell; all persistent state stays in local/.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
command=${1:-help}
[[ $# -eq 0 ]] || shift
case "$command" in
    setup)
        dotnet tool restore
        dotnet restore Hdt.Checks/Hdt.Checks.csproj --locked-mode
        dotnet restore Hdt.Development/Hdt.Development.csproj --locked-mode
        mkdir -p .vscode
        temporary=$(mktemp .vscode/.mcp-XXXXXXXX)
        trap 'rm -f -- "$temporary"' EXIT
        jq '{servers: (.mcpServers | with_entries(.value.type = "http"))}' .mcp.json >"$temporary"
        if [[ -e .vscode/mcp.json ]] && ! cmp -s -- "$temporary" .vscode/mcp.json; then
            echo 'Preserving existing .vscode/mcp.json; reconcile it with .mcp.json before setup.' >&2
            exit 2
        fi
        mv -- "$temporary" .vscode/mcp.json
        mkdir -p .codex
        temporary=$(mktemp .codex/.config-XXXXXXXX)
        printf '# Generated from .mcp.json by dev.sh setup.\n[mcp_servers.avalonia-docs]\nurl = %s\n' \
            "$(jq -c '.mcpServers["avalonia-docs"].url' .mcp.json)" >"$temporary"
        if [[ -e .codex/config.toml ]] && ! cmp -s -- "$temporary" .codex/config.toml; then
            echo 'Preserving existing .codex/config.toml; reconcile its MCP declaration before setup.' >&2
            exit 2
        fi
        mv -- "$temporary" .codex/config.toml
        ;;
    build)
        dotnet build Hdt.Development/Hdt.Development.csproj --no-restore -m:2 -p:UseSharedCompilation=false
        ;;
    run)
        app=$(dotnet msbuild Hdt.Development/Hdt.Development.csproj -getProperty:TargetPath)
        [[ -f $app ]] || {
            echo 'Run dev.sh build first.' >&2
            exit 1
        }
        mkdir -p ../local/native-development
        exec dotnet "$app" --replay "$PWD/fixtures/draft.json" --settings "$PWD/../local/native-development/settings.json" "$@"
        ;;
    tools)
        exec dotnet avdt "$@"
        ;;
    mcp)
        [[ -n ${AVALONIA_TOOLS_LICENSE_KEY:-} ]] || {
            echo 'DevTools MCP requires a paid Avalonia tools license supplied through the local environment. The free documentation MCP remains available.' >&2
            exit 2
        }
        exec dotnet avdt mcp "$@"
        ;;
    watch)
        # F# edits use full restart. Licensed XAML/C# hot reload is not enabled.
        mkdir -p ../local/native-development
        exec dotnet watch --project Hdt.Development/Hdt.Development.csproj --no-hot-reload --non-interactive run -- --replay "$PWD/fixtures/draft.json" --settings "$PWD/../local/native-development/settings.json" "$@"
        ;;
    help)
        echo 'Inside nix-shell avalonia/shell.nix: bash avalonia/dev.sh setup|build|run|tools|mcp|watch'
        ;;
    *)
        echo "Unknown native development command: $command" >&2
        exit 2
        ;;
esac
