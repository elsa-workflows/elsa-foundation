# Called-control claim takeover and split delivery through EF

This bounded T045 increment adds eight actual-runtime cases over SQLite with released BPMN 0.5.0 in Durable and Balanced modes. Four hold one real notification delivery before or after actual caller acceptance, expire its real lease using the injected clock and let a distinct provider take over. Four hold ordinal zero under a live claim while a different owner claims and delivers an actual empty terminal ordinal one. The [machine receipt](called-claims-ef-recovery.json) binds source, binaries, final runs and restored mutations. T045 remains open for no-capability legacy recovery and complete actual Workbench proof.

## An expired owner cannot change the winner

The first provider publishes/pins and executes a real caller and separate BPMN child. The child emits WARN zero and parks at an Event; a forwarding outage leaves its actual source retryable with no caller control acceptance. After disposal, a fresh owner takes the source through the real outbox processor. An optional forwarding test wrapper holds either before calling the normal handler or after it returns. Both dispatcher overloads preserve the original intent/context, cancellation and durable-source declarations. In the after-effect case, concrete caller state positively shows accepted zero, cursor one and its actual warning-handler Event suspended while the source is still Delivering. The before-effect case has no inbox or handler execution.

A separate peer's real processor attempts no delivery before expiry, with the complete claimed source projection unchanged. Advance the injected clock past the observed deadline; the peer then holds a new real Delivering claim on the same immutable source. Its nonempty owner differs and its fence is exactly one greater, while recorded attempt count is unchanged. The original owner task remains held throughout. Release the peer: the actual handler returns, the source becomes Delivered with exactly one new recorded result, and the exact call has accepted zero at cursor one with no pending batch. Across both providers there is exactly one real ControlAsync evaluation: the peer performs it for the before-effect cut, the original owner for the after-effect cut.

Freeze complete caller/child activity and incident documents, actual physical bookmark observations, named workflow status/pin/mode and the complete source model projection. Release the old owner. Its actual processor result is **SupersededByOtherOwner**, with zero delivered/failed counts; winner source and recipient facts stay equal. Dispose both overlapping providers. A fourth provider reads those same facts and source, resumes the actual warning and child Events, consumes fresh empty terminal one at cursor two and completes both workflows with truthful pins/modes and no waits, incidents or poison. The original delivered zero source remains equal.

## Two live claims cannot skip an earlier ordinal

A separate matrix uses terminal success and ordinary fault. After the seed provider's actual before-effect notification failure, a fresh owner claims ordinal zero and holds before delivery. A separate producer provider normally resumes the real child Event and drives normal recovery until the real terminal intent is produced. A controlled before-delivery outage records that terminal source retryable. No synthetic batch or claim is installed.

A different peer uses the terminal intent's actual parent workflow address to claim ordinal one while the zero claim remains unexpired and its entire projection unchanged. Both actual Delivering sources have nonempty distinct owners and positive fences. The terminal batch is fresh ordinal one, empty and without authored failure classification; the dispatch result remains truthful Completed or Faulted. Release only terminal delivery: the calling leaf completes normally with the correct outcome/no local fault and its processed-parent receipt. Terminal one is accepted Pending at cursor zero/not-consumed. The caller stays Running with its complete BPMN private-state envelope unchanged, no warning/normal execution and **zero adapter control calls**. Zero's owner task remains held and its source unchanged.

Release zero through its still-valid claim. Exactly one actual callback receives **[0, 1]** for the exact call, committing cursor two/terminal-consumed/no pending while preserving the original completion receipt. Both sources deliver under their own claims without supersession; the already Delivered terminal source stays equal. Ordinary failure faults the caller without promoting a classification; success retains a real warning Event that later completes normally. Complete original child activity/incident truth remains equal. Both overlapping providers are disposed; a fresh final provider reads the complete checked facts and source projections unchanged before finishing the success handler. This matrix uses five provider generations: seed, zero owner, terminal producer, terminal peer and final reader.

## Verification and limits

Final called-control **32/0/0** and whole Runtime EF **1,710/0/0** match discovery. Two production variants compile and restore byte-identically before final normal verification:

| Mutation | Failures / passes | Observed phase |
|---|---:|---|
| Remove stale-owner validation from the shared claim completion transition | 4 / 0 | EF's optimistic concurrency still refuses the old write, but its re-read no longer identifies the stale claim. Old completion throws the explicit claim-still-appears-valid InvalidOperationException instead of converging to Superseded. No winner overwrite is claimed. |
| Initialize an inbox at incoming ordinal rather than zero | 4 / 0 | Terminal one is incorrectly consumed instead of buffered behind the separately live zero claim; the buffered-terminal assertion fails before release or final restart. |

The initial takeover-only four-case pass predates the final cleanup refinement. The first combined run passed four takeover rows and failed all four split rows because the fixture inspected terminal production immediately after bookmark dispatch. A subsequent run with normal recovery exposed a terminal claim filtered to the child instead of its actual parent owner address; it timed out, was stopped by SIGTERM on the owned test process and is excluded. Final code drives the normal producer and uses the actual terminal transport address. Review also fixed cleanup to release and join both delivery tasks even if one faults, preserve the original assertion and bound both processors with owned cancellation.

These are deterministic lease expiry and intentionally ordered cut releases with overlapping real providers, not simultaneous SQL claim conflict or process kills. Full activity/incident and physical bookmark documents plus named workflow status/pin/mode and source model fields are compared; whole-workflow bytes, complete variables and physical source-row revision are not claimed. Builders supply compiled contracts, so public authoring compiler, HTTP Workbench and final Studio remain separate gates. Production source, frozen authoring, eight old captures and historical receipts remain unchanged. Three independent bounded component audits are clean; full-PR rounds remain zero, bound six.

Reproduce:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~BpmnCalledWorkflowControlEfTests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

T029 ordinary compensation, final actual current Studio, T032 deletion after all old writer captures/upgraded-host proof, T033–T035 guards/audits, T036–T038 final qualification/full-PR/required green exact-head checks/protected delivery/resulting-main, then separate ADR0063 remain open. Independent #2898/#2907/program production acceptance stays open; Orleans/task-runner remain deferred.
