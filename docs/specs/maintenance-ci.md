# Keep the installed fix and check future changes

2026-11-15. The operator authorized the maintenance follow-ups after the Arena fix was merged.

## Contract

Update the installed Nix package and the existing homelab HDT pin to the tested merge revision. Preserve unrelated dirty homelab work, the running game, the working plugin, and rollback generations. Do not activate the full machine configuration or restart the current tracker for a package-source update.

Add GitHub Actions checks for the existing `nix flake check` contract. Reuse the homelab's checkout and Nix-install actions, pin their verified commits, use read-only permissions, and bound runner time. Check workflow syntax locally and in the Nix checks. Keep Microsoft/HDT binaries and account data outside CI.

Prepare a sanitized upstream report that separates the isolated particle-path reproduction, the live observations, and the unknown graphics-library cause. Do not file the report or attach raw private logs automatically.

## Acceptance

- The user profile's HDT package contains the particle workaround, and other profile entries remain intact.
- The canonical homelab module and its existing PR select the tested merge commit without including unrelated changes.
- Workflow lint and Nix package/tests pass; a real GitHub Actions run is observed after publication.
- Running Hearthstone and HDT process identities remain unchanged during package maintenance.
- Fresh-draft and longer-session verification remain operator-driven and explicitly pending.
