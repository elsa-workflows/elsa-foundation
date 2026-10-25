# Actual legacy execution checkpoints

Captured on 25 October 2026 from **`d81a4c88a2924708ea8afcc2db20b8a77b84f703`**, using the real Foundation BPMN writer and EF Runtime SQLite store. Production BPMN source and package pins are unchanged from parity merge `fbe694d2f3d126e324c3e46578bc1a560b0217f1`: Model/Interchange `0.1.1-preview.19`, private alias `Elsa.Bpmn.ExecutionState`, version 1. The capture code is in that commit's `LegacyBpmnCheckpointEfTests.cs`; its source revision is permanent even after the old writer/generator is removed.

These are actual committed executions. A SQLite online backup runs after a committed transaction while the single test actor is still awaiting it. The backup includes the complete Runtime database: executable, parent and direct/terminal child documents, variable frames/values, bookmarks, scheduler queue, incidents, operational ownership, hierarchy/inspection projections, post-commit outbox status/claims and checkpoint replay markers. No states were constructed by hand to substitute for host execution.

Each `.sqlite.gz` is paired with a JSON receipt containing exact raw/compressed SHA-256 digests, source/pins and boundary facts. The online backup is self-contained; no original machine path, WAL sidecar or temporary proof file is needed. Always decompress to a new path before opening it. For read-only inspection of its WAL-mode header, use SQLite's `mode=ro&immutable=1`; an in-memory `deserialize` connection cannot open that header. Normal writable copies were opened successfully by every old-host control.

| Snapshot | Actual boundary | Old-host control |
|---|---|---|
| [resolved-race](resolved-race.json) | Winner consumed; loser is terminal Cancelled with its token provenance; trailing final bookmark remains. | Fresh provider over a separate whole-store copy resumes final wait and completes. |
| [queued-loser](queued-loser.json) | Loser is Completed and its exact callback was observed queued at ordinal **13**, before first winner cancellation at ordinal **15**. Queue/outbox and both child identities remain in the database. | Fresh provider recovers pending work, absorbs the loser callback and resumes final wait once. |
| [parallel-mi](parallel-mi.json) | Three outstanding invocations at the same node with distinct child/token/iteration identities and three bookmarks/frames. | Fresh provider resumes all three and completes once. |
| [listener-rearm](listener-rearm.json) | Prior terminal listener and live re-arm share node/null iteration, but have different exact child/token identities. | Fresh provider fires the re-armed listener again, then completes held main work. |
| [deferred-fault](deferred-fault.json) | Real legacy PendingFault after an evaluation already staged work; original `bpmn.gateway.no-flow-selected` and queued/outbox work retained. | Fresh provider recovers the callback and faults with that exact original code. |

The controls do not modify the captured database: each consumes a separate copy. Each creates a new service provider and type registry, uses a distinct namespace for newly minted child IDs, and advances its test clock by one day so inherited claim leases can recover. This is a same-version old-host control, not a process-kill test, a provider migration, the new interpreter, or Studio compatibility.

## Ordering control and review

Initial review found that cancellation plus queue co-presence did not establish their order. Artifact review also caught that an interface queue read inherited the AsyncLocal coalescing overlay. That could describe transient child scheduling while the physical queue still held segment-entry work. The final capture source reads the underlying EF activity/bookmark/queue adapters and re-reads the resulting backup, asserting full equality before export. Receipts therefore describe the physical snapshot; owed child scheduling can correctly remain in outbox rather than queue. The earlier uncommitted outputs were discarded and regenerated.

The capture now records first queue-presence and first cancellation observations and requires strict ordering. A test interceptor physically withholds the exact loser's queue insert until after the first cancellation commit, then inserts it. Capture rejects that real reversed order. Removing the ordering condition made the negative test fail (`Assert.False`, actual True); restoring it passed all six capture/control cases. Matching two completions in one drain alone is not used as this proof.

Independent correctness, standards and acceptance reviews covered the capture code. Source findings were corrected before export. Independent correctness, standards and acceptance artifact reviews verified the regenerated digests, physical state and receipt agreement before the task ledger closed gate A.

## Reproduction

Use the exact capture source in a separate checkout; after the switch, its writer cannot regenerate historical state. Export to a **new empty directory**. Export uses CreateNew and never overwrites existing fixtures.

```sh
git checkout --detach d81a4c88a2924708ea8afcc2db20b8a77b84f703
ELSA_BPMN_CAPTURE_SOURCE=d81a4c88a2924708ea8afcc2db20b8a77b84f703 \
ELSA_BPMN_CAPTURE_DIRECTORY=/absolute/new-empty-output-directory \
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release --filter FullyQualifiedName~LegacyBpmnCheckpointEfTests
```

The export invocation passed **6/6, zero failures/skips**: five scenarios plus the reversed-order rejection. The affected BPMN project also passed as a whole **251/251, zero failures/skips**. Locked solution restore and generated solution-filter check passed. Adding the scheduling test reference regenerated its lock: one project addition plus framework-package pruning, with no retained requested range/version/content-hash changes. These results do not claim the entire EF suite or final architecture/check gate has run.

## Upgraded reader checkpoint

`BpmnUpgradeRecoveryEfTests` now consumes these exact compressed databases through fresh released-library EF hosts. It verifies both receipt digests, completes all five scenarios, preserves version 1 and its `activeChildren` wire name while continuing, and retains the original deferred fault code/message. Four corruption controls reject missing/conflicting child history or a missing graph element, preserving the source private state and issuing no additional work.

First-node/null-iteration lookup mutation fails exactly the listener-rearm witness; raw library exception mutation fails exactly the missing-element domain-exception witness. Restoring the implementation passes **9/9 with zero failures/skips**. A second resumption sweep checks repeated discovery after completion; it is not duplicate callback delivery or combined-outcome retry proof.

The old generator's six tests are retired from the switched tree: they intentionally depend on the preview writer, including its deferred-fault behavior, and cannot capture that writer by executing the new default adapter. Reproduction remains on the exact capture-source commit above. The five frozen-data consumers and four rejection controls replace that ongoing test obligation; no goldens are regenerated by normal tests.

## Remaining acceptance

Gate A preserves the old writer's real data before deletion. The fresh upgraded-host readability/outcome and same-node lookup witnesses above now exist. Dedicated late-fault/duplicate callback/pruning assertions, native parent behavior, combined-outcome durability and rollback limits still need qualification. Library tests and synthetic probes cannot substitute for those witnesses. The public authoring fingerprint and Studio journeys remain separate gates.
