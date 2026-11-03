# Competing held retry and cancellation recovery

Executing source: `7414d8a13ee6cd2f65923914065b075602437ca2`. Ten actual production journeys cover retry-first and cancel-first directly and through native RunBpmn Root/Sequence/Flowchart/CompensationScope. This increment adds capture and verification only; it changes no production model, native or ETL semantics.

Every journey publishes the real native RunEtl worker and model with HoldForRetry/2. The original worker receives actual HTTP429/Retry-After20 after its sibling delivers two output records. Its host loses its process through SIGKILL. Temporary unavailability of the unchanged retained lookup snapshot produces genuine `etl.snapshot-read-failed`; the original model holds its exact interpreter state at revision2, while the native worker and ETL run retain their actual fault. A second SIGKILL preserves that failure. The external snapshot is restored with identical bytes before the competing commands.

Separate owned production-SDK clients submit supported retry and cancel commands through the normal actor/mailbox. The first process pauses an unchanged real revision-two proposal at the existing commit hook, with its actual ownership lease recorded. The second process durably queues its command, acquires successor ownership and returns pending: the protected FIFO head is still claimed. Releasing the first writer cannot commit its stale proposal. Both inputs remain pending, in their actual original queue order, with the unchanged held model and no additional HTTP effects. Whole stores retain the greater physical fencing token and both queue payloads before cold recovery.

| Queue order | Actual cold recovery |
|---|---|
| Retry first | Exact retry receipt at revision3 creates one bounded replacement invocation for the same semantic work. Exact cancellation at revision4 closes progression. The existing start/cancel delivery path positively retires that original replacement start at revision5, with no native worker or ETL run admitted. Start/cancel acknowledgments follow the exact retirement receipt. The failed attempt remains Retried with its replacement ID; its physical fault is unchanged. |
| Cancel first | Exact cancellation at revision3 retires the held failure. The queued retry receives Rejected/execution-terminal at the same revision, without creating a replacement or cancellation responsibility for the already-faulted worker. |

All four native parents consume the original typed Cancelled RunBpmn result, preserve their actual Cancelled disposition and run no successor. Exact model parent settlement precedes result acknowledgment. A fourth standard host retains identical model/worker/ETL/incident/API/receipt/source/queue and HTTP views. Both orders preserve the original fault and previous two physical HTTP effects; neither admits a new worker or ETL run.

This is actual concurrent submission, successor fencing and protected FIFO recovery. The queue prevents two simultaneous semantic proposals for the same model; the capture does not bypass it or manufacture simultaneous database transactions. Receipt times prove that both final decisions were consumed by fresh hosts after the unchanged proposal was released, before source acknowledgment. Earlier native-admission/model-cancellation races retain their separate actual competing-proposal evidence.

The [manifest](manifest.json) binds412 raw assets and120 consistent whole stores: Runtime/Design plus ETL Runtime and ETL Design at before, held, concurrently queued and finished cuts. All70 listed children are stopped:40 Workbench,20 SDK and10 HTTP fixtures;20 predecessor exits are actual SIGKILL. The portable verifier checks every store immutably, complete original model/worker pins and authority, unchanged held interpreter state, bounded policy/dependency material, protected input payloads, physical successor fence, exact command/result receipts, original ETL invocation/terminal/native-delivery outbox, positive replacement non-admission and source ACK, and original native-parent result/settlement. No execution state or clock is edited. Negative controls operate only on explicitly corrupted copies, with hashes and provider receipt digests recomputed.

Run from any checkout:

```sh
python3 e2e-tests/etl/verify_held_command_race.py --manifest specs/1017-direct-bpmn-hosting/evidence/races/held-command-race/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

Reproduce from clean committed source after building the hosting test project with `-p:ElsaBpmnRecoveryProbe=false`. Use `qualify_http_recovery.py --parent BpmnModel --technical-fault --hold-service-retry --business-error-boundary matching --held-race-order retry-first|cancel-first`, built Workbench/DeliveryRecoveryRead/NativeWorkDispatch assemblies and a fresh output directory. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for native cases. Historical paths and ports are provenance only; reproduction creates private stores and bindings.

[Qualification](qualification.json) records the source-matched host/SDK build0 errors, portable ETL240/240 (16 new positive/corrupt-copy controls), BPMN tools27/27, unchanged104 frozen baseline assets/original manifest and maps/123 tier results, zero skips. Prior unit and host results retain their executing sources; no earlier result is relabelled as this source. Direct-model broadcast/provider/retention/rollback and final affected-source upgrade/Studio acceptance remain open. #3242 is still undecided. T023/T024 and broader tasks, #2898/#2907/program#2892 remain open/Partial; full-PR review rounds remain0 of6.
