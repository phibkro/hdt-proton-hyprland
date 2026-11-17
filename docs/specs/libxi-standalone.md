# Standalone libXi correction

## Goal

Integrate the independently reproduced libXi requested-device-ID correction into
main without depending on optional debugger capture or portable desktop work.
KDE/GNOME acceptance is deferred.

## Scope

Reuse the signed upstream patch and original regression from PR17, plus PR27's
separation of scheduling-dependent build logs from deterministic check outputs.
Retain upstream patch license notices and author-authorized DCO unchanged.
No installed library, HDT runtime, Steam session, prefix or launcher selection
changes. The check owns a sandboxed Xvfb server.

## Acceptance

- Three original-library runs demonstrate requested/returned ID mismatch.
- Three corrected-library runs each complete 50,000 open/map/close cycles.
- Rebuilding the same derivation retains identical output artifacts.
- C formatting/static analysis and normal hooks pass on committed source.
- Hosted CI passes before merging.

These are library regression checks. A live HDT crash-prevention claim requires
separate runtime evidence; it is not a merge requirement for this isolated patch
and check. PR16's debugger acceptance remains a separate unchanged gate.
