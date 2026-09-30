# Generating from pinned workspace profiles

Implementation boundary for [#2166](https://github.com/elsa-workflows/elsa-foundation/issues/2166), following the [profile selection contract](../../../specs/174-profile-selection-planner/contracts/selection-planner-v1.md), the [file bridge contract](../../../specs/176-composition-file-bridge/contracts/file-bridge-v1.md), and the existing [edited-selection acceptance](edited-selection-acceptance.md) flow. Delivered through [PR #2173](https://github.com/elsa-workflows/elsa-foundation/pull/2173), canonical merge `d18ee00a9e9bb6908718571a348d4303a8695e54`, after independent/root review and applicable exact-head/main gates; [main CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36667956159) and [Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36667955933) passed. The issue records the partial merge response and source-identical metadata retry; unavailable Copilot is not approval.

## Developer flow

A workspace profile is one user-owned immutable snapshot. Supply the same pinned file to planning, acceptance, and generation:

```bash
dotnet elsa composition plan --composition edited.json --workspace-profile ./runtime-profile.json --format json
dotnet elsa composition accept --composition edited.json --workspace-profile ./runtime-profile.json --output accepted.json
dotnet elsa composition generate --host-dir ./host --shell default --environment Production \
  --composition accepted.json --workspace-profile ./runtime-profile.json --output-dir ./candidate
```

`--workspace-profile` is repeatable. Generation passes every supplied definition, including unused definitions, to the same selection planner used by plan and accept. The accepted exact feature set must still equal the candidate plan. A missing or mismatched pin, malformed or digest-invalid definition, duplicate profile identity, known required dependency gap, or unsupported host mapping refuses without publishing a candidate.

## Immutable review boundary

Generation captures the accepted composition, optional supplied catalog, optional setting review, and every workspace-profile file once. Parsing uses only those captured bytes. Immediately before the private staging directory is published, generation rechecks both the complete host source snapshot and all supplied inputs. A change to a selected or unused input invalidates the review and cleans staging. The bundled Foundation catalog remains program data rather than a local captured file.

Workspace profiles influence selection only. They are not copied into the host bundle, do not add a package or setting source, and do not establish historical authenticity beyond the supplied content digest. Existing four-argument candidate-builder callers retain Foundation/default-catalog behavior; the additive overload accepts the explicit profile collection without moving file access into the planning API.

## Evidence and limits

The acceptance proof uses actual CLI plan, terminal acceptance, accepted-plan inspection, terminal generation with repeated selected and unused profiles, and CShells readback of the exact IDs. It also covers source and sibling preservation, redaction of profile rationale and dependency reasons, supplied-input drift, invalid definitions, collisions, cancellation, and handoff behavior.

The plan and generated diff continue to report inventory and persistence as unverified. Candidate files do not prove package availability, provider connectivity, migration safety, database state, deployment integrity, or live activation readiness.
