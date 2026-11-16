---
name: verify-hdt-proton
description: Verify HDT Proton's native Linux CLI, independent diagnostics, and isolated WPF/X11 behavior; distinguish those results from live Steam/Hyprland game acceptance when reviewing changes.
---

# Verify HDT Proton

Use the [feature map](features/README.md) as the coverage index. The primary
surface is a Windows desktop app with Linux launch/diagnostic commands. Prefer
this repository's real CLI, private X11 acceptance, and WPF replay. A successful
build or synthetic draft does not establish live game tracking.

Workflow derived from pstack's [creation](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/create-verification-skill/SKILL.md)
and [maintenance](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/maintain-verification-skill/SKILL.md)
skills. These links pin the examined instructions; the feature map below is
specific to this repository.

## Launch

From the checkout, enter `devenv shell`. Check machine headroom before builds or
replays. Build once with `CARGO_BUILD_JOBS=2 cargo build --locked`, then initialize
an empty verification session:

```sh
mkdir -p evidence
export HDT_HOST_BINARY="$CARGO_TARGET_DIR/debug/hdt-host"
export HDT_VERIFY_ROOT="$(mktemp -d "$PWD/evidence/verification-XXXXXXXX")"
chmod 700 "$HDT_VERIFY_ROOT"
mkdir "$HDT_VERIFY_ROOT/data" "$HDT_VERIFY_ROOT/home"
```

Use a fresh CLI subprocess for each drive.
Run the doctor below before its first drive and after a surprising result.
For WPF, use the existing `hdt-replay` seed preparation in
[UI-REPLAY.md](../../../docs/UI-REPLAY.md) (resolve repository docs from the
checkout, not this directory). A seed needs original HDT assemblies, native CLR,
an installed Proton and copied Steam runtime; an empty data directory cannot
supply proprietary binaries. Missing prerequisites are an explicit unreachable
route, never an implicit skip. Do not copy account data. Do not modify the outer
replay script while it runs. Its private display and prefix belong to that run.

## Doctor

Record a read-only CLI identity check in the new evidence directory:

```sh
{
  git rev-parse HEAD
  git status --porcelain
  sha256sum "$HDT_HOST_BINARY"
  "$HDT_HOST_BINARY" --help
} >"$HDT_VERIFY_ROOT/doctor.txt" 2>&1
```

Require exit zero and the `hdt-host` command list. Inspect the revision and dirty
state; do not claim a committed proof for dirty product source. CLI readiness
means the selected command works with its own explicit configuration. WPF
readiness additionally requires the replay's `ready main=... overlay=...` marker,
a first state snapshot, and its isolation record; process existence is insufficient.
A live desktop drive needs exact window identities and the running prefix/Proton
from `hdt-status`, not just a healthy CLI. Re-do doctor or reset after any failure.

## Drive

Follow all indexed feature files. The coordinator owns all runtime actions;
source-review agents may read only. Run each mapped entry point or record its
attempt and concrete missing prerequisite. Never replace a live path with a
mock and label it passed. Mutation tests use a new owned data directory; preserve
operator prefixes, services, configurations and sessions. User controls game
entry, picks and matches.

## Evidence

For each drive, keep its command, exit status, stdout/stderr and resulting files.
Record the tested revision/binary hash, dirty state and feature IDs. Verify byte
content, file permissions, exit status and overwrite refusal with a second read.
For UI runs retain `result.json`, source hashes, `transitions.jsonl`, state,
screenshots and supervisor logs in the replay's printed directory. Internal
setters in the synthetic WPF sequence prove that fixture path, not live
HearthMirror, account ratings or game events. Keep failure artifacts too.

Run notes remain private under `$HDT_VERIFY_ROOT`. Mark each route passed,
failed, or verified-unreachable with the actual prerequisite and attempted route.
Do not publish raw logs, environment values, draft choices or account information.

## Cleanup

Wait for each finite CLI and replay process. Stop only the journal PID started by
that drive, send SIGTERM, and wait for its final record. Replay timeout and PID
namespace own their descendants. Never kill by application name or signal a
shared wineserver. Do not stop the operator's service as test cleanup.

Keep `$HDT_VERIFY_ROOT` and replay evidence after teardown. Confirm their doctor,
result and captured files still exist, and no owned child remains. If a failure
strands a process, identify its exact run/PID before cleanup; never start another
drive against an uncertain instance. Do not delete evidence to make a rerun pass.

## Maintain

Apply pstack's maintenance workflow to this directory. Run concurrent read-only
source readers per indexed feature, reconcile their recipes, then drive each
feature serially as coordinator. During that maintenance pass, edit only this
skill directory. Product defects end the pass for triage outside it. Report
`clean`, `changed`, or `blocked`; unreachable routes retain their concrete
prerequisites. Re-run changed harness paths before shipping corrections.
