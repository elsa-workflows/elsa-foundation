# Actual default technical-fault recovery

Five production Workbench journeys at **5533b8367c7fe78850677fcebf5c7fcb0f2e7e27** retain a real ETL technical failure through direct BPMN and native RunBpmn Root/Sequence/Flowchart/CompensationScope parents. No production runtime, ETL classifier, binding DTO or persisted writer changed for this increment.

Each published ETL definition uses the actual two-shard HTTP source, HTTP output and an immutable CSV lookup. Shard A completes two physical output effects while shard B waits on HTTP429 with its original twenty-second Retry-After. The controller captures the original coupled stores, kills its owned Workbench with SIGKILL and temporarily renames only the retained lookup snapshot. The immutable source CSV remains available; both source and snapshot bytes are retained and hashed. No database row or clock is edited.

A fresh standard host attempts to read that exact prepared lookup, records the existing technical diagnostic `etl.snapshot-read-failed` and settles ETL Faulted. The same native RunEtl root faults without a fabricated completion payload. Its terminal checkpoint retains the actual model-result source; the exact model receipt precedes acknowledgment. BPMN feeds this uncoded fault to released 0.5.0 semantics and ends Faulted / bpmn.work.faulted with null errorCode. The authored boundary declares etl.rows-rejected and does not catch the technical fault.

| Parent | Actual terminal disposition |
|---|---|
| Direct | ETL Faulted, native worker Faulted, model TerminalSettled/Faulted |
| Native Root | RunBpmn and workflow Faulted, one blocking child incident |
| Sequence | Workflow Faulted; Sequence activity observed Running; one blocking child incident |
| Flowchart | Workflow and composite Faulted; both original blocking incidents retained |
| CompensationScope | RunBpmn Faulted; default scope/workflow Completed with Compensated; child incident Resolved/AbsorbFault |

The existing Sequence status question [#3242](https://github.com/elsa-workflows/elsa-foundation/issues/3242) remains undecided. No forward successor runs in these fault cases. A third standard process keeps the exact state and physical HTTP observations unchanged past the original HTTP retry deadline plus two seconds. Only after all hosts stop is the retained lookup path restored; its SHA-256 still matches its original bytes. The host uses the supported 0.2-second production resumption sweep; no probe is enabled.

## Evidence

The [manifest](manifest.json) binds **117 qualifying raw assets and 30 whole stores**, complete immutable pins, original invocation/attempt/registration, native and model receipts, physical fault incidents, exact sources and parent settlement. Waiting/final interpreter states compare across direct/all four parents after normalizing only the model's self-referential producingWorkHandle; all twelve captured first-party product versions/hashes match. Fifteen Workbench processes and five HTTP fixtures were used; all 20 listed children stopped. No external-library binary hash is claimed by that product inventory.

Two [diagnostic counterexamples](capture-summary.json) retain 28 separate raw assets and 12 stores at their original sources. The first established that HTTP400 is an ETL BusinessError, correctly caught by the declared business boundary. It was not changed into a technical failure. The second captured a real snapshot fault but rejected an observer assumption that the completed shard's captured fetch was absent. Its distinct current fetch is now inspected separately from the waiting shard. None is counted as qualifying technical recovery; all 30 rejected listed children stopped.

The prepared lookup key/hash, immutable binding and published lookup definition are checked in both physical ETL stores against the actual retained CSV bytes and unavailability receipt. Physical native/model state, typed-consumption receipts, fault code/incident inventory, terminal ETL delivery, output accounting and unchanged delayed HTTP work are also checked. The [actual pre-policy model material](legacy-model-v2.json) is derived read-only from the original direct store and retains its original content hash for subsequent policy compatibility checks. Its source store and digest are recorded; it is not a replacement execution witness.

[Qualification](qualification.json) binds the full validation logs and exact source scopes. The source-matched standard host build has zero errors. Portable controls pass 128/128, including this positive matrix and eight new rehashed negative controls. Maps and all 123 test-tier registrations pass. All 104 frozen baseline assets and their manifest remain unchanged. Earlier BPMN 400/human-host 8 results retain source `663464b61`; no .NET production source changed here, and those results are not relabeled as this executing source.

## Reproduction

Build a clean committed source through the normal build-slot wrapper and run against fresh owned output paths:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py --parent BpmnModel \
  --technical-fault --business-error-boundary matching --embedded-parent Root \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> --output <fresh-owned-output>
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/technical-fault-native-recovery/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_*.py'
```

Omit embedded-parent for direct execution, or select Sequence, Flowchart or CompensationScope. Capture paths/ports are historical provenance; reproduction creates fresh resources. Do not restore old loopback bindings or mutate captured stores.

## Limits

This proves the existing default FinalFault path; it does not prove model-held retry, a retry command's final-state refusal, upgraded-host recovery, other providers, remaining timer/broadcast/race/retention/rollback arms or final Studio acceptance. Default fault is terminal. The approved HoldForRetry path must instead preserve active interpreter state before feeding a semantic fault, retain this original failed-run evidence and admit exactly one new physical attempt for authorized retry. T036/T037/T039/T040 and #2898/#2907/program acceptance remain incomplete. HTTP effects remain at least once.
