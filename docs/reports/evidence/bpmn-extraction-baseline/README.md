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

**The test-name lists are the framework §2.21.1 ledger.** Regenerate after any work unit and diff.
Additions are fine. A deletion or a rename is not a passing CI detail: it needs recorded architect
approval in the PR body, because §2.21.1 says a test may move file, project, or assembly but its
subject and objective must be preserved, and CI green is explicitly not sufficient evidence.

Regenerate with:

```bash
dotnet test tests/Elsa/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release --logger "trx;LogFileName=bpmn.trx" --results-directory <dir>
```

Run the two projects in **separate** invocations. `dotnet test` rejects two project arguments with
MSB1008, and if the call is piped the shell reports the pipe's exit status rather than the failure,
so a broken run can look like a passing one.

**`activity-contract-fingerprint.json` must not change across the entire program.** It encodes
`structure.kind = elsa.bpmn.structure`, `schemaVersion = 1.0.0`, the `Bpmn.Activities` child slot, the
`Done` outcome, and the `CanStartWorkflow` input. If a diff appears here, the extraction has altered
the authoring contract, which is a breaking change to published workflow definitions rather than a
refactor.

Note the fingerprint records only `Done` for `BpmnProcess`. The `Cancelled` outcome is
structure-dependent and resolved during compilation, so it is correctly absent from the static
contract.

## Still missing, and it matters

There is no persisted-state golden here yet, and no test anywhere asserts that a BPMN instance
persisted by the current engine resumes on a later one. `BpmnStatePersister` hard-validates both the
`Elsa.Bpmn.ExecutionState` type alias and the state schema version, throwing on any mismatch, so a
shape change in `BpmnExecutionState` is the one class of regression that unit tests pass through
silently while breaking every running instance on upgrade.

Add that golden in WU-2, alongside the host-port refactor: serialize a representative set of
post-prune `BpmnExecutionState` instances, commit them here, and add a test that deserializes them
through the current code path.
