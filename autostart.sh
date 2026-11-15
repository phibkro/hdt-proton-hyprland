#!/usr/bin/env bash
# Compatibility entry point for existing Home Manager service declarations.
set -euo pipefail
exec bash "$(dirname -- "${BASH_SOURCE[0]}")/scripts/autostart.sh" "$@"
