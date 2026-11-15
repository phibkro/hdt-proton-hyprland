# Publish the tested HDT compatibility profile

Goal: publish the original launcher, plugin, compositor integration and diagnostics as a reusable public GitHub repository, preserving the operator's running game and HDT.

Constraints: no installer/assemblies, extracted app, account/game data, raw evidence, prefix copies, screenshots or third-party source snapshots in Git. Keep the local recovery material intact and ignored. Put the Steam shortcut ID and installation root behind one configuration boundary. Preserve current behavior for the tested local configuration. Do not restart or manipulate the running game for publication.

Acceptance: a clean checkout can evaluate its pinned devenv, validate configuration, bundle the TypeScript entry points and compile the read-only native probe. The installed plugin's real compile/click/visual observations are documented with their limited scope; no claim of an independent clean-prefix runtime test. Only allowlisted source/documentation appears in the first commit. Public remote main must resolve to the reviewed local commit. Operator authorized a public repository on 2026-11-15.

Not acceptance: universal Wayland support, every HDT button, unattended .NET installation, automatic HDT updating, Arena match saving, live draft offers/picks or ArenaSmith ratings. These are separate tests, not inferred from visual success.

Revision 2026-11-15: publication requires extracting local app ID/path configuration; the game session stays running. Existing tested subprocess mechanisms remain unchanged under the documented temporary engineering exception.
