#!/usr/bin/env bash
# Compatibility entry point; implementation is readable Bun TypeScript.
exec bun "$(dirname -- "${BASH_SOURCE[0]}")/check.ts" "$@"
