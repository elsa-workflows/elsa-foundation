# Actual cancelled native parent with its model return still owed

Executing source **ef833d12dc288202bf6e538884837b6e2526dc7e** qualifies a second actual process loss while a terminal service model still owes its original native parent result. Root, Sequence, Flowchart and CompensationScope all recover the same cancellation chain. The [matrix](manifest.json) retains **120 raw assets and 36 complete coupled stores**, binding sixteen standard Workbench processes, four production-SDK clients and four HTTP fixtures. All twenty-four qualified children have stopped.

These captures extend the [actual service embedding journeys](../embedded-native-recovery/README.md), retaining their earlier executing source and success/direct comparison separately. No production semantic change was needed for this increment.

The first standard host publishes the real RunBpmn parent, service model, closed native RunEtl worker and HTTP ETL definition. A real HTTP429 retry is durable while the sibling shard has produced two HTTP effects. The first actual SIGKILL retains those stores. The second standard host opens them with its normal recovery sweep configured to 300 seconds, and its public CancelWorkflow alteration cancels the outer native parent. The model and worker remain running, with the original embedded-cancel source owed.

The owned [production SDK client](../../../../../tools/bpmn/NativeWorkDispatch/README.md) binds that exact parent ID to its live controller marker and original lossless model invocation. Using existing outbox processors and protocol-aware recovery mailboxes, it delivers and consumes embedded cancellation, worker cancellation, actual ETL cancellation and the exact native result. It starts no background worker and writes no synthetic state, result, receipt or acknowledgment.

Complete provider-observed stores at the second crash prove:

- The outer native parent, original native worker and actual ETL run are Cancelled. Two prior HTTP effects remain, with zero owed output.
- The model is TerminalPendingSettlement at revision 3, format 6. Worker cancellation is settled and the exact native-result receipt is durable, but the native parent return remains owed.
- The original `ResumeNativeParent` source is Pending and unacknowledged. No `native-parent-settled` receipt exists and no model input remains queued.
- Native-result, embedded-cancel and worker-cancel source acknowledgments remain FailedRetryable with their real positive delivery fences. Actual ETL cancellation was delivered before native-result consumption.

The second host loses its process through another actual SIGKILL, exit -9. A third standard Workbench settles the original native parent result at revision 4 and acknowledges the original sources. Its interpreter payload, terminal result and previous exact input receipts remain unchanged. Source acknowledgment follows the exact parent-settlement receipt. A fourth standard host stays identical past the original HTTP429 deadline plus two seconds; no late request or extra HTTP effect occurs. Native recovery work clears and ETL terminal delivery retains its actual ProvenParentTerminal disposition.

The read-only verifier cross-checks all three coupled stores, physical parent/model/worker pins, actual source payloads, original fenced acknowledgments, exact input receipts, cancellation ordering, output accounting and retained views. Nine new rehashed negative controls reject early parent ACK, early native-result ACK, preexisting parent settlement, unsettled worker cancellation, omitted ETL store, physically settled model, rebound native parent pin, an old SDK source and a duplicated parent case. All portable controls pass **94/94**, zero skips. The source-matched SDK and standard host build with zero errors. [Qualification](qualification.json) keeps prior BPMN391/human-host8/maps/tier receipts explicitly at source b6e7e1f0d; those checks were not rerun for capture-tool changes. All 104 frozen baseline assets and their manifest remain unchanged.

## Reproduction

Build a clean committed source through the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --embedded-parent Sequence --native-parent-cancel \
  --pending-parent-result-crash --dispatcher-assembly <built-NativeWorkDispatch-assembly> \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> --output <fresh-owned-output>
```

Repeat for Root, Flowchart and CompensationScope, using fresh output and stores. The controller requires a clean executing source; output must be outside the checkout. Verify the committed evidence with:

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/pending-native-parent-return/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

These are cancelled-parent returns. They do not prove successful RunBpmn parent consumption followed by source-ACK loss, business/technical-fault mappings, all other cancellation/race arms, held retry, other providers/rollback or final affected-source upgrade/Studio. T025/T027/T029/T031/T034–T041, #2898, #2907 and the program remain open/partial. Full-PR review rounds remain 0 of 6.
