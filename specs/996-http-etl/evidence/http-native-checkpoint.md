# Persisted HTTP ETL native continuation

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), draft
[PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156), task T010.

## Executable boundary

The persisted publication journey now runs both retained v1 file pipelines and v2 HTTP-to-JSONL pipelines
inside a Sequence and a Flowchart. The shared fixture uses actual design stores, compilers, native promotion
and workflow publication. It publishes ETL version 1, freezes that selection into the parent, then publishes
version 2 with a different output mapping before disposing all authoring services.

The ETL design database is moved offline. A separate durable runtime provider has no v1 or v2 ETL design
store/compiler, no workflow executable compiler, and no compilation sources. It reads the published native
executable and admits its retained ETL artifact. The following native sibling is absent while RunEtl is
suspended; the worker has not sent an HTTP request.

For HTTP, the first runtime provider is disposed after admission. Its replacement uses the same SQLite
workflow and ETL databases but a changed current source revision and endpoint. The binding resolver confirms
that new work would select revision 2 with a different handle. The admitted run retains revision 1, sends
exactly one GET to its original approved seed, and writes the original `name` mapping and `Ada` value.
No CSV source or lookup is prepared for this HTTP pipeline.

The actual shared worker completes the ETL run, consumes the native completion receipt and clears the
bookmark/private state. The following activity and enclosing Sequence/Flowchart complete, with the typed
terminal counts and acknowledged ETL result outbox checked. This is provider recreation in one test process;
it is not the two-worker-process interruption acceptance owned by T013.

## Review and validation

Initial focused execution exposed a fixture defect: restarting its deterministic ID generator reused the
already-persisted root activity ID for the following sibling. Retained diagnostics showed completed RunEtl,
a parent continuation and no following activity. The replacement harness now uses a disjoint ID range.
All four focused cases then passed. The shared fixture also avoids duplicate publication and binding setup.

Two bounded root review/fix rounds and independent reviews ended with no remaining actionable findings.
The final ETL suite passed **927 tests, 0 failures, 0 skipped**. The focused publication matrix passed all
four cases before the negative control.

For the negative control, both exact source-binding resolutions were temporarily replaced with current
revision resolution. Both HTTP cases failed with the retained `etl.http.page-admission-invalid` diagnostic
(the Sequence assertion detected the unsuccessful ETL result; Flowchart detected the absent success sibling),
while both v1 cases still passed. The production source was restored byte-for-byte, verified by SHA-256,
before the final full suite. No production source change is included in this checkpoint.

Final validation:

| Gate | Result |
| --- | --- |
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore` | 927 passed, 0 failed, 0 skipped |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 584 passed, 0 failed, 0 skipped |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | Passed |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check` | Passed |
| Focused persisted publication matrix | 4 passed |
| Current-for-retained binding negative control | Both HTTP cases failed as intended; both v1 cases passed; source restored exactly |
| Relative Markdown links and `git diff --check` | Passed |

T010 is complete; **9/22 tasks** are complete in the full HTTP ETL leaf. Fresh pushed-head CI remains a separate
gate and the draft is not ready to merge. The Workbench REST compatibility gate from
[the preceding worker checkpoint](http-worker-checkpoint.md) was not rerun for these test-only changes;
actual HTTP product/browser acceptance remains T019.

## Remaining boundary

This success-path proof does not close HTTP cancellation/late acknowledgement races, two-process recovery,
frozen predecessor-worker resume, Studio authoring/inspection or live browser/REST HTTP acceptance. Those
remain explicit tasks in [the full plan](../tasks.md). External effects remain at least once. Historical receipt
and HTTP timeout causes remain unproved. The task runner and workflow-scoped pause remain follow-up work.
