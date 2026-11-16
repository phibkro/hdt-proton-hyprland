# Documentation

Start with [installation](guides/installation.md), or use the
[Nix guide](guides/nix.md) to install the launcher and desktop integration.
The [compatibility record](reference/compatibility.md) distinguishes observed
results from untested configurations.

| Directory | Contents |
|---|---|
| [guides](guides/installation.md) | Installation, operation, diagnostics and runtime testing |
| [reference](reference/commands.md) | Commands, configuration, architecture and compatibility evidence |
| [development](development/README.md) | Building, language tooling, test strategy and PR review |
| [investigations](investigations/upstream-report.md) | Reproductions and reports that do not establish a production fix |
| [specs](specs/publish-compatibility.md) | Design contracts and historical implementation plans |

For a freeze, follow [evidence capture](guides/freeze-capture.md). For account-free
UI checks, use the [isolated replay guide](guides/ui-replay.md). Contributors
should start with the [development guide](development/README.md) and
[test strategy](development/testing-strategy.md).

The [HDT view trees](reference/wpf-view-map.md) map the original windows,
settings categories and migration process boundaries to reference capture.
