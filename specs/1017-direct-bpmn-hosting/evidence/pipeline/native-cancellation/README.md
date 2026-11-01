# Actual native worker cancellation into ETL

At **78b1e582793d6e76410bc357aa55c687bc3e8e1d**, a real direct BPMN process starts its published RunEtl native worker. Shard A delivers two records while shard B retains an actual `429 Retry-After: 60` wait. Consistent whole-store backups preserve that cut, then the controller kills its owned Workbench with SIGKILL (exit -9).

A fresh Workbench opens the same unedited stores. The controller submits the normal public **CancelWorkflow alteration plan for the actual native worker**, rather than calling the ETL cancel API. The original native activity/workflow becomes Cancelled. That cancellation checkpoint owns both the exact `Elsa.Etl.Cancel.v1` source and its BPMN native-result source. The ETL cancel payload equals the original frozen ETL invocation and full result address; both sources retain the same actual checkpoint identity. The ETL run becomes cancelled with two accepted records, no rejections and no outstanding records/bytes. The model consumes its exact native-result input at revision2 before source acknowledgment and remains Cancelled.

The ETL result is acknowledged with **ProvenParentTerminal**: its native parent is already Cancelled. This differs from the consumed typed ETL completion receipt in the [pipeline-originated cancellation](../direct-cancellation/README.md) journey. Verification requires the actual delivered ETL cancel source here; a cancelled native header alone is insufficient. No typed ETL completion consumption is invented.

A third standard Workbench remains running past the original saved retry deadline. Native/model/ETL API and checked observations remain unchanged, and the physical HTTP fixture receives no second shard-B request. The two effects already delivered remain intact. The original model/native/ETL pins, material incarnation, native invocation, model invocation/attempt and frozen return addresses survive. The [manifest](manifest.json) binds **19 raw assets**, including six complete coupled stores, API/wire views, checked reads and source/process provenance. Twelve selected Foundation runtime/BPMN/ETL products identify the exact source. All three owned Workbench processes and the HTTP fixture are stopped; private paths/ports are historical evidence, not reproduction bindings.

## Reproduction and checks

Use one clean committed source and the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --native-cancel \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>

python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/native-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_http_recovery.py -v
```

The actual journey passes on the existing runtime; no production semantics needed changing. Twenty-one portable tests pass across the native/direct recovery and both cancellation families, zero skips. Three new rehashed corrupt-copy controls reject a missing ETL cancel source, a redirected frozen ETL address and a cancel source from another checkpoint. All six stores pass integrity and all104 frozen baseline assets remain unchanged. [Qualification](qualification.json) retains scope and check hashes. The legitimate test-only main progress6ad81f7c7 was adopted before this capture; earlier receipts keep their own sources.

## Remaining acceptance

This proves **native worker → ETL cancellation**, with the resulting native cancellation also reaching direct BPMN, on EF SQLite and the built-in single-node actor. The model-to-worker cancellation producer is not implemented by this receipt. Cancellation before native worker admission, pending cancel/result crashes, RunBpmn service embedding, held-fault retry, additional fault/race/provider criteria and final affected-source upgrade/Studio remain open. T035/T039/T040, #2898/#2907 and the program remain incomplete. Full-PR review rounds remain0 of6; exact-head required checks and protected merge gates remain separate.
