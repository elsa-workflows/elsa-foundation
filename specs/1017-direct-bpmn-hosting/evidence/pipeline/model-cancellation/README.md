# Actual BPMN cancellation through the admitted native ETL worker

At **6f6bf1786cb6fbf290caac500c4737f4721b2a96**, direct BPMN publishes and starts a closed native RunEtl service worker. A real HTTP source delivers shard A's two records while shard B retains `429 Retry-After: 60`. The controller records the coupled committed stores and kills its owned Workbench with SIGKILL (exit -9).

A fresh standard Workbench opens the same unedited stores. This time the public **BPMN cancel API** targets the model execution. Its exact command receipt at revision2 retains `TerminalPendingSettlement`; it closes semantic progression, preserves the cancellation reason and owns `Elsa.Bpmn.CancelNativeWork.v1` for the original native invocation/attempt. The shared durable queue delivers an ordinary native Cancel command to that worker. No ETL cancel API or native alteration plan is submitted by this controller.

The native cancellation checkpoint owns both its actual `Elsa.Etl.Cancel.v1` delivery and typed native/model result. The result retains the complete downstream cancellation source inventory, including ETL's existing intent handler. The qualified physical reader requires those deliveries to be acknowledged before consuming the exact native-result input. Model revision3 then records the native worker's actual Cancelled result, marks the original cancellation responsibility settled and reaches `TerminalSettled`. Only afterward can the model cancellation source be acknowledged. The original revision2 command receipt remains an acceptance decision; it is not rewritten to imply settlement at request time.

The actual ETL run is cancelled with two accepted records, no rejections and no outstanding records/bytes. Its return acknowledgment is `ProvenParentTerminal`; no typed ETL completion consumption is invented. A third standard Workbench remains stable beyond the saved retry deadline. No second shard-B request occurs, and the two completed HTTP effects remain. The full model/native/ETL pins, material incarnation, native invocation, model invocation/attempt and frozen return address survive. All three Workbench processes and the HTTP fixture are stopped.

The [manifest](manifest.json) binds **20 raw assets**, including six complete coupled stores, the public cancellation response and retained command, API/wire observations and checked model/native reads. [Qualification](qualification.json) retains scope and checks. The first real capture exposed an empty downstream inventory because ETL's existing cancellation handler does not declare the newer required-source protocol. The corrected source captures every other terminal-checkpoint intent, validates the final inventory and waits for its actual delivery. The verifier rejects the first capture and an equivalent fully rehashed corrupt copy. That first capture is not promoted as acceptance evidence.

## Reproduction and checks

Use a clean committed source and the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>

python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/model-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_http_recovery.py -v
```

The actual journey passes. BPMN tests382/382, focused ETL boundary/API/race tests72/72 and portable comparisons/corrupt controls26/26 pass, zero skips. All104 original baseline assets and the frozen authoring fingerprint remain unchanged. Existing authoring material and state documents1/2/3 remain readable; document4 retains the new model-owned cancellation responsibility. The additional default-refusing cancellation method has its exact package API entry and a clean Core build.

## Remaining acceptance

This receipt proves **BPMN → already-admitted native worker → ETL cancellation after restart**, on EF SQLite and the built-in single-node actor. It does not prove cancellation before native worker admission. In that cut the model closes progression, the existing atomic admission condition prevents a late child, and start/cancel responsibility remains owed until a positive retirement mechanism is implemented; a missing native header is never treated as acknowledgment. Pending cancel/result crash cuts, truthful already-terminal races, RunBpmn service embedding, held-fault retry, other graph/provider criteria and final affected-source upgrade/Studio qualification remain open. T035/T039/T040, #2898/#2907 and the program remain incomplete. Full-PR review rounds remain0 of6; exact-head required checks and merge gates remain separate.
