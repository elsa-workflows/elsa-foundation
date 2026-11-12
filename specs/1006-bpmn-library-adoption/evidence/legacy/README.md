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

## Supplemental actual late-child-fault capture

A sixth snapshot, [queued-late-fault](queued-late-fault.json), uses the unchanged production writer/pins from `d81a4c88a2924708ea8afcc2db20b8a77b84f703` plus the retained test-only [capture overlay](late-fault-capture.patch). Its patch SHA-256 is `1b4332b7fc97d0920ca2697dfacf172cfbe83556a697fb3916863d0ba327bf06`; export requires the actual patch file and verifies that digest. The base revision alone does not identify this supplemental harness.

The actual losing child returned `TEST-FAULT`, with its exact original blocking/unresolved incident and a child-fault callback physically queued at observation **10**, before winner cancellation at **14**. The parent is Running, token:9 is Canceled, and trailing final work is owed. The online backup includes the original incident, and its physical reread is compared with the capture provider along with all prior activity/bookmark/queue checks. No Faulted state or incident was synthesized.

The fresh **old-host** control consumes that exact callback, records its processed-work receipt and the old engine's ignored-late-fault diagnostic. It nevertheless leaves the original incident blocking; the ordinary incident strategy applies `FaultWorkflow`, leaving the BPMN parent Running and final catch parked. This is a verified legacy incident-absorption gap, not an old-host successful completion claim. The upgraded host is expected to correct that gap using released Absorbed disposition and exact local incident resolution.

To reproduce only this supplemental capture, first check out the exact base in a separate directory, then apply the retained patch. Use a new empty export directory and the patch's actual absolute path:

```sh
git checkout --detach d81a4c88a2924708ea8afcc2db20b8a77b84f703
git apply /absolute/path/to/late-fault-capture.patch
ELSA_BPMN_CAPTURE_SOURCE=d81a4c88a2924708ea8afcc2db20b8a77b84f703 \
ELSA_BPMN_CAPTURE_PATCH_FILE=/absolute/path/to/late-fault-capture.patch \
ELSA_BPMN_CAPTURE_PATCH_SHA256=1b4332b7fc97d0920ca2697dfacf172cfbe83556a697fb3916863d0ba327bf06 \
ELSA_BPMN_CAPTURE_DIRECTORY=/absolute/new-empty-output-directory \
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release --filter FullyQualifiedName~Actual_legacy_late_fault_checkpoint
```

The source/control/export run passed **1/1, zero failures/skips**. The overlay applies to the pristine base index and reverses against the capture checkout. Both artifact hashes and SQLite integrity/physical facts were independently reviewed. The original five snapshot files and frozen authoring oracle were not replaced. Normal switched-tree tests consume this sixth frozen artifact and never invoke the old capture writer.

[Upgraded late-fault and replay qualification](../late-fault-upgrade.md) records the actual new-host result and mutation sensitivity, separately from this source/control proof.

## Remaining acceptance

Gate A preserves the old writer's real data before deletion. The fresh upgraded-host readability/outcome and same-node lookup witnesses above now exist. Actual late-fault absorption and duplicate queued completion/fault callbacks after pruning/provider restart are separately qualified in the linked evidence. MI/listener-specific remaining replay/fault cases, native parent qualification and combined-outcome durability remain open; running binary rollback is unsupported. Library tests and synthetic probes cannot substitute for those witnesses. The public authoring fingerprint and Studio journeys remain separate gates.

## Supplemental actual typed MI/listener fault checkpoints

Two additional captures, [parallel-mi-outcome](parallel-mi-outcome.json) and [listener-rearm-outcome](listener-rearm-outcome.json), were made on 27 October 2026 from the same exact old base `d81a4c88a2924708ea8afcc2db20b8a77b84f703` with the test-only [outcome-wait capture overlay](outcome-wait-capture.patch), SHA-256 `fdcfc6365c407bad175eaa0473ea047ae4f6af02e2a24bfd0ed4e0bb45d2b17f`. All 12,312 base files were compared: only the capture test changes, plus its added test-only CLR leaf. Old production source, writer and package/build pins are unchanged. Standalone patch application reconstructs both test files exactly outside the working repository.

The real typed leaf suspends through the ordinary runtime and returns a typed fault when normally resumed. The identical leaf source is retained in the upgraded test assembly; native Event behavior is not altered. The MI boundary has three actual Suspended invocations with distinct nonblank token/iteration identities, exact parent/live-work/bookmark links. The listener boundary has a prior Completed invocation and a different current Suspended invocation at the same node and null iteration, after a real normal completion/rearm. Actual committed concrete EF reads and online-backup rereads retain their original coherence checks. No private state, fault, incident or callback is fabricated.

Fresh **old-host** controls over separate copies normally resume one selected wait with `fault`. Both controls pass and record the genuine child `LEGACY-OUTCOME-FAULT`, original incident facts, and Faulted workflow. The old parent reports `bpmn.child.faulted`; the released adapter's new unhandled-work verdict is `bpmn.work.faulted`, while preserving the actual child fault. The leaf also contains a cancel branch; no old cancellation execution or parent cancellation callback is claimed from that unused branch.

Reproduce in a separate exact-base checkout, with a new empty output directory:

```sh
git checkout --detach d81a4c88a2924708ea8afcc2db20b8a77b84f703
git apply /absolute/path/to/outcome-wait-capture.patch
ELSA_BPMN_CAPTURE_SOURCE=d81a4c88a2924708ea8afcc2db20b8a77b84f703 \
ELSA_BPMN_CAPTURE_PATCH_FILE=/absolute/path/to/outcome-wait-capture.patch \
ELSA_BPMN_CAPTURE_PATCH_SHA256=fdcfc6365c407bad175eaa0473ea047ae4f6af02e2a24bfd0ed4e0bb45d2b17f \
ELSA_BPMN_CAPTURE_DIRECTORY=/absolute/new-empty-output-directory \
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~Actual_legacy_outcome_wait_checkpoint
```

Export passes **2/0/0**, retains CreateNew guards and never overwrites the prior six artifacts. Both new SQLite integrity checks and raw/compressed digests pass. The [upgraded fault receipt](../legacy-outcome-fault-replay.md) distinguishes terminal queue purge from the live-workflow processed-callback replay guard.
