# Actual successful native parent return across consumption, settlement and ACK loss

Executing source **cdef9b87e136ac082fbf1ef332ca3abb115a0018** qualifies three second-crash boundaries under Root, Sequence, Flowchart and CompensationScope. All twelve real HTTP ETL journeys recover on standard Workbench hosts. The [matrix](manifest.json) binds **348 raw assets and 108 complete coupled stores**, forty-eight Workbench processes, twelve production-SDK clients and twelve HTTP fixtures. All seventy-two qualified children and thirty-seven rejected/superseded children have stopped. No production semantic change was needed.

Each first host publishes the actual native RunBpmn parent, service model, closed native RunEtl worker and HTTP ETL definition. It reaches a real durable HTTP429 retry while a sibling shard has produced two HTTP effects, then loses its process through SIGKILL. A second standard host opens the same stores with its normal recovery sweep configured to 300 seconds. Actual ETL retry finishes, preserving four real HTTP output records and the typed native worker result, while the original model result remains owed.

The owned [production SDK client](../../../../../tools/bpmn/NativeWorkDispatch/README.md) composes the existing activity runtime, CLR activation, native parent modules and serialization/type registry, alongside the existing ETL/native/model delivery services. Fourteen captured SDK product identities match the executing source. It uses the controller's original bindings and stores, starts no background worker, and creates no synthetic state, result, receipt or acknowledgment. The live ownership marker identifies the actual original RunBpmn invocation; its retained private state must match the model's exact parent invocation.

The SDK uses the normal outbox processor and protocol-aware recovery mailboxes to consume the original native result. It then reaches the selected actual boundary:

| Second-crash cut | Model | Native RunBpmn invocation | Original parent-result source | Parent-settlement receipt |
|---|---|---|---|---|
| `pending` | Revision 2, TerminalPendingSettlement | Suspended at its original real registration | Pending, unacknowledged | Absent; no queued settlement |
| `consumed` | Revision 2, TerminalPendingSettlement | Completed with its exact typed consumption receipt | FailedRetryable, fenced, unacknowledged | Absent; the original exact settlement input is queued |
| `settled` | Revision 3, TerminalSettled | Completed with the same typed consumption proof | FailedRetryable, fenced, unacknowledged | Accepted at revision 3 |

The original native-result source also remains FailedRetryable, positively fenced and unacknowledged, with its exact accepted revision-2 receipt. All ETL output is already complete before the second crash. Each cut retains provider-observed whole runtime, ETL runtime and ETL design stores before another actual SIGKILL, exit -9.

**The surrounding native workflow header is still Running at all twelve second-crash cuts.** Typed RunBpmn child consumption and model settlement are separate from whole-native-workflow completion. The native workflow still owns its actual completion or successor work. The capture preserves this real boundary rather than inferring terminal workflow status from a completed activity.

Fresh third/fourth standard hosts finish the original native workflow and composite successors, settle any original model parent input and acknowledge the original sources. No native/model scheduler work or incidents remain. The interpreter payload and prior input receipts do not change. In the `settled` cases, model revision 3 and its exact parent receipt are already durable before the crash and remain unchanged; acknowledgment loss causes no additional model decision. Parent-result ACK follows exact settlement. All four physical HTTP records remain unchanged, with no additional request or output after the second crash.

The read-only verifier checks actual model/worker/parent pins and dependency closure, the pinned native composite type, typed native consumption, original source payloads/fences, exact queued or accepted settlement, physical activity/input/queue rows, ETL terminal/accounting and HTTP effects. Eleven new rehashed negative controls reject premature consumption/ACK, missing typed or physical receipts, changed revision, a foreign queued settlement source, false physical model/ETL truth, a removed physical delivery fence, duplicated cases and swapped native composite labels. All portable controls pass **106/106**, zero skips. Maps and test-tier registration pass; all 104 frozen baseline assets and their manifest remain unchanged. [Qualification](qualification.json) records the bounded claims and check receipts. Earlier BPMN391/human-host8 receipts retain source b6e7e1f0d; they were not rerun for capture-tool changes.

Rejected attempts are retained privately before correction. The initial observer waited for `completed`, while the ETL API calls its successful status `succeeded`. The initial SDK composition lacked native bookmark resume and serialization/type registration. A later capture incorrectly required the whole native workflow header to be Completed despite a durable typed RunBpmn consumption receipt and real native continuation. Existing runtime features and the actual child-consumption boundary corrected these tooling assumptions. None of those attempts is relabelled as a qualifying journey.

## Reproduction

Build a clean committed source through the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --embedded-parent Sequence --successful-parent-result-crash consumed \
  --dispatcher-assembly <built-NativeWorkDispatch-assembly> \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> --output <fresh-owned-output>
```

Repeat `pending`, `consumed` and `settled` for all four parent shapes, using fresh output and stores. The controller requires clean executing source; output must be outside the checkout. Verify the committed evidence with:

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/successful-native-parent-return/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

These successful return cuts extend the [service success/cancellation matrix](../embedded-native-recovery/README.md) and separate [cancelled-parent return cuts](../pending-native-parent-return/README.md), retaining their original executing sources. Business/technical-fault mappings, held retry, other race/outcome arms, other providers/rollback and final affected-source upgrade/Studio remain open. T025/T027/T029/T031/T034–T041, #2898, #2907 and the program remain open/partial; full-PR review rounds remain 0 of 6.
