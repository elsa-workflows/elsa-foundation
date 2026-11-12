# A1 focused correction validation

**Result: PASS — A1 is resolved within the actual original-input cancellation journey with compute bindings offline.** This is a focused validation, not a new complete-PR round. The original round11 report remains unchanged and round11 remains not clean. A subsequent committed exact-head full review is still required.

Executing products: `6bd8ac3e86d1701603b1c975534514d3fe62933b`.
Source-equivalent reviewed head: `f83df1b56ae3f8f41e70f7f61b9c0ad60bad74e1`.
Original producer: `9508e25e57672c569f462aff9a1e0282c5780eb9`.
Input: `specs/1017-direct-bpmn-hosting/evidence/reviews/round-7/pre-correction/waiting/`.
Raw new capture: `/tmp/elsa-2898-review11-nested-cancellation/`.
Controller: `/tmp/elsa-2898-review11-nested-cancellation.py`, SHA256 `153e797f341ed0cdb83bb87da2269fb97118d167635fe286fe43171def7ef1fe`.

## What was independently established

I checked the controller, actual retained configuration, product hashes, all original and current closed SQLite snapshots, physical model/native/ETL records and full API observations. The audit uses private decompressed copies opened with SQLite readonly/immutable access. All12 store references (three original plus three before/completed/retained trios) match their recorded compressed/decoded hashes and pass integrity checks. All12 actual Workbench/BPMN/Runtime/ETL product hashes match the declared6bd products. No build, host operation, source edit or GitHub write was performed by this validation.

The first current-source closed cut contains the actual missing starting condition: both original model headers remain Running at revision1; both original native workers remain Running; the original ETL remains Running with no terminal/outbox result. Both model headers, both worker headers, accepted original model inputs, original start sources and immutable executable rows match the original waiting input. This is not the already-settled after-cancel fixture.

The unchanged chain is:

- Outer model `bpmn:1e8d5e7048efac41363050bc9d38cc13788dd36bf1bd742004a928163f5db4c7`.
- Native RunBpmn worker `wfexec:start:v1:88a6734247f9e516ee3827a57a34c6138aa34b8b29cae7916ed4dfcd0ca26099`.
- Inner model `bpmn-embedded:2098c54849a9f9364b861dc8d29bf6166225d1a6bd50093ed07f8d3d212b7fbe`.
- Native RunEtl worker `wfexec:start:v1:0e7161e8cf829ea01282f9bda47dc9f4db31714f89b90df4264d387855b70612`.
- ETL run `etl-0c01015ac05338d8e7775918207ee230620d5c7c22ca002aec2d1661a074705a`.

The controller submits only the normal outer BPMN HTTP cancel request. Its retained revision2 receipt is Accepted/TerminalPendingSettlement. No native or ETL cancellation API is used to substitute the direction. The native pipeline cancellation intent's complete payload equals the original ETL `InvocationJson`, including artifact, scope, original result address and parent pins. The actual terminal is Cancelled; no fault or previous terminal state is relabelled. Both workers finish Cancelled; both models finish Cancelled/TerminalSettled with their original scope, tenant, authority, protocol, full executable pins, material incarnation and creation identity unchanged. Each retained cancellation origin equals its original native work origin. Exactly two native workers and two model rows remain; no second logical admission appears.

The actual ETL run keeps its original invocation fingerprint/JSON, binding JSON, alias/revision/handle values and historical two accepted effects. Its terminal has zero outstanding records/bytes, zero rejected/filtered records, no preparation or partition owner/expiry, no owed batch IDs and no reservations. The terminal outbox has one acknowledged delivery, no live owner/expiry and **ProvenParentTerminal** disposition2. That is the truthful native-parent-terminal proof, not an invented consumed typed completion. The original native RunEtl parent is physically Cancelled.

The connector run and partition rows are exactly unchanged from the original waiting input. In particular, the old sibling's waiting429 position is retained as history; it is not falsely described as completed compute. The run-level Cancelled disposition fences that history. The selected physical EF claim/acceptance paths require RunStatus.Running (`EfEtlConnectorRuntimeStore.cs:173–185` and the corresponding acceptance paths). There is no remaining Running run, active lease, owed batch or unacknowledged terminal delivery.

## Actual settlement and delivery ordering

These UTC values were independently read from the closed Runtime/ETL databases:

