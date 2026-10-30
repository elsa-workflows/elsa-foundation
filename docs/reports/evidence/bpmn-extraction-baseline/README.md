# BPMN extraction baseline (WU-0)

Pre-extraction fingerprints captured on branch `claude/elsa-bpmn-libraries-1b4509`, before any seam
refactor. These are the differential oracle for the BPMN extraction program: every later work unit is
verified by diffing against them.

Captured with the tree at 16 BPMN specs marked `Implemented` and no source changes.

## Files

| File | What it pins |
| --- | --- |
| `Elsa.Activities.Bpmn.Tests.tests.txt` | 247 test names, sorted |
| `Elsa.Activities.Bpmn.Interchange.Tests.tests.txt` | 107 test names, sorted |
| `activity-contract-fingerprint.json` | The public authoring contract of `BpmnProcess` and `BpmnDecision` |

Both suites were green at capture time: 247 passed / 0 failed, and 107 passed / 0 failed.

## How to use them

**The test-name lists are historical differential oracles.** Compare later work with them without
restamping the captured files. Follow the current [framework §2.21.1](../../../../.specify/memory/constitution-framework.md#221-test-discipline)
when moving, rewriting or deleting tests. For rewrites or deletions, name the removed code or the
other test that proves the behaviour, for PR review. Green CI alone does not explain a changed
subject or objective.

Regenerate with:

```bash
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release --logger "trx;LogFileName=bpmn.trx" --results-directory <dir>
```

Run the two projects in **separate** invocations. `dotnet test` rejects two project arguments with
MSB1008, and if the call is piped the shell reports the pipe's exit status rather than the failure,
so a broken run can look like a passing one.

The interchange project is
`Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj`.
The [0.5.0 adoption comparison](../bpmn-adoption-0.5.0/README.md) records the current test inventory,
the historical registration-test removal, its coverage limit and the separate host-compatibility gates.

**`activity-contract-fingerprint.json` must not change across the entire program.** It encodes
`structure.kind = elsa.bpmn.structure`, `schemaVersion = 1.0.0`, the `Bpmn.Activities` child slot, the
`Done` outcome, and the `CanStartWorkflow` input. If a diff appears here, the extraction has altered
the authoring contract, which is a breaking change to published workflow definitions rather than a
refactor.

Note the fingerprint records only `Done` for `BpmnProcess`. The `Cancelled` outcome is
structure-dependent and resolved during compilation, so it is correctly absent from the static
contract.

## Existing restart evidence and missing upgrade fixtures

This capture has no persisted-state golden for changing engines. Later integration work does prove a
narrower fresh-host restart: the EF
[`NativeBackgroundActivityFlowchartEfTests`](https://github.com/elsa-workflows/elsa-foundation/blob/98efed4fc2c07337ad0b30128b60f462b8ddcff0/Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/NativeBackgroundActivityFlowchartEfTests.cs#L252)
disposes the host, reopens the same durable store, accepts a recorded background-worker receipt and
asserts the still-running BPMN instance is waiting at its join. That is same-version restart proof;
it does not prove upgrading the private-state reader, a killed process, provider migration, or
race-loser/deferred-fault recovery.

`BpmnStatePersister` hard-validates the `Elsa.Bpmn.ExecutionState` alias and schema version. A shape
change under that version can still lose fields silently. Before the library switch deletes the old
writer, capture actual legacy checkpoints and prove their version-aware read and continuation
through the replacement. Include state, durable child identity, queues/outbox and bookmarks at a
coherent checkpoint. Synthetic typed-state probes remain useful but are not persisted execution
upgrade proof. The [adoption comparison](../bpmn-adoption-0.5.0/README.md) names these gates.
