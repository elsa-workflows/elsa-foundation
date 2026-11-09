# Actual model consumption before native-result acknowledgment

Executing source **d1bd08c33f436764eaca5a5c69017f14a3ea85d5** qualifies the post-consumption/pre-ACK crash cut for both public model-originated and native-originated cancellation of the real admitted RunEtl/HTTP pipeline. It extends the earlier [pending cancellation/result captures](../pending-cancellation/README.md), retaining their pre-consumption snapshots as a separate phase.

The first standard Workbench publishes and runs the real direct BPMN → closed native RunEtl → HTTP ETL journey. One shard has produced two actual HTTP effects; the other retains its original durable 429 retry. After the first SIGKILL, a second standard host opens the same stores with its normal recovery sweep configured to 300 seconds. Public BPMN cancel records revision 2 with worker delivery owed, or public native CancelWorkflow records the actual terminal native checkpoint with ETL-cancel/result delivery owed. These are the same two initial cuts as the earlier receipt; neither is itself relabelled as the new crash cut.

An owned [production SDK client](../../../../../tools/bpmn/NativeWorkDispatch/README.md) then uses existing APIs, with no delivery hook or stored-state edits. It composes the existing ETL runtime/native adapter and EF persistence using the controller's original bindings and stores; it starts no background worker. For model-originated cancellation it first delivers the original model-cancel source and drains the real native Cancel command through its recovery mailbox. In both cases it delivers the original ETL-cancel source, queues the original native result through the production outbox processor, and uses the protocol-aware model recovery mailbox to consume that exact input. The first result delivery remains FailedRetryable until acknowledgment. All eight SDK product identities match the executing source.

The consumed-phase observations and complete stores prove:

- The real ETL invocation is already Cancelled with two retained accepted records, zero owed output and its original immutable native result address.
- The actual native worker remains Cancelled; the same terminal checkpoint retains the ETL-cancel and native-result sources, and the result's cancellation inventory names that exact delivered ETL source.
- The model is TerminalSettled at revision 3 for model-originated cancellation, or revision 2 for native-originated cancellation. Its exact native-result receipt is durable and matches the source payload, invocation, attempt and fingerprint.
- The native-result source is still FailedRetryable, carries its real positive delivery fence and has no delivered timestamp. In the model-originated case the original model-cancel source also remains unacknowledged. No model input is queued.
- The model-originated SDK recovery mailbox leaves an ordinary native recovery wake queued. The subsequent standard host consumes it; it is absent from the final physical queue.

The second host then loses its actual process through SIGKILL, exit -9. A fresh third standard Workbench acknowledges the original sources from the retained receipts. The model revision, original receipt and native truth do not change: acknowledgment loss causes no second logical progression. ETL terminal delivery uses its actual ProvenParentTerminal disposition. A fourth standard host stays identical through the original HTTP retry deadline plus two seconds. Both earlier HTTP effects remain and no late retry occurs.

The [matrix](manifest.json) binds **65 raw assets and 24 complete coupled stores**, including before, pending, consumed-before-ACK and fully recovered phases. Its two cases use eight Workbench processes, two production-SDK processes and two HTTP fixtures; all twelve qualified children have stopped. All rejected/superseded attempts' owned children have also stopped. Temporary paths/ports are observations; reproduction creates fresh owned bindings. [Qualification](qualification.json) retains source, checks and scope.

The first SDK attempt was correctly refused because it tried to process a model decision inside a native coalescing session. The corrected client uses the same protocol-aware recovery mailbox as normal model recovery. A later native-originated capture exposed a read-only observer assumption that active work still existed after completion; it now reads the retained native result. Those rejected captures were retained privately before correction. No production semantic fix or new architecture was needed.

## Reproduction

Build a clean committed source through the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel --pending-cancellation-crash --consumed-before-ack-crash \
  --dispatcher-assembly <built-NativeWorkDispatch-assembly> \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Repeat with `--native-cancel` in another fresh directory. Verify the committed cases:

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/consumed-before-ack/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

All 71 portable tests pass, zero skips. The new positive matrix and nine fully rehashed controls reject an already-acknowledged source relabelled as ACK loss, missing physical model consumption, a hidden physical acknowledgment, early model-cancel acknowledgment, an uncompleted ETL run, foreign SDK source, wrong/faulted protocol dispatch, a crash timestamp preceding consumption and leftover recovery work. The current standard host build has zero errors; maps and test tiers 123 pass. All 104 frozen baseline assets and the authoring fingerprint remain unchanged. Earlier BPMN 384/EF model checkpoint 43 checks retain source 54d8532b0 and are not relabelled as new-source test runs.

## Remaining acceptance

This closes both admitted direct-bridge cancellation/result ACK-loss cuts on the selected EF SQLite/built-in actor profile. RunBpmn service embedding and native-parent mappings, held-fault retry, other-provider/rollback qualification and final affected-source upgrade/Studio remain open. Broader T027/T029/T031/T034–T041 acceptance, #2898, #2907 and program #2892 remain incomplete. PR #3410 remains draft; full-PR review rounds remain 0 of 6. Orleans and task-runner follow-up remain deferred.
