#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export HS_APP_ID=1234567890
CARGO_BUILD_JOBS=2 cargo build --locked
bun scripts/quality.ts check rust-fast
bash scripts/test-unit.sh
bun scripts/quality.ts check shell
bun run lint:ts
bun run format:ts:check
bun run typecheck
mkdir -p evidence
read -r -a xi_flags <<<"$(pkg-config --cflags --libs x11 xi)"
cc -Wall -Wextra -Werror tests/xinput-race.c -o evidence/xinput-race-check "${xi_flags[@]}"
