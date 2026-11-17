# Verify and merge a pull request

Use this workflow for HDT integration changes. Its project-specific journeys live
in [.cursor/skills/verify-hdt-proton](../../.cursor/skills/verify-hdt-proton/SKILL.md).
Static analysis, source review, isolated runtime tests and live game acceptance
provide different evidence. Keep their results separate.

## Install the review skills

The project skill was generated and maintained using pstack's
[create-verification-skill](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/create-verification-skill/SKILL.md)
and [maintain-verification-skill](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/maintain-verification-skill/SKILL.md).
On the tested workstation both are installed under `~/.codex/skills/`. They become
available on the next agent turn. Other contributors can reproduce that install
with Codex's bundled installer:

```sh
python3 "${CODEX_HOME:-$HOME/.codex}/skills/.system/skill-installer/scripts/install-skill-from-github.py" \
  --repo cursor/plugins \
  --ref bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7 \
  --path pstack/skills/create-verification-skill pstack/skills/maintain-verification-skill
```

The installer refuses existing destinations. Review local changes before an
upgrade; do not overwrite them automatically. No global installation is needed
to read the checked-in project skill by path.

## Review the exact proposed change

1. Read the PR head, base, diff, existing review threads and CI results from GitHub.
   Match the local checkout's commit to the PR head. Preserve operator edits in
   other checkouts.
2. Read the project verification skill and its feature index. Use the creation
   skill only when a project skill is missing or a genuinely new surface needs
   its launch/doctor/drive/evidence/cleanup model.
3. Apply the maintenance skill: one concurrent read-only source reader per
   feature; the coordinator reconciles recipes and owns all runtime driving.
   Readers return source entry points, likely drift and one practical recipe.
   They do not launch apps or edit product files.
4. Run the doctor, then drive every indexed feature using the skill. Start a
   fresh CLI session per drive. UI replays own one isolated display/prefix each.
   Recheck health after failures and preserve evidence through cleanup.
5. Classify each entry point as passed, failed, or verified-unreachable. An
   unreachable result names the attempted route and concrete missing prerequisite,
   such as a running Steam game session. It is not a pass through a substitute.
6. During maintenance, edit only the project skill directory. For a product or
   shared-harness defect, stop that pass, retain its evidence, fix the defect in
   a separate development phase, and resume verification at the new commit.
   Re-drive corrected harness paths before publishing them.

Record private run notes with feature coverage and `clean`, `changed`, or
`blocked`. Do not commit raw account logs or screenshots. A changed map needs
proof of its corrected paths; a blocked pass does not authorize a merge.

## Required checks

Pre-commit runs fast staged checks. GitHub classifies the complete base-to-head
change using [ci-scope.sh](../../scripts/ci-scope.sh), which owns the documentation
allowlist. Documentation-only changes skip builds and source analysis. Every
other change requires successful source-quality, rust-quality and nix-check jobs.
Manual runs and changes without a comparable base run all checks. An unreadable
comparison fails the gate; it never grants a documentation exemption.

The aggregate `check` job always runs. It requires successful classification and
either all three successful check jobs or an explicit documentation-only result
with all three jobs skipped. A documentation commit at the tip of a code PR does
not make that PR documentation-only. Rename detection is disabled so a move from
production code into documentation still includes the deleted production path.
This uses [job conditions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions)
rather than workflow path filters, which can leave required checks pending.
See [tooling](tooling.md) for the maintained command definitions. Check the
statuses for the current PR head, not a previous successful revision.

For runtime changes, retain the relevant native X11 and real WPF replay results.
The canonical publisher replay and flight-recorder replay are separate journeys:
`--canonical-events --diagnostics` is rejected so recorder checks cannot be
silently bypassed. The synthetic Arena replay does not prove HearthMirror picks,
ArenaSmith entitlement or ordinary Power.log match consumption.

The Rust launcher migration additionally requires actual same-session
Steam/Proton discovery and Hyprland overlay/input acceptance before merge. Until
that route is available, keep the PR draft and keep the working installed package.
The user opens the game, enters drafts, picks cards and starts matches; review
automation does not perform those actions for them.

## Merge on pass

Resolve source-review issues, required CI and the applicable runtime gates. If a
fix moves the head, repeat affected runtime checks and wait for CI on that head.
Read the final committed diff before merging. Mark the PR ready only after its
remaining draft blockers are resolved.

Use a squash merge with the expected head SHA; reject a moved head rather than
merging an unreviewed revision. Then verify the returned merge commit against
remote `main` and confirm the PR is merged. Apply the repository's branch cleanup
policy only after preserving evidence and proving integration. Do not reset dirty
operator checkouts, delete investigation branches or activate a new desktop
service as part of PR cleanup.

## Automate visual acceptance

Use the real WPF replay for screen-pixel assertions, not a heartbeat as a proxy
for rendering. The [replay guide](../guides/ui-replay.md) documents the executable
Settings/popup comparisons and their negative controls. The classifier runs in
CI without HDT; the real rendering journey requires the private HDT/CLR seed.
A green classifier job does not substitute for that journey.

| Visual behavior | Current automated evidence | Remaining boundary |
| --- | --- | --- |
| Settings appears and clears | Native opening input, WPF state, before/shown/after screen crops | Progressive animation quality and real compositor |
| Transparent popup clears | Actual screen-pixel return to baseline | Real card previews and other popup classes |
| Empty space clicks through; control receives input | Separate private X11 input journey and actual click counters | Hyprland-specific control gating |
| Overlay follows game movement/resizing | Human desktop acceptance | Automate against a window-only fixture under nested Hyprland |
| Overlay survives Alt-Tab and game clicks | Human desktop acceptance | Composed-frame assertions under nested Hyprland |

For the compositor journeys, own a nested compositor and its display/input
sockets, drive a window-only game fixture and the real HDT overlay, and observe
both composed pixels and delivered input. Preserve failing frames and timing.
Do not drive the operator's active game or treat Openbox/Xvfb as Hyprland proof.
Until those journeys exist and pass, keep their manual gates explicit.
