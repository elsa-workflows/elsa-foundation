# Native parent cancellation checkpoint

This is a partial T022 checkpoint in draft PR #3111, not completion of the finite ETL journey or program.

## Actual cancellation and ordering

The shared SQLite/native composite fixture now covers Sequence, Flowchart and CompensationScope in two directions of delivery ordering:

- After actual ETL admission, cancel the native workflow through `WorkflowExecutionHarness.CancelAsync`, the actor command and ordinary scheduler cancellation checkpoint. The checkpoint-derived ETL cancel intent settles the actual run. Require cancelled native activities, no following sibling, no consumed trigger receipt, absent native bookmark, terminal-cancelled ETL result, no accepted output manifest and an acknowledged result outbox with `ProvenParentTerminal`.
- Pause the production admission service before its call, after the ordinary native bookmark/start intent has committed. Require that no ETL run exists yet. Cancel through the same native command and deliver only the committed cancellation-intent kind through the real outbox processor. Require an actual cancelled/no-bindings tombstone before releasing admission. The delayed call forwards to the production admission service; the complete serialized run remains unchanged, with no bindings or preparation. The real host worker subsequently acknowledges the terminal result without resuming the cancelled activity.

The barrier controls ordering only; it returns no fabricated admission/result state. Its release runs in `finally`, the pending drive is awaited, and the fixture retains its three SQLite databases and diagnostic JSON on assertion failure. The first ordering run reached cancellation but failed CLR object equality because deserialized artifacts use reference equality. Comparing persisted values corrected that fixture assertion; it was not an ETL resurrection.

## Generic native cleanup correction

`WorkflowCancellationPlanner` already captured bookmark/timer/work cleanup for activities terminalized by cancellation. `WorkflowCancelSchedulerWorkHandler` discarded `plan.CleanupRequests` when constructing its checkpoint. The correction passes those existing requests into `RuntimeCheckpointStateChangeSet`; no ETL-specific scheduler branch is added.

Cleanup belongs to that cancellation checkpoint. There is no later bookmark sweeper to rely on. A terminal parent may permit ETL result acknowledgement even when its bookmark remains, so acknowledgement alone is not cleanup evidence. The generic runtime regression additionally verifies that the same commit deletes the live activity's bookmark while preserving an already-completed sibling and its bookmark. Existing cancellation, coalescing, ownership and dispatch contracts remain exercised by the runtime suite.

## Evidence

Root self-review ran three bounded passes and ended with no remaining actionable finding in this checkpoint. Final validation: **254 ETL, 2,431 workflow-runtime and 579 architecture tests passed**, with zero failures/skips. The final strengthened generic cancellation-contract subset passed all **17** cases after exact source restoration. Workbench rebuilt with zero errors (81 existing warnings), its owned-server REST replay/restart journey passed, and committed dependency-map/filter/diff checks passed. These are local Debug results; fresh exact-head CI remains separate evidence. The regression receipt [native-parent-cleanup-regression.json](native-parent-cleanup-regression.json) records the temporary removal and byte-exact restoration. Removing only the cleanup handoff makes all three actual SQLite parent-cancellation cases fail their absent-bookmark assertion; restoring it passes. Removing it also fails the generic live-bookmark regression (one failure); exact restoration passes the 17 cancellation-contract cases. The original six-case exploration also rejected three invalid fixture scope assumptions: ordinary native composite roots do not own `ExecutionScopeId == InvocationId`.

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore
pwsh -NoProfile -File e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

The REST replay/restart journey uses a rebuilt Workbench, its own temporary content root/database, a free loopback port, and only processes it owns. It proves public alteration cancellation/reconciliation; it does not substitute for the private native-to-ETL cancellation assertions above.

## Remaining boundary

T022 remains unchecked until genuine activity-boundary subtree cancellation and its reversed ordering are proved. No scope metadata is injected to make an ordinary composite appear to own a reusable activity boundary. The production compiler/placer must supply that boundary. The full workflow-definition/compiler/picker/API/Studio path, deterministic final materialization, two-process recovery and remaining HTTP/browser acceptance remain owed. Physical effects remain at least once; cancellation does not prove physical worker termination or reverse prior output. Historical receipt/HTTP timeout causes remain unproven. Orleans remains deferred; no actor provider is selected here.
