# Actual cancellation before first native worker admission

Executing source **3e14b7ef7f90c8103e812eeeb21e20aeab9de56e** publishes an actual ETL pipeline, its closed native RunEtl worker and a direct BPMN service binding through ordinary authenticated Workbench APIs. The first host uses the normal recovery sweep configuration of300 seconds. BPMN starts and retains its real native-start outbox source, while no native workflow/activity/bookmark or ETL run exists. No test driver, database state edit, alternate clock or substituted worker is used.

The public BPMN cancel API records revision2/`TerminalPendingSettlement`, the original invocation/attempt and the exact cancel source. The controller captures that pending cut, then kills its owned host with SIGKILL (exit -9). A fresh standard Workbench opens the same unedited stores with a normal0.2-second recovery sweep. The original native-start handler verifies the committed cancellation and queues one protected `native-start-retired` model input. Its consumption at revision3 records `NotAdmitted`, preserving the interpreter snapshot as history and the final Cancelled reason. It creates no synthetic native result. Start acknowledgment follows this exact positive retirement receipt; cancel acknowledgment follows start acknowledgment. A third standard host retains the same model, receipts, pins and source acknowledgments. No native worker, ETL run or HTTP effect appears.

The [manifest](manifest.json) binds **22 raw assets**, including nine complete coupled stores across the initial, pending-cancel and settled cuts. Checked first-party provider reads expose the exact model/material/source/input/queue identities. Independent physical checks require zero native carriers/work and zero ETL runs, unchanged native/model material, actual pending source responsibility and exact physical receipts/acknowledgment order. All three owned Workbench processes and the HTTP fixture are stopped. Private paths/ports are historical observations, not reproduction bindings.

The implementation uses the existing irreversible terminal model decision and qualified atomic model-to-native admission condition. An admission that committed first has its actual header; after cancellation, the same provider cannot accept an initial native admission against the old Running model snapshot. Header absence alone does not acknowledge anything. Missing cancellation proof, a previously acknowledged start without its exact retirement receipt, or queued native work leaves responsibility owed.

A separate saved-state test opens this actual pending capture and sends a late model-owned start through the production dispatcher. The native start/checkpoint retire under the ordinary mailbox fence without native state or root work; replay converges on the same marker. Removing the physical cancellation source refuses retirement and retains the queued work. This is a provider/dispatcher test, not a full process-loss witness for an already-queued native checkpoint. That process cut and simultaneous admission/cancellation race remain open.

## Reproduction and checks

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel-before-admission \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>

python3 e2e-tests/etl/verify_native_start_retirement.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/native-start-retirement/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

Use a clean committed source and the normal build-slot wrapper. The actual journey passes; all nine stores pass integrity. The portable suite passes33/33 with zero skips, including six new rehashed controls for graceful-exit substitution, missing physical retirement proof, early source acknowledgment, acceptance mislabeled as settlement, an invented admitted-child result and a physical carrier hidden by an empty public list. All104 original baseline assets and the authoring fingerprint remain unchanged. [Qualification](qualification.json) retains checks and scope.

This proves cancellation of the original undelivered start across real process loss on the selected EF SQLite/built-in actor profile. The real worker and pipeline are published/pinned, but are deliberately never admitted by this cut. Already-queued start process recovery, simultaneous admission/cancellation, pending native result/cancel cuts, RunBpmn service embedding, held retry, provider/rollback and final affected-source upgrade/Studio acceptance remain required. T035/T039/T040, #2898/#2907 and the program remain open/partial. Draft PR3410 and full-PR review rounds0 of6 remain unchanged.
