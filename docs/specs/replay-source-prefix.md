# Canonical source prefix for private replay

## Outcome

Preparing a replay from a Steam prefix reached through symlinks must produce
an owned environment that can launch. Preserve the source prefix and the
operator's running applications. The prepared manifest identifies the actual
source directory that the replay masks from its private runtime.

## Boundary and constraints

Resolve the configured source prefix to an existing canonical directory before
checking native CLR prerequisites, copying files or recording ownership. Reuse
the existing `realpath` dependency and preparation flow; application-directory
resolution already uses it. Do not change the caller's configuration, prefix,
registry, service or game. Missing or cyclic source paths fail preparation.

Reuse preparation still verifies the selected CLR, application and runtime.
Copied proprietary files and raw runtime evidence remain private. No new
application assets, account data or dependency is introduced.

## Acceptance

- An owned Steam-layout alias to an already sanitized test prefix is accepted
  by real `prepare --reuse`; its manifest contains the canonical source path,
  not the alias. Source CLR/application hashes remain unchanged.
- A real private WPF replay proceeds past isolation setup and completes its
  existing transition/recorder assertions. Retain clean source identity,
  manifest, exit status and cleanup evidence.
- A missing or cyclic prefix fails before a reusable environment is declared.
- Existing CLI argument guards, shell checks and normal commit hooks pass.

The observed pre-fix falsifier is a successful preparation that stores a
symlink, followed by Bubblewrap rejecting that destination before HDT starts.
This repairs preparation, not WPF rendering or live game compatibility.

## Revisions

- 2026-11-17: Initial contract after the combined verification run exposed the
  source-prefix alias at the isolation boundary.
