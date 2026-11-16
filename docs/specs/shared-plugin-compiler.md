# Shared plugin source compiler compatibility

## Outcome

The original plugin compiler and the modern native acceptance build must consume
one implementation of the shared publisher and Arena event bridge. A native
preview change must not prevent the existing WPF plugin from compiling.

## Boundary

The installed plugin and private WPF replay use the prefix's Framework compiler;
modern checks use the pinned .NET SDK with nullable analysis enabled. Unknown
preprocessor directives are rejected by the Framework compiler even inside an
inactive conditional. Preserve both compiler routes and all runtime behavior.
Use legacy-supported, file-local warning pragmas only for nullable diagnostics
observed in these shared C# 5 sources. Preserve nullable analysis elsewhere and
do not create manually maintained copies or broaden global warning suppression.

## Acceptance

- The actual Framework compiler builds every plugin source in the private WPF
  replay and its existing recorder/Battlegrounds assertions complete.
- Modern ordinary and optional original-watcher acceptance builds succeed with
  warnings as errors; shared publisher/event tests continue to pass.
- Removing the repair reproduces the Framework compiler error at the original
  unknown directive. Source, prefix and proprietary binaries remain unchanged
  outside project-owned test copies.
- Normal source checks and hooks pass. No live game interaction is required.

## Revisions

- 2026-11-17: Initial contract after combined preview replay exposed CS1024.
