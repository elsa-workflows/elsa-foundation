# Actual old native results while a replacement is live

Executing source: `9380009b86f3ceb383b7b58b6317b2a90f94b17d`. These five standard owned Workbench journeys use direct BPMN and native RunBpmn Root/Sequence/Flowchart/CompensationScope, with probes disabled and the production resumption sweep at0.2 seconds. Production runtime/interpreter/persisted writers are unchanged by this increment.

Each journey preserves its actual held ETL/native fault across two SIGKILLs, then authorizes one retry. The replacement has the same semantic invocation and a different physical attempt/worker/run. The owned loopback HTTP fixture rearms its existing429 response once, giving this replacement a real Retry-After20 wait after two physical output effects. No execution store or clock is edited. The controller waits for an unclaimed durable fetch checkpoint and quiesces only its owned Workbench while the production SDK model actor/mailbox handles three transports:

| Transport | Actual result |
|---|---|
| Exact original failed native-result bytes and input identity | Accepted replay; original revision2 receipt remains exact, current model stays revision3/Running. |
| Same old identity with one explicitly changed fault code | Refused because it differs from the committed terminal worker/source; no new model input. |
| Unchanged old result sent to a new input identity | Refused because it does not address that exact invocation result; no receipt at the new address. |

Every probe retains full before/after model state, current physical origin, original failed worker, original accepted receipt/source ACK and empty model queue. The replacement remains in its real wait until its own deadline. Three coupled whole stores at that boundary verify the same live model/native worker/ETL run beside the original failed attempt. Later standard-host processing completes exactly that replacement and the original native parent, retains typed consumption/settlement/ACK, and stays unchanged in a fourth process. Six physical HTTP records include the prior two again; external effects remain at least once.

The [manifest](manifest.json) binds211 raw assets and60 whole stores, covering15 actual transports. All30 listed children are stopped. Existing held-retry checks still verify the complete model/worker material closure, exact logical/physical attempts, immutable ETL preparation, failed and replacement result proofs, native parent registrations and unchanged interpreter histories across direct/all four parents. Additional checks bind the transported commands to the real old source, compare every per-probe receipt/source/model, inspect the physical live model/input/native/ETL rows, and verify the replacement obeys its second actual429 response's delay.

The [diagnostic](diagnostics/manifest.json) retains the first observer failure at its original source: the collector accessed a transient partition before its fetch checkpoint existed. No old-result SDK probe ran. Its nine whole stores/four stopped children are excluded from qualifying counts. The collector now waits for a real unclaimed retry checkpoint. The portable reader was also corrected to use the actual model Running lifecycle and physical UTF16 column encoding; no capture was rewritten to fit an expected encoding.

Run from any checkout:

```sh
python3 e2e-tests/etl/verify_held_service_retry.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/obsolete-service-results/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

Reproduce after a standard committed-source hosting-test build with `-p:ElsaBpmnRecoveryProbe=false`: run `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --hold-service-retry --obsolete-result-controls`, supplying built Workbench, DeliveryRecoveryRead and NativeWorkDispatch assemblies and a fresh output path. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for parent cases. The loopback fixture alone enables its one-shot rearm control. Historical paths/ports are provenance; reproduction creates fresh private stores and bindings.

[Qualification](qualification.json) retains portable208/208 including16 new positive/corrupt-copy controls, source-matched standard host build0 errors, maps and123 test-tier registrations; zero skips. All104 frozen baseline assets and the original manifest remain unchanged. Earlier BPMN418/418, actual direct/native human-host9/9 and real scoped Identity authorization keep source`1a10565935b568e42c8431158bfafe77b571c775`. This increment does not qualify timer interruption, competing retry/cancel commit orders, remaining model-broadcast/provider/retention/rollback or final affected-source upgrade/Studio acceptance. #3242 remains undecided. T036/T037, #2898/#2907 and program#2892 remain incomplete; full-PR rounds remain0 of6.
