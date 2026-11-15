# Bun runtime exception

## Scope and owner

This temporary exception covers the existing Bun command-line supervisor, diagnostics, and configuration extraction. The repository maintainer owns the exception. It was recorded on 2026-11-15.

The house default is native Effect. At the recorded lookup, the project had no installed Effect package, guidance, house overlay, or application scaffold.

The tested launcher uses Bun's process API to enter the running Steam server's user and mount namespaces. It also supervises the compositor bridge. Publication retained that implementation during a working desktop session.

This decision does not claim that Effect lacks process or filesystem capabilities. A rewrite would first examine native Effect services and platform services. Wrapping the existing promises alone would not redesign process supervision.

## Verification

The recorded checks cover configuration rejection, TypeScript bundles from a clean checkout, native probe compilation, and plugin compilation against the installed HDT and .NET assemblies. Desktop observations are recorded separately in [Observed compatibility](COMPATIBILITY.md).

Bun 1.4.4 was the observed runtime. `devenv.lock` pins the toolchain. `scripts/check.sh` selects test files explicitly because the private prefix backups contain unrelated files. An earlier unbounded test run was stopped.

C# is required by HDT's .NET Framework plugin interface. The C probes use Xlib, XComposite, and XRender. The repository ships no Python source.

## Reopening and retirement

A change to process supervision, configuration ownership, or runtime dependencies reopens this exception. Review then uses the installed Effect guidance for that version.

A native Effect rewrite that preserves the verified namespace and process behavior retires this exception.

## Stacking change recorded on 2026-11-15

The lookup again found no installed Effect package, guidance, or house overlay. The existing bridge gained one bounded Xlib command without a new process-supervision model or configuration owner.

`src/hyprland/overlay-link.c` changes the overlay's transient parent after identity checks. It verifies the result and supports guarded restoration. The pixel probe remains read-only.

Verification covered compilation with warnings as errors, real relationship readback, and user checks for clicks, hover previews, and Alt-Tab. This change retained the Bun exception.

## Source organization and setup recorded on 2026-11-15

Source relocation retained the existing Bun process boundaries. The setup script composes the existing Bash and devenv commands. The Steam wrapper requests the existing systemd service and executes Steam's original arguments.

No new TypeScript runtime layer or dependency was added. The plugin installer gained support for an already-stopped tracker. It refuses writes while an accessible tracker process remains.

Subprocess tests cover argument handling and exit behavior. The [compatibility record](COMPATIBILITY.md#setup-and-startup-results) distinguishes those tests from actual desktop activation.
