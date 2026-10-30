# Qualification sequence

This is the reproduction and verification sequence for the implementation. Executed results live in source-bound receipts; commands here do not imply a pass. The [final affected legacy/Studio repeat](evidence/final-compatibility/README.md), [storage/provider gate](evidence/providers/model-storage/README.md) and [task evidence index](tasks.md) retain their executing sources and limits. Existing #2735 receipts keep their original source/version scopes.

## Prerequisites and process ownership

- Use the configured .NET SDK and build-slot wrapper; inspect machine load before interpreting timing failures. Build only affected projects; let required CI provide broader gates.
- Use owned Workbench/Studio instances, isolated scope/configuration and actual durable stores. Record PID, start time, parent/executable and configured roots before stopping a process; terminate only that owned process and wait for its actual exit. Never kill by port or process-name pattern. The user's existing host/browser sessions are not fixtures.
- Keep baseline and upgraded assemblies/configuration manifests distinct. Record source SHA, assembly/package identities, selected modules, store paths and permissions without secrets. Authenticated HTTP commands obtain credentials through the existing local configuration; do not print tokens or store them in evidence.
- Check OrbStack/Docker before Testcontainers provider work. A missing container runtime is not a provider pass. Keep SQLite host results, four-engine common-provider tests and distributed-host qualification separate.

## Gate A: capture before writer changes

Gate A is complete in the frozen [baseline manifest](evidence/baseline/manifest.json): 104 original assets and 22 complete store inputs were captured before the affected writer changes. Do not regenerate those inputs with the new writer. `tools/bpmn/qualify-hosting.py capture-baseline` remains the original partial-fork capture workflow over normal publication/execution/inspection commands; the other actual capture families have their own retained producers and receipts. Never edit execution state to produce a positive fixture.

Required capture families are defined by the affected writer map in the plan: partial fork/join/waits, all three native parents and terminal delivery, signal source/cohort/rearm/pre-ack cuts, and affected called-control/MI/compensation state. Capture actual Studio-published authored material/export/layout/pins. Baseline source and all asset hashes must verify before the first affected writer edit. Existing 0.5.0 production assemblies may supply the baseline only when their actual source and bytes are verified and labeled truthfully.

Run the runner's read-only `verify-capture` phase against whole backups; check saved source/version, physical keys, pending work and frozen authoring. Synthetic corrupt/incompatible copies are separate negative controls and never become positive upgrade inputs. Missing required actual-host coverage keeps Gate A open.

## Focused implementation checks

Run the affected whole projects through the build-slot wrapper and record the exact source, commands and executed/listed counts:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check
```

Also run the touched Runtime, Runtime EF, DispatchWorkflow, ETL, API and serialization projects discovered from the changed-project map; record exact commands/results in the resulting receipt. Run genuine provider/migration/upgrade tests for all changed first-party EF paths. Use the repository's explicit [incremental Runtime migration mode](../../tools/ef/README.md); its default Initial regeneration must not be used against the retained baseline. No ad-hoc DDL, rewritten baseline IDs or unrelated global freeze.

The strict common-provider gate uses `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS=1` and test-owned PostgreSQL/SQL Server/MySQL containers. Remove the three `ELSA_RUNTIME_BOOKMARKS_EF_*_TEST_CONNECTION_STRING` overrides before that run so it cannot select a shared database. [The retained matrix](evidence/providers/model-storage/README.md) records the exact commands: physical atomic checkpoint/receipt cases and fresh/additive migration fixtures are separate from actual old-engine host recovery. SQLite also migrates the whole actual frozen store.

## Actual host matrix

`Essentials/tests/Activities/Bpmn/Hosting/Tests/` and the journey-specific `e2e-tests/bpmn/qualify_*.py` controllers record normal API commands and persisted observations for these cases. There is no generic `qualify-hosting.py qualify` phase. Follow each receipt's actual producer, arguments and build identity. Real-process integration tests supply deterministic barriers and actual process termination at the named cut; timing sleeps alone do not establish a cut. Live SQLite reads/backups use the host's .NET provider; Python validators read only the resulting closed immutable evidence.

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

The [final affected-source repeat](evidence/final-compatibility/README.md) qualifies both captured Studio versions, saved layout, Published Runs and Test Runs after the bridge/retry/signal changes. The [earlier Studio receipt](evidence/studio/README.md) retains its original source. Build the Studio browser workspace before Studio.Web so static-asset discovery cannot overlap frontend regeneration. `tools/bpmn/run_owned_studio_upgrade.py` owns a fresh pair over either actual store; `tools/bpmn/verify_studio_upgrade.py` verifies the retained inputs, browser observations and twelve upgraded whole stores. T041 is complete for its declared affected source; repeat any later change that invalidates those observations.

## Delivery receipt

One concise receipt maps FR-001–FR-022, SC-001–SC-006 and all #2898 criteria to committed assets/commands/results, explains provider/profile limits and lists remaining #2907/program obligations. Verify every recorded asset hash and relative link. Complete the independent full-PR loop within six rounds, green exact-head required checks and protected merge, then verify actual resulting main. Keep issue/PR/Project62 current and release only the completed specific claim.
