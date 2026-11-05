# Actual direct BPMN / native ETL recovery

Two production Workbench journeys at **18ac139e055b7e9af4573b720086c989884a9f48** demonstrate the first real direct model/native worker boundary. BPMN publishes and starts a plain start/service/end process whose service binding retains the complete pin of a published **RunEtl native root**. The controller starts BPMN; the runtime admits its actual native worker. No substitute native parent, bookmark or execution row is constructed.

| Real journey after process loss | ETL and physical HTTP output | Native root completion | BPMN result |
|---|---|---|---|
| Direct service success | 4 accepted, 0 rejected; 4 mapped records | Completed / Done; typed RunEtlResult | Completed |
| Direct service rejected record | 3 accepted, 1 rejected; 3 mapped records and 1 safe JSONL error | Completed / BusinessError; typed RunEtlResult with etl.rows-rejected | Faulted: unmatched modeled business error |

Both use the delivered HTTP source/receiver. A real `429 Retry-After: 20` holds shard B while shard A exhausts. The controller briefly quiesces its owned Workbench for read-only provider backups, then kills it with SIGKILL (exit -9). A fresh standard Workbench opens the same unedited stores, resumes the original RunEtl invocation and completes its native root. The next HTTP request respects the original twenty-second delay. The complete worker/model pins, incarnation, model invocation/attempt and frozen return input remain unchanged. No state or clock is edited.

The native terminal checkpoint commits its typed root result and result outbox together. Ordinary shared queue/resumption consumes the exact model input; only its retained receipt permits outbox acknowledgment. The result is compared with the authoritative native root completion in the physical store. This catches the important distinction: native **Completed** can carry ETL **BusinessError**, and must not become BPMN success. A third Workbench preserves the exact API, checked-reader and receiver observations.

The actual process-loss attempt exposed a Balanced persistence defect: coalescing already materialized the result intent as outbox changes, while the new terminal guard checked only the original intent list. The fixed guard requires the applied, correctly scoped delivery responsibility. Provider regressions accept the folded outbox and reject an intent without an outbox or a foreign source scope. Earlier publication and runner-composition findings were also fixed before these retained passing runs; [qualification](qualification.json) records them.

The [manifest](manifest.json) binds **36 raw assets**, including twelve complete store snapshots, normal API/wire observations, safe-error output, checked provider reads and source/process receipts. Six Workbench processes and two owned HTTP fixtures were used. All twelve selected Foundation runtime/BPMN/ETL products identify the exact source above. Source main files were unchanged by provider backups; Python opens only closed hash-verified copies for portable comparison. Temporary paths and ports are historical observations, not portable live bindings.

## Reproduction and checks

Build `Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj` in Release with `-p:ElsaBpmnRecoveryProbe=false` at one clean committed source. This builds the standard Workbench and checked reader. Node runs the existing loopback fixture. The runner selects the explicitly supported built-in single-node actor and private SQLite stores; it stops only its own children.

```sh
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Repeat with `--business-error` for the rejected-record case. Reproduction creates fresh resources. Do not restore the captured private ports or rewrite the retained pins.

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/direct-http-recovery/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_http_recovery.py -v
```

Portable checks verify both this bridge and the earlier native reference. Rehashed corrupt copies reject a relabeled typed BusinessError, acknowledgment without consumption, a foreign physical model receipt fingerprint and a redirected physical worker return address. Prior native controls still reject early HTTP retry, a graceful shutdown called process loss and a foreign native ETL result address. Ten tests pass with zero skips. At the executing source, direct hosting tests pass2/2 and the full Runtime EF suite passes1770/1770, zero skips. All104 frozen baseline assets remain unchanged.

## Remaining acceptance

This is one direct start/service/end with one closed native worker on EF SQLite and the built-in single-node actor. It proves same-source process-loss recovery and an unmatched business-error mapping. It does not prove upgraded-host ETL recovery, RunBpmn embedding, matched error catches, broader service/event shapes, cancellation propagation/settlement, held terminal-fault retry, clustered execution or direct-model Studio. HTTP effects retain their at-least-once contract. T027/T031/T034–T041 and the rest of #2898/#2907/program acceptance remain open. Earlier Studio/native reference receipts retain their own sources; final affected-source upgrade and Studio qualification must be repeated after bridge implementation.
