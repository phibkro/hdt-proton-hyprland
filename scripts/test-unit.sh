#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export HS_APP_ID=1234567890
exec bun test ./tests/*.test.ts