| Event | UTC |
|---|---|
| Outer cancellation accepted, revision2/pending | 20:31:10.996729 |
| Inner cancellation accepted, revision2/pending | 20:31:11.798377 |
| Actual ETL terminal Cancelled | 20:31:12.544592 |
| Exact `Elsa.Etl.Cancel.v1` source Delivered | 20:31:12.587018 |
| ETL terminal delivery acknowledged, ProvenParentTerminal | 20:31:12.677657 |
| Inner native result accepted, revision3 | 20:31:12.799084 |
| Exact `CancelEmbedded` source Delivered | 20:31:13.041790 |
| Inner parent settlement accepted, revision4/TerminalSettled | 20:31:13.327959 |
| Inner native-result source Delivered | 20:31:13.790750 |
| Outer native result accepted, revision3/TerminalSettled | 20:32:11.902309 |
| Outer native-result source Delivered | 20:32:15.323105 |
| Outer model cancellation source Delivered | 20:32:15.326516 |

The native result inventories name exactly the actual owed child sources: W2's result inventories ETL Cancel; W1's result inventories CancelEmbedded. Their accepted model results retain the exact original typed payloads. ETL cancellation/terminal acknowledgment and inner native-result acceptance precede CancelEmbedded acknowledgment. Inner revision3 still owes its native-parent return; revision4 settles that separate parent responsibility. Therefore CancelEmbedded being acknowledged before revision4 is not premature ETL settlement and does not require a circular parent-ACK dependency. Outer settlement follows the inner work and parent settlement.

At the completed capture, the outer cancellation source and outer native-result source are still FailedRetryable/unacknowledged even though the outer model has consumed its result. At the retained cold cut both are Delivered. This distinguishes semantic settlement from transport ACK instead of claiming all sources were acknowledged at the completed cut. Retained native/model scheduler queues and native bookmarks are empty. All retained BPMN/ETL source rows are Delivered.

## Cold retention and scope

Two ordinary owned Workbench processes, PIDs93657 and94368, have recorded SIGKILL exits-9; neither original process remains. Both complete full API views are identical before and after the cold restart. Physical model/worker headers, original start sources and result-input receipts are unchanged across that pair.

The completed and retained **ETL Runtime and ETL Design** files are byte-identical. The combined Runtime/Design files are **not** byte-identical. Their row differences are exactly two ordinary outbox delivery envelopes and the Activity dependency projection's rebuild metadata (`RebuildId`, `Sequence`, `AsOf`, `ConcurrencyToken`, `LastModifiedAt`); projection Items are identical. No model/native progression or material/authority mutation is hidden by a whole-file equality assertion.

The actual host has normal ETL Runtime, EF persistence, API and native adapter features enabled, with empty FileBindings/HttpBindings. This is an ordinary supported persisted-terminal-delivery mode: `EtlRuntimeWorker.cs:71` and153–156 explicitly keep terminal discovery/delivery alive without compute scopes. The old stored bindings and connector material are untouched. No clock, state, queue, claim, result, authority or frozen endpoint was rewritten to make the run pass.

This establishes the missing actual changed-host cancellation through the original pending model/native/model/native/ETL chain, plus coupled cold retention, with compute connectors unavailable. It supplies the narrow A1 correction within the delivered runtime scope. It is not new current-source HTTP processing/retry, active connector-effect/race, re-enabled-connector execution, Studio, other-provider, cluster or broader #2908/program acceptance. Historical HTTP wire/two-effect observations retain source9508; they are not promoted to current-source connector execution. Existing guard controls provide their own adverse-order evidence; this actual run does not invent an executed reverse-order counterexample.

Private machine-readable checks and provenance: `validation.json`, `inputs.json`, `configuration.json`, `product-verification.json`, `projection-difference.json`, `audit.py` and `audit.log`. The first private audit compared an encoded Runtime header origin with an opaque normal-string origin and failed that observer assertion; `audit-encoding-diagnostic.log` retains it. The corrected comparison matches each retained cancellation origin to its original opaque native-work origin and separately verifies the unchanged encoded native header. That was an audit encoding mistake, not a product failure.

The Git head remains f83. Parent packaging has created untracked runner/evidence files for the future commit; this validation did not read other current reviewers' outputs or alter those files. Portable packaging and the subsequent exact-head full review remain due. The original round11 report was not changed.
