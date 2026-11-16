# Actual ETL workers against duration boundaries

Executing source: `8dc088bae4463b8a9279a1f25036ac14b1715cd6`. Fifteen production Workbench journeys cover worker-first, timer-first and interruption of a held failure, directly and through native RunBpmn Root/Sequence/Flowchart/CompensationScope. All use the normal authenticated model/ETL APIs, real published native `RunEtl` workers, actual HTTP responses and the existing delayed outbox. This increment changes capture/verification only; the production duration implementation retains its earlier source.

Every case starts the original native worker, observes an actual HTTP429 wait after the sibling delivered two output records, quiesces its owned host to capture consistent whole stores, then kills that host. A fresh standard host recovers the original timer, model/worker pins, ETL invocation and native parent registration where present.

| Order | Actual behavior |
|---|---|
| Worker first | The worker resumes after HTTP Retry-After20 and completes all four records before its60-second boundary. The model's winning completion survives a second SIGKILL. The original timer later receives Rejected/execution-terminal without advancing the winner. |
| Timer first | The10-second timer wins while the original HTTP response prescribes Retry-After60. Its actual cancellation reaches the native worker and ETL run; their Cancelled result settles the model's existing cancellation responsibility. The previous two effects remain and the original delayed HTTP retry never occurs. |
| Held interruption | After HTTP Retry-After20/process loss, the temporarily unavailable original lookup snapshot causes a genuine `etl.snapshot-read-failed`. The model holds its exact active interpreter snapshot and failed attempt at revision2. Another SIGKILL, repair of the unchanged external snapshot and cold recovery preserve that state. The90-second timer then retires the exact incident and follows its boundary route. The original native/ETL fault and receipt remain unchanged; subsequent retry is rejected without a new worker or run. |

All four native parents consume the model's actual Completed/Done result through their original typed RunBpmn registration. The actual successor runs once where present; exact model parent-settlement precedes source acknowledgment. A fourth standard host retains identical model/worker/ETL/incident/API/receipt/source/queue and physical HTTP views. These are real ordered decisions; simultaneous competing commits remain separate acceptance.

The [manifest](manifest.json) binds488 raw assets and135 consistent whole stores: Runtime/Design plus ETL Runtime and ETL Design at before, middle and finished cuts. All75 listed children are stopped:60 Workbench and15 HTTP fixtures. There are30 actual SIGKILL predecessor exits. Portable checks read each store immutably, validate the original worker/header/origin and native parent pin, full version4 material/policy/timer inventory, typed native completion, ETL invocation/terminal/outbox, exact model input receipts, source deadlines and source acknowledgments. No execution state or clock is edited.

Interpreter histories compare across direct/all four native parents for each order and phase. Normalization is limited to a `producingWorkHandle` equal to that model identity and a `stoppedHandle` equal to its independently verified original duration or native-work invocation. Every raw identity, source, attempt and receipt remains checked separately. Unknown handles and semantic differences are not normalized away.

The [diagnostics](diagnostics/manifest.json) retain two original limits at source`ffc00efa4`. A40-second held timer expired during third-host recovery, producing an HTTP observation whose incident was already Retired; its actual store retained valid revision3 completion. In another case host recovery delivered a10-second timer after the original20-second HTTP retry was due: the timer won the model decision, while the physical worker's actual Completed result was preserved for settlement. These are excluded from final qualifying counts; their15 whole stores and seven stopped children remain intact. Final fixture windows distinguish pre-deadline held recovery and physical cancellation before HTTP retry. Neither the runtime nor the classifier was altered to fit an expectation.

Run from any checkout:

```sh
python3 e2e-tests/etl/verify_service_duration.py --manifest specs/1017-direct-bpmn-hosting/evidence/races/service-duration/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify_*.py'
```

Reproduce after building the hosting test project from clean committed source with `-p:ElsaBpmnRecoveryProbe=false`. Run `qualify_http_recovery.py --parent BpmnModel --service-timer worker-first|timer-first|held-interruption` with built Workbench/DeliveryRecoveryRead assemblies and a fresh output directory. Held interruption additionally requires `--technical-fault --hold-service-retry --business-error-boundary matching`. Add `--embedded-parent Root|Sequence|Flowchart|CompensationScope` for native cases. Historical paths and ports are provenance only; reproduction creates fresh private stores/bindings.

[Qualification](qualification.json) retains portable224/224 including16 new positive/corrupt-copy controls, existing BPMN tools27/27, source-matched standard host build0 errors, maps/123 test-tier registrations and104 unchanged frozen baseline assets/original manifest. Earlier431 BPMN, actual human/timer2, human-host9 and native-service1 passes keep source`a2c2e47751d595c9489f889593ed4d7427ef6076`. Simultaneous retry/cancel commits, exact direct-model broadcast/provider/retention/rollback and final affected-source upgrade/Studio acceptance remain open. #3242 remains undecided. #2898/#2907/program#2892 stay open/Partial; full-PR review rounds remain0 of6.
