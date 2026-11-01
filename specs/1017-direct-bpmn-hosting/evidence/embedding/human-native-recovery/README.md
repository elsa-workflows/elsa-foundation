# Actual native RunBpmn human-work recovery

Eight production Workbench journeys at **0c419ca5f5fe494c802d062eff2512a2b7ab9f38** run one published BPMN start → human wait → end process through a real native `RunBpmn` activity. Root, Sequence, Flowchart and CompensationScope each have completion and whole-parent cancellation cases. Publication retains the complete native/model pins, model material incarnation and dependency. The normal activity checkpoint freezes its actual bookmark registration and first native attempt; no substitute parent, activity or bookmark is constructed.

Every case reaches the actual native/model wait, loses its owned first host through `Process.Kill` (observed exit137), and opens the same unedited stores in a fresh Workbench. Completion resumes the original typed registration and continues the native parent. Cancellation uses the existing public `CancelWorkflow` alteration API and leaves native/model Cancelled; a late human completion is rejected without advancing the model. A third Workbench preserves the exact checked terminal observations. This demonstrates whole-parent cancellation after model admission; cancellation before admission and subtree cancellation remain separate acceptance cuts.

The model's terminal checkpoint retains `TerminalPendingSettlement` and its result delivery source together. Ordinary bookmark delivery consumes the original native invocation, then queues the model's `native-parent-settled` input. Its exact durable receipt permits source acknowledgment and `TerminalSettled`. Portable comparison checks the physical receipt fingerprint, native result/typed registration, source payload and acknowledgment time. Queue acceptance or a dispatcher status alone does not qualify settlement.

Each of the four completion cases also executes the **same published process directly**. Released-interpreter waiting and terminal states compare exactly after one declared normalization: a token's `producingWorkHandle` equal to that execution's own model ID. Diagnostics, token/flow/element identities, ordering, sequence, outcomes and every other state field compare unchanged.

The [manifest](manifest.json) binds **64 raw assets** from **24 Workbench processes**, including 32 closed whole Runtime/Design snapshots. Checked-provider reads are compared with physical rows in read-only, immutable copies; every snapshot passes SQLite integrity. Nine selected Foundation host/runtime/BPMN/publishing products carry the exact source above; both interpreter binaries identify released0.5.0. All owned children are stopped. Captured process identities and paths are historical provenance; reproduction creates fresh private hosts and stores.

An earlier real completion exposed a faulty assumption: native completion legitimately clears activity private state. The first attempt therefore left the model pending after its parent completed. The fix proves terminal responsibility against retained material and the actual native completion/registration, while cancellation captures the committed predecessor before state retirement. Earlier native-only composition also exposed a required model reader in the embedding boundary; that boundary is optional when unused. The retained eight passing journeys include both fixes. CI subsequently exposed additional delivery-service composition and activity/readability integration gaps; **CI at this source is red**, and this receipt does not authorize merge.

## Reproduction and checks

Use the normal build-slot wrapper at a clean committed source. The owned-host suite builds the standard Workbench and checked reader. Evidence export is optional and requires an exact source revision; it never opens user stores.

```sh
ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=<fresh-output-directory> \
ELSA_BPMN_HOST_SOURCE_REVISION=<exact-source-commit> \
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false --filter FullyQualifiedName~RunBpmnWorkbenchTests

python3 tools/bpmn/verify_runbpmn_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/embedding/human-native-recovery/manifest.json
python3 -m unittest discover -s tools/bpmn -p test_verify_runbpmn_recovery.py -v
```

All eight actual cases pass at the recorded source, as does the full Runtime EF suite1777/1777. Earlier tests of the implementation content committed as80e6d5928 passed BPMN381/381 and Publishing61/61; those pre-merge runs are separately scoped in [qualification](qualification.json). Portable verification plus seven rehashed corrupt-copy controls pass8/8. Controls reject a foreign typed model result, a missing settlement receipt, a changed physical receipt fingerprint, a lost model dependency, a changed semantic diagnostic, a graceful exit relabeled as process loss and late progression after cancellation. All104 frozen baseline assets remain unchanged.

## Remaining acceptance

This is the human-work `foundation-direct-v1` profile on EF SQLite and the built-in single-node actor. The actual process-loss cut is the first human wait. Service/ETL embedding, cancellation before model admission, pending-parent-result crash, subtree cancellation, three native parent fault mappings, model-held terminal-fault retry and remaining race/provider/rollback criteria remain open. Prior legacy, Studio and ETL receipts retain their own sources; final affected-source upgrade and Studio checks must be repeated. T027/T028/T029 and T031/T034–T041 remain incomplete, and #2898/#2907/program acceptance stays open.
