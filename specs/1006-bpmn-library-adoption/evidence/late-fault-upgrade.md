# Actual legacy late-fault recovery and replay

The supplemental [actual capture](legacy/queued-late-fault.json) and [portable writer-harness recipe](legacy/README.md#supplemental-actual-late-child-fault-capture) preserve the original preview-host faulted losing child, incident, queued callback and canceled semantic token. This is a whole EF database from real execution, not a synthetic fault-row edit. The original five captures remain unchanged.

Two permanent cases in BpmnUpgradeRecoveryEfTests reopen this artifact through fresh released-library Foundation providers. The general recovery case drives the original callback and final wait to Completed. The replay case additionally verifies the original fault document byte-for-byte through serialization and the original incident's identity, workflow/activity/node, severity, failure type, message and creation time. Exactly that incident becomes AbsorbFault with StructuralFaultAbsorption provenance, the exact BPMN parent and reason bpmn.work.late-fault-absorbed. No unrelated incident is selected.

The losing token is pruned after accepted consumption; the exact durable parent-completion work receipt survives. A second provider re-enqueues the identical original fault callback, proves it is physically queued then consumed with no poison, and observes no repeated adapter evaluation, private-state/child/bookmark change or incident-resolution change. The final wait completes once. The companion captured losing completion has the same replay assertions.

A temporary mutation suppressing only the adapter's late-fault absorption request made exactly both new late-fault cases fail: the original incident stayed blocking and the workflow could not complete. Restoration passed the whole EF project: **1018 passed, zero failures/skips**. The targeted upgraded recovery/refusal/replay set contains13 cases. The earlier omitted-replay-sweep mutation remains documented separately and is also removed.

The fresh old-host control over the same capture consumes the callback but leaves the incident blocking; the ordinary fault strategy terminalizes the workflow with the BPMN/final wait parked. The new host corrects this explicit legacy incident-absorption gap; the old-host result is not misreported as successful process completion.

Reproduce the upgraded witnesses from this commit:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~BpmnUpgradeRecoveryEfTests
```

Independent correctness, standards and acceptance artifact/source reviews verified hashes, SQLite integrity, physical receipt facts, unchanged production capture writer/pins, portable patch application and upgraded assertions. These are incremental reviews. The final complete-PR loop has not begun.

This closes the fresh-provider all-fixture recovery/reader/rollback-documentation task T018. T017 remains open for its remaining MI/listener-specific fault/replay coverage. This does not establish combined-outcome acknowledgement loss, concurrent claims, called-workflow controls, arbitrary alteration, running binary rollback or production acceptance.
