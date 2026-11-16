#!/usr/bin/env bash
# Compatibility entry point for existing Home Manager service declarations.
exec bun "$(dirname -- "${BASH_SOURCE[0]}")/scripts/autostart.ts" "$@"
