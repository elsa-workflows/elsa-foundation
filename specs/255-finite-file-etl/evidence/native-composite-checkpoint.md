# Native ETL composite checkpoint

Date: 2026-10-20. Active work: #3109 / draft PR #3111, following component checkpoint `73f79539a4b52f451c0af12b3d791b63923c8bc7`. This checkpoint implements T017/T019 and part of T020; it does not finish the finite-file ETL product or program #2892.

## Actual execution and the defect found

`EtlNativeCompositeJourneyTests` starts a normal published native execution via the actual start dispatcher and durable scheduler/resumption service. Workflow runtime, ETL runtime and ETL design use separate migrated SQLite databases. The registered ETL `IBackgroundTask` runs after the migration initializers. The test does not call the activity's Execute/Resume methods, forge native state, replace result delivery or inject a terminal ETL result.

Four journeys complete and retain the actual accepted JSONL `{"name":"Ada"}`:

1. Sequence schedules RunEtl, suspends, resumes it from its durable result, then completes its next sibling.
2. Flowchart routes from the resumed child to its next connected node and completes.
3. CompensationScope journals normal RunEtl completion, schedules its next named step and completes.
4. A later scope step faults after ETL and its sibling completed, with no compensation handlers configured. The scope finishes `Compensated`; its surrounding Sequence runs the next sibling, while the accepted ETL run/manifest/output remain.

Each case asserts the exact durable consumed typed receipt (registration, provider and payload type), missing consumed bookmark, one completed native ETL child and next sibling, succeeded ETL state and acknowledged ETL result outbox. A worker can observe parent termination before acknowledging; `ProvenParentTerminal` is legitimate alongside `Consumed`/`AlreadyConsumed`, and the receipt separately proves this child really resumed.

The initial integration did **not** pass. Sequence/Flowchart completed but exposed a test's overly narrow outbox-disposition expectation. CompensationScope remained Running after the ETL child completed and the result was consumed. Retained SQLite/JSON showed `compensation.role`, scope and step in the child's committed scheduling provenance, absent from the later resume command. CompensationScope read that transient command and ignored the completion. Callback contexts now carry an immutable `ChildSchedulingMetadata` snapshot from the persisted child. The native parent handler supplies it for completion and fault; CompensationScope consumes it. Existing constructor signatures remain available. There is no command-metadata merge, ETL kernel branch, actor-provider work or ETL-specific compensation rule.

## Verification

All executed suites below had zero skipped cases:

| Check | Result |
|---|---|
| Focused durable composite journey | 4 passed |
| Full ETL Release suite | 239 passed |
| Compensation Release suite, including EF crash/restart cases | 37 passed |
| Activity Runtime Release suite | 517 passed |
| CI-shaped locked Release and isolated Debug restores | Passed |
| Regenerated committed dependency-map freshness | Passed |
| Architecture Release suite | 579 passed |
| Solution-filter check | Passed |
| Diff review | Passed; root bounded loop ended clean after four passes |

Commands, run from the repository root:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --configuration Release --no-restore
dotnet test Essentials/tests/Activities/Compensation/Tests/Elsa.Activities.Compensation.Tests.csproj --configuration Release
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --configuration Release
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --configuration Release --no-restore
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

Root review/fix passes covered fixture API/lifecycle and acknowledgement expectations, the reproduced callback-provenance defect, the immutable/additive contract and later-fault journey, then final integrated source/evidence review. A bounded worker review supplied input; root retained integration and QA. Sipke's standing self-review/approval/normal self-merge authority applies; independent approval is not an agent-imposed gate. The PR stays draft for incomplete product acceptance.

The [regression receipt](composite-provenance-regression.json) records exact source hashes and commands. Temporarily replacing both scope metadata reads with the old command lookup failed two repeated-delivery regressions (2 failed, 2 passed); byte-exact source restoration passed all four. The actual SQLite pre-fix timeout is independently retained in the local diagnostic receipt. Passing later tests does not establish the cause of any unrelated historical receipt/HTTP timeout.

## Evidence limits and next work

The fixture uses actual ETL compilation/publication and invokes the actual ETL pin contributor, then stores the built executable through the harness publication helper, including CLR contract pinning. It **does not** run the full workflow-definition compiler/contributor chain. That remains API/Studio acceptance work. One successful row/chunk is covered here; deterministic final materialization remains T016. Native ETL business-error/fault/cancellation cases, real two-process failure recovery, HTTP and browser gates remain open. Existing CShells worker-lifecycle tests are separate from this harness-driven background-task lifecycle. Physical external effects remain at least once; only SQLite is qualified.

A read-only review flagged existing Flowchart/BPMN delivery-command correlation fallbacks for repeated active same-node paths/tokens. This checkpoint proves the stated graphs only; it neither reproduces nor resolves that broader concurrency concern. Track it with program compatibility work before claiming all composite/parallel-model semantics qualified. Combined child-outcome/notification contexts are unchanged; CompensationScope uses completion/fault callbacks.

## Reviewed source hashes

```json
{
  "Essentials/src/Activities/Compensation/Activities/CompensationScope.cs": "dbdd2f563a73b0da08489374b5219cbcf3d6ced201d572e82e45b703127d1550",
  "Essentials/src/Activities/Runtime/Core/Models/ActivityChildCompletedContext.cs": "e9de442f71bef53a9144e3b572c2fca9cf27a88cf63d48d6df411710613f3cbd",
  "Essentials/src/Activities/Runtime/Core/Models/ActivityChildFaultedContext.cs": "074170089fdbc17bb732cd935db8ffe6f2ce3996adc83233d44961cca7a47f66",
  "Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs": "2fa2b445674e1486507018b90f03077df6b345bdc6e46edad6e9a570005ae9b0",
  "Extensions/Etl/tests/Tests/EtlNativeCompositeJourneyTests.cs": "03db045d24baac7c7d6ebcbd53ee9c1a051d3e98af30adda1f0d8ece9050a937",
  "Essentials/tests/Activities/Compensation/Tests/CompensationScopeRepeatDeliveryTests.cs": "77794e0ed857864ba7a6aecd938c0d68e9c3944cfd69f81d5da545b8a92916b7"
}
```
