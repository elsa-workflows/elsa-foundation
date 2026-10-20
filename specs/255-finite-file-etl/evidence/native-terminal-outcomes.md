# Native ETL terminal outcomes

Date: 2026-10-20. Active #3109 / draft PR #3111; builds on integrated checkpoint `bca4c08cc02f75f7d5d67256f832ba501ba950b4`. T020 is complete, bringing the checklist to 17/36. T022 and the complete product acceptance remain open.

## Executed cases

The same actual migrated SQLite/native start, scheduler/resumption and ETL background-task fixture now runs a matrix of three ETL terminal outcomes across Sequence, Flowchart and CompensationScope, alongside the four existing successful/later-fault journeys: 13 native cases in total.

| ETL outcome | Trigger | Native result and composite behavior |
|---|---|---|
| BusinessError | Source contains an invalid required Int64 field | Native child completes `BusinessError`, with exact persisted typed result/counts; one rejected row/error chunk and no main chunk. Sequence and CompensationScope schedule the next sibling normally. Flowchart follows its explicitly authored `BusinessError` edge. |
| Faulted | An owned fixture file occupies the configured host snapshot-directory path | Real snapshot publication fails with `etl.output-write-failed`; native child faults and has no successful completion. Sequence halts with a faulted workflow; Flowchart records its own `flowchart.child.faulted` fault; CompensationScope completes `Compensated` without executing its later forward step. No accepted output manifest exists. |
| Cancelled | Actual ETL runtime store cancels the admitted invocation before workers run | The real terminal outbox resumes the child with Cancel. `ActivityCancellationCheckpointService` atomically queues workflow cancellation; all three surrounding workflows/composites cancel, no next sibling runs and no successful child completion is recorded. |

Each case requires the matching consumed typed native receipt, original registration/provider/payload type, absent consumed bookmark and acknowledged ETL result outbox. A final `ProvenParentTerminal` disposition alone cannot pass, because the exact consumed receipt is independently asserted. Completed success and business-error results are deserialized from the native activity's committed completion and compared with the real ETL run/terminal counts and diagnostic. Faults/cancellations cannot masquerade as successful completion.

The fixture uses actual files and `IEtlRuntimeStore.CancelAsync`; it does not call RunEtl execution methods directly, fabricate ETL terminal results or replace the native dispatcher. ETL-returned cancellation is distinct from native whole-parent/subtree cancellation flowing into ETL, which remains T022. Input/output binding revisions remain pinned before execution. Physical effects remain at least once; these preparation-fault/cancel cases do not establish reversal of previously published effects.

## Fixture correction and diagnostics

The first matrix run passed 8 and failed the CompensationScope cancellation case at the fixture's eight-immediate-sweep queue-empty assertion in about 950 ms. An instrumented isolated rerun passed. That is evidence of an intermittent fixture observation failure, not a proven causal explanation of any historical timeout. Source inspection shows that an actor/result worker may concurrently own queued work, so a nonempty queue after eight immediate sweeps is not the domain completion condition.

The fixture now sweeps and waits, under the same 30-second deadline, for the required durable native states and ETL acknowledgement. All final state/outcome/receipt/output assertions remain strict. Initial suspension/admission uses the same state-based wait. A shared lifetime helper retains the three SQLite files and captures queue/native/ETL diagnostic JSON for test-body failures, including assertions after polling. Diagnostic collection does not mask the original failure. Disposal still stops the owned worker and releases services/host before successful-fixture deletion.

## Validation

- Focused native composite/outcome cases: 13 passed, zero failures/skips.
- Full final-source Release ETL suite: 248 passed, zero failures/skips.
- Committed dependency-map and solution-filter checks: passed; diff check passed.
- Root bounded self-review: three passes, ending with no remaining actionable finding in this test delta. Worker review identified the post-poll evidence-retention gap; root fixed it and verified it using a real regression control.
- No permanent production, package, lock or project-reference change is included. The preceding integrated source's 66 compensation, 569 Activity Runtime, 579 architecture and locked-restore results remain baseline evidence; they are not represented as newly rerun here.

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~EtlNativeCompositeJourneyTests
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --configuration Release --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

The [regression receipt](native-business-outcome-regression.json) records exact source hashes and command. Temporarily changing `BusinessError => Complete(value, "BusinessError")` to `Complete(value)` makes the real Sequence business-error case fail on its native outcome. The fixture retained its diagnostic JSON and all three SQLite databases. Exact source restoration passes the same case; no changed production code remains.

The full workflow-definition compiler/picker/API/Studio path, native-initiated whole-parent/subtree/pre-admission cancellation, two-process recovery, deterministic final materialization and HTTP/browser acceptance remain required. The parent/program is not complete, and standing self-review/normal self-merge authority does not turn incomplete product acceptance into a merge gate pass.

## Final test-source hash

`Extensions/Etl/tests/Tests/EtlNativeCompositeJourneyTests.cs`: `8d953dec8ab7f89c55e0ef54d3824bd417c7793bab0a231f5f00ffa6fe3c28ab`.
