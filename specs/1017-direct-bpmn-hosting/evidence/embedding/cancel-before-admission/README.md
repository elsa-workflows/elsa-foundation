# Real parent cancellation before model admission

At **d22a35f0e6e33305734cdfdb9e98d2dc604e71f8**, a normally published native RunBpmn root reaches its real typed wait while its start source remains owed in the durable outbox. The checked reader and normal model API observe **no model execution or input receipt**. This is the cut before first outbox delivery; the model command queue is empty.

The normal public `CancelWorkflow` alteration plan then cancels the native parent through its ordinary orchestration pump. The first host uses the normal configuration `SweepIntervalSeconds: 300` for resumption, leaving time for this earlier cancellation. No pump hook, fake clock, synthetic parent or stored-state edit is used. The original start and cancellation sources remain pending, with the same immutable invocation/model pin. A stopped whole-store backup proves the physical parent is Cancelled and that no model execution, receipt or model queue row exists. The host was killed with `Process.Kill` (exit137).

A fresh Workbench opens the same unedited stores with the normal resumption interval0.2 seconds. Actual first start delivery observes that the parent has already cancelled and retains a Cancelled model record without starting the interpreter: sequence0, no tokens, active work, diagnostics or human waits. In this capture, the original start input is accepted at revision1 and the later cancellation input is rejected as already terminal at the same revision. The exact parent settlement input commits revision2 before result acknowledgment. All three original/result sources become Delivered. A third process preserves the exact checked model/native/source observations and normal model API response.

The [manifest](manifest.json) binds **nine raw assets**, including four complete closed Runtime/Design snapshots, the actual native wait, checked observations before/after cancellation, and source/process receipts. Nine selected Foundation products carry the exact source above; both interpreter binaries identify released0.5.0. All three owned Workbench processes are stopped. Captured process identities/paths are historical provenance; reproduction creates fresh isolated resources.

## Reproduction and verification

At a clean committed source, run through the normal build-slot wrapper:

```sh
ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=<fresh-output-directory> \
ELSA_BPMN_HOST_SOURCE_REVISION=<exact-source-commit> \
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false --filter FullyQualifiedName~RunBpmnAdmissionCancellationTests

python3 tools/bpmn/verify_runbpmn_admission_cancel.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/embedding/cancel-before-admission/manifest.json
python3 -m unittest discover -s tools/bpmn -p 'test_verify_runbpmn*.py' -v
```

The exact-source host case passes. All eight original parent completion/cancellation host cases also pass against the same built products after the CI fixes. Six portable checks for this receipt and eight for the [earlier human-work matrix](../human-native-recovery/README.md) pass with zero skips. Five rehashed corrupt-copy controls reject an already existing model, changed interpreter sequence, a physically Running parent relabeled Cancelled, a physical source redirected to another wait, and acknowledgment earlier than exact settlement. Every retained closed store passes integrity. The original104 baseline assets remain unchanged.

The CI integration fixes in f925baa8c make both delivery services resolvable when no model reader is selected and refuse actual use without qualified persistence. The real behavioral drive now measures the activity's Done outcome and required Model input through the native/model runtime and SQLite. The activity-contract baseline adds only RunBpmn; readability/finalization expectations include the protected scheduler version3 and native worker version2 while retaining old readers. Local content checks pass BPMN381/381, behavioral220/220, composition2/2, readability117/117, contract3/3 and finalization15/15. [Qualification](qualification.json) preserves these test/source distinctions; exact-head CI remains a separate gate.

## Scope

This proves one actual **native root cancellation before first model admission**, on the built-in single-node actor and EF SQLite. The earlier matrix retains its own source and proves cancellation after admission under all four parent forms. This receipt does not prove a simultaneous database race, pending-parent-result crash, service/ETL embedding or cancellation, subtree cancellation, held-fault retry, upgraded-host ETL or final affected-source Studio. T027/T028/T029/T031/T034–T041 and broader #2898/#2907/program acceptance remain incomplete. Full-PR review rounds remain0 of6; no merge is authorized by this partial receipt.
