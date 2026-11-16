# Actual held-service retry after process loss

Executing source: `25bec5ade15d98fa4be0259acdb522e2bd07ff95`. Production policy/driver/API source: `6f532c6edfe4931ce0d574e0c98c5d93cb012d81`; the intervening change adds the observer and waits for replacement-result ACK. These are standard owned Workbench processes with the production resumption sweep at 0.2 seconds and probes disabled.

Five actual journeys use direct BPMN and native RunBpmn Root/Sequence/Flowchart/CompensationScope. The model pins HoldForRetry with two total attempts, one closed published RunEtl worker, an immutable CSV lookup and the coded `etl.rows-rejected` boundary. After a real HTTP429 wait with two completed HTTP output effects, the first host is killed. The owned retained lookup path is temporarily renamed without changing bytes; the recovered ETL/native worker faults with `etl.snapshot-read-failed`.

The model retains revision 2/WaitingIncident, its exact active token/interpreter state and original failed worker result. The native parent stays suspended at the same registration. A second SIGKILL retains this held state. Restoring the unchanged external snapshot and starting a third host does not revive the old attempt. The authorized retry consumes the exact incident/invocation/failed attempt at revision 3 and creates one different native worker and ETL run for the same semantic invocation. Identical retry content replays that receipt; conflicting content returns 409.

The replacement delivers four records and completes. Original faulted ETL/native state, native incident, pre-failure semantic snapshot, exact model consumption and failed-attempt result remain retained. The native parent's original typed RunBpmn registration consumes the model result and its actual successor completes once where present. Both worker-result sources and the native-parent source are acknowledged from exact consumption/settlement receipts. A fourth standard process retains identical checked/API views and wire effects. All25 listed children stopped.

The [manifest](manifest.json) binds 166 qualifying raw assets and 45 consistent whole stores. The verifier checks actual runtime material/pins/policy, model state and input receipts, original and replacement worker roots/results, ETL invocations/native addresses, immutable lookup preparation, native parent root shapes/dependency closures, typed consumption and source ACK. Waiting, held and final interpreter traces compare across all five cases after normalizing only the model's self producingWorkHandle. All 12 captured first-party product versions/hashes match. The new physical ETL run owns its own prepared snapshot key with the same immutable lookup bytes; it does not overwrite the failed run's checkpoint.

The [diagnostic capture](diagnostics/manifest.json) is separately retained at its original source. The first direct observer captured completed semantics while the replacement result source still owed ACK; the next standard host finished it. It does not qualify identical finished/retained source views. The repeated qualified collector waits for ACK before taking the finished snapshot. Its nine whole stores and30 raw assets are excluded from the 45/166 qualifying counts; all five listed diagnostic children stopped.

Run the portable checks from any checkout; historical paths and ports are provenance only:

```sh
python3 e2e-tests/etl/verify_held_service_retry.py --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/held-service-retry/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

To reproduce, build the hosting test project on a clean committed source with `-p:ElsaBpmnRecoveryProbe=false`, then run `qualify_http_recovery.py --parent BpmnModel --technical-fault --business-error-boundary matching --hold-service-retry` with the built Workbench/DeliveryRecoveryRead assemblies and a fresh owned output directory. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for the parent cases. Do not restore historical loopback bindings or edit captured stores.

This proves one authorized physical retry. It does not yet prove budget exhaustion, held cancellation/timer interruption, obsolete-result refusal, authorization-before-replay, remaining race/provider/retention/rollback or final affected-source upgrade/Studio acceptance. Native fault-policy decision#3242 remains undecided. T036/T037, #2898/#2907 and program#2892 remain incomplete. External HTTP output is at least once: six physical records include a repeat of the prior two successful effects.

[Qualification](qualification.json) binds the source scopes and full validation logs: BPMN 411/411, direct/native human-host 9/9, portable 149/149 with 21 new controls, maps and 123 test-tier registrations pass with zero skips. All 104 frozen baseline assets and the original manifest remain unchanged. Final affected-source upgrade/Studio and the other limits above remain open.
