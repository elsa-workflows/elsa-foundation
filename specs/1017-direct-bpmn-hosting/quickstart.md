# Qualification sequence

This is the reviewed implementation target, not a record of executed results. The hosting test project and portable runner below are created by the implementation tasks before use. Existing #2735 receipts keep their original source/version scopes.

## Prerequisites and process ownership

- Use the configured .NET SDK and build-slot wrapper; inspect machine load before interpreting timing failures. Build only affected projects; let required CI provide broader gates.
- Use owned Workbench/Studio instances, isolated scope/configuration and actual durable stores. Record PID, start time, parent/executable and configured roots before stopping a process; terminate only that owned process and wait for its actual exit. Never kill by port or process-name pattern. The user's existing host/browser sessions are not fixtures.
- Keep baseline and upgraded assemblies/configuration manifests distinct. Record source SHA, assembly/package identities, selected modules, store paths and permissions without secrets. Authenticated HTTP commands obtain credentials through the existing local configuration; do not print tokens or store them in evidence.
- Check OrbStack/Docker before Testcontainers provider work. A missing container runtime is not a provider pass. Keep SQLite host results, four-engine common-provider tests and distributed-host qualification separate.

## Gate A: capture before writer changes

Implement `tools/bpmn/qualify-hosting.py` first as a portable runner over normal product publication/execution/inspection commands. Its `capture-baseline` phase accepts an owned baseline host/config manifest, creates uniquely named fixtures, parks real native/BPMN runs, takes consistent whole-store backups, and writes `specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json` with exact IDs, hashes and observations. Never edit execution state to produce a positive fixture.

Required capture families are defined by the affected writer map in the plan: partial fork/join/waits, all three native parents and terminal delivery, signal source/cohort/rearm/pre-ack cuts, and affected called-control/MI/compensation state. Capture actual Studio-published authored material/export/layout/pins. Baseline source and all asset hashes must verify before the first affected writer edit. Existing 0.5.0 production assemblies may supply the baseline only when their actual source and bytes are verified and labeled truthfully.

Run the runner's read-only `verify-capture` phase against whole backups; check saved source/version, physical keys, pending work and frozen authoring. Synthetic corrupt/incompatible copies are separate negative controls and never become positive upgrade inputs. Missing required actual-host coverage keeps Gate A open.

## Focused implementation checks

Run after the corresponding projects/tasks exist; no commands below have been claimed as passed by this plan:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check
```

Also run the actual touched Runtime, Runtime EF, DispatchWorkflow, ETL, API and serialization projects discovered from the changed-project map; record exact commands/results in the resulting receipt. Run genuine provider/migration/upgrade tests for all changed first-party EF paths. Add/use the explicitly selected incremental mode in the repository's `tools/ef` workflow; the current default regenerates/deletes Initial and must not be used against this retained Runtime baseline. No ad-hoc DDL, rewritten baseline IDs or unrelated global freeze.

## Actual host matrix

The portable runner's `qualify` phase records normal API commands and persisted observations for these cases. Real-process integration tests supply deterministic barriers and actual process termination at the named cut; timing sleeps alone do not establish a cut.

| Family | Required observable result |
|---|---|
| Direct start/wait/resume and native counterpart | Real model execution/material without native root; unchanged native consumer; matching scope/material pin and retained input receipt. |
| Parallel recovery | Two branches, crash after first accepted completion, restart, complete second; one logical join and terminal outcome. |
| Delivery cuts | Crash before dispatch, after recipient acceptance before acknowledgement, and before native-parent consumption; owed work retained and duplicate logical advancement absent. |
| Human/timer and worker/timer | Four durable acceptance orders, eligible timer due checks, restart, stale/foreign/duplicate/conflicting attempts and obsolete-result refusal. |
| Broadcast | Listener created between publication/first delivery receives; first empty/populated snapshot retained; two listeners delivered; listener rearm and accepted-recipient/pre-ack retry do not broaden cohort. |
| Real pipeline | Direct and embedded success, caught/unmatched business error, technical fault and pipeline-originated cancellation; inspect physical output and actual ETL/native terminal result. |
| Propagated cancellation | BPMN interruption and each supported native-parent cancellation before admission, during work and during pending result; inspect actual pipeline and remaining work after restart. No unsettled uncompleted run silently remains Running; already-terminal results remain truthful. |
| Human/incident commands | Inspect/complete actual wait, inspect/retry actual incident and cancel after restart; one retry attempt; authorization precedes replay disclosure; no direct state edits. |
| Retry lifecycle | Actual direct and RunBpmn native-service technical failure held before semantic fault; restart in WaitingIncident, retry once, matching retry replay, unchanged old fault evidence, rejection of old results and eventual typed completion. FinalFault/exhausted-budget/cancelled work refuses retry without reviving semantics. |
| Terminal settlement recovery | Terminal model plus owed child cancellation/result-consumption or parent acknowledgement; terminate/restart, retain and consume the exact settlement input without new semantic progression. Existing native terminal-purge behavior remains unchanged. |
| Native parents | Sequence/Flowchart/CompensationScope completion/fault/cancel and pending outcome recovery; preserve existing fault projection and actual typed outputs. |
| Ownership | Real predecessor termination/replacement plus deliberately delayed stale decision and obsolete worker attempts; no stale new state/outbox acceptance, while previously committed physical delivery remains allowed. |
| Material/retention/rollback | Immutable pins survive publication retirement with retained edges; delete/GC cannot remove owed material; capability admission off retains accepted readers/workers; unsupported old writer fails before touching new families. |
| Runtime-only | Recover/run accepted material with Design storage unavailable; no mutable-source lookup. |

For each changed safeguard, demonstrate a control that fails when the production guard is removed, then restore and pass. Do not use a passing baseline or an assertion over documentation as that control. Capture actual raw durable inputs/output/observations safely and bind every claim to source/library/provider versions.

## Upgrade and Studio

The runner's `resume-upgrade` phase opens Gate A's actual backups on the changed host, observes held identities/state, then finishes them through supported commands. Retain baseline/recovered/final whole stores, observations and comparison results. If a source/configuration change invalidates the comparison, repeat the relevant capture honestly; do not relabel an old positive snapshot.

Use the authorized browser harness against the owned Studio pair: open the actual saved definition, inspect retained authored payload and diagram, edit/save/export/publish, run it and inspect the exact resulting execution. Compare frozen authoring fingerprint `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92` and explicit XML/layout boundaries. Save and inspect screenshots where they substantiate the journey. Preserve user tabs and hosts. This is existing Studio compatibility, not #2907 direct-model Studio acceptance.

The [actual existing Studio upgrade receipt](evidence/studio/README.md) qualifies both captured versions, saved layout, Published Runs and Test Runs at its named source. `tools/bpmn/run_owned_studio_upgrade.py` owns a fresh pair over either actual store; `tools/bpmn/verify_studio_upgrade.py` verifies the retained inputs, browser observations and twelve upgraded whole stores. Repeat against final affected source under T041.

## Delivery receipt

One concise receipt maps FR-001–FR-022, SC-001–SC-006 and all #2898 criteria to committed assets/commands/results, explains provider/profile limits and lists remaining #2907/program obligations. Verify every recorded asset hash and relative link. Complete the independent full-PR loop within six rounds, green exact-head required checks and protected merge, then verify actual resulting main. Keep issue/PR/Project62 current and release only the completed specific claim.
