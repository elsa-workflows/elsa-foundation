# Actual ETL cancellation through the native worker and BPMN

At **b5c612276334d1d39aec827b85d12cb1021e5c38**, a real direct start/service/end BPMN process runs one closed published native RunEtl root. The real HTTP source receives `429 Retry-After: 60` for shard B while shard A completes and delivers its two records. The controller captures the complete coupled stores and kills only its owned Workbench with SIGKILL (exit -9). No state or clock is edited.

A fresh Workbench resumes the original invocation. The ordinary **ETL cancel API** accepts cancellation while the run is Running. The pipeline becomes `cancelled` with two accepted records, no rejected records and no outstanding records/bytes. Its typed ETL return consumes the original RunEtl registration, making the actual native activity/workflow Cancelled. The native model-work result carries status Cancelled and no completion result; BPMN consumes that exact result at revision2 and retains Cancelled. Its physical input receipt precedes result-source acknowledgment. A cancelled header alone is not accepted as typed ETL consumption: verification also requires the physical Consumed receipt with the exact ETL registration, provider and completion type.

A third Workbench opens the same unedited stores. Ordinary pumps remain running beyond the **original saved HTTP retry deadline**, then the API, checked-provider and HTTP receiver views still match. No second shard-B request occurs. The two effects already delivered remain intact. Cancellation preserves the partial partition history; it does not relabel the unfinished shard as exhausted or undo accepted external output. HTTP effects retain their at-least-once contract.

The original native/model invocation and attempt, full executable pins, model material incarnation, ETL format2 publication and result address survive. The [manifest](manifest.json) binds **19 raw assets**, including six complete Runtime/ETL Runtime/ETL Design snapshots, normal API/physical HTTP observations and checked model/native reads. Twelve selected Foundation runtime/BPMN/ETL products identify the exact source above. Three owned Workbench processes and one HTTP fixture were used; all four journey children are stopped. Private paths/ports are historical provenance; reproduction creates fresh resources.

## Reproduction and checks

Build the standard Workbench and checked reader from one clean committed source through the build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --pipeline-cancel \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>

python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/direct-cancellation/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_http_recovery.py -v
```

The actual journey passes. Focused native checkpoint/result delivery, cancellation-race and API tests pass72/72, zero skips. Seventeen portable checks pass across this receipt and the earlier native/direct ETL receipts. Six new rehashed corrupt-copy controls reject a native cancellation relabeled Completed, erased accepted-record accounting, an observation before the saved deadline, a late HTTP retry, a physically Completed native header, and a missing typed ETL consumption receipt. All six stores pass integrity; all104 original baseline asset hashes remain unchanged. [Qualification](qualification.json) retains commands, source distinctions and check hashes.

## Remaining acceptance

This proves **pipeline → native worker → direct BPMN cancellation** after same-source process loss, on EF SQLite and the built-in single-node actor. Reverse model-to-worker cancellation, cancellation before native worker admission/during work/pending result, RunBpmn service embedding, matched errors, technical faults, worker/timer races, held terminal-fault retry and final affected-source upgrade/Studio remain open. The earlier success/BusinessError, human embedding and pre-admission native-root cancellation receipts retain their own sources. T027/T029/T031/T034–T041 and broader #2898/#2907/program acceptance remain incomplete. Draft/full-PR rounds remain0 of6; exact-head CI, security/CLA and protected review/merge gates remain separate.
