# Actual queued native start/checkpoint recovery

Executing source **fc03c3b96b99ae309a3763ecbae07562a237ade5** qualifies both queued boundaries: the real native Start item and its generated WorkflowStarted checkpoint. Ordinary authenticated Workbench APIs publish the actual ETL pipeline, its closed RunEtl worker and a direct BPMN service model. The first host's normal recovery sweep is300 seconds, retaining the original native-start source. An owned [SDK client](../../../../../tools/bpmn/NativeWorkDispatch/README.md) then calls the production start dispatcher, using the existing drain-policy seam to enqueue only or drain exactly one work item. It retains the original worker/model pins, authority, invocation/attempt and source address. No workflow/activity/bookmark or ETL state is seeded or edited; no alternate semantic driver or clock is used.

Each before snapshot has model revision1, the undelivered original start source and exactly one protected-family3 native queue item. For the checkpoint case, that actual envelope still owns its pending root ScheduleActivity intent. The public model cancel API records revision2/TerminalPendingSettlement while that same native envelope remains queued. The owned Workbench is killed with SIGKILL (exit -9) at this cut.

A fresh standard Workbench opens the same unedited stores with a normal0.2-second recovery sweep. It retires the queued native start/checkpoint through the existing mailbox and checkpoint funnel. The original native retirement commit ID is retained, its pending root-work list is empty, and the actual native queue is consumed. Only then does the model consume its exact native-start-retired receipt at revision3/TerminalSettled, retain NotAdmitted and acknowledge start then cancel. A third standard host preserves the same model, pins, interpreter snapshot, receipts and acknowledgments. No native header/activity/bookmark, invented native result, ETL run or HTTP effect appears.

The first real queued captures exposed a missing flush boundary: coalescing replaced the retirement's logical marker with an anonymous quiescence-flush marker. Those captures are rejected by the verifier. The fix gives this concrete runtime boundary the existing catalog's `NativeModelWorkStartRetired` name and adds it to the immediate-flush set. It adds no execution authority or checkpoint participant and changes no existing checkpoint's flush behavior. The tracked Core constant builds with zero errors. The corrected actual captures retain the exact original commit identity, and a fully rehashed corrupt-copy control also rejects anonymous-flush substitution.

The [matrix manifest](manifest.json) binds both cases and **46 raw assets**, including eighteen complete coupled stores across the before, pending-cancel and settled cuts. Each case uses three Workbench processes, one HTTP fixture and one owned SDK process. All ten qualified journey processes are stopped. Private paths/ports are observations; reproduction creates fresh owned bindings. [Qualification](qualification.json) retains checks and scope.

## Reproduction and checks

Use a clean committed source and the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel-before-admission --queued-native-stage checkpoint \
  --dispatcher-assembly <built-NativeWorkDispatch-assembly> \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Repeat with `--queued-native-stage start` and another fresh output directory. Verify both committed cases with:

```sh
python3 e2e-tests/etl/verify_native_start_retirement.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/queued-native-retirement/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

Both actual journeys pass, with eighteen stores passing integrity. BPMN384/384, focused runtime coalescing policy23/23 and portable40/40 pass, zero skips. Six additional rehashed controls reject an anonymous retirement marker, a missing physical marker, a queue item absent from the crash cut, a redirected attempt, premature SDK admission and a different SDK source. Both real pre-fix captures are rejected. Maps and test tiers pass. All104 original baseline asset hashes and the frozen authoring fingerprint remain unchanged.

## Remaining acceptance

This proves actual queued start/checkpoint cancellation recovery on the selected EF SQLite/built-in actor profile, with an SDK enqueue client and standard Workbench recovery. The already-admitted-worker/ETL and undelivered-original-start receipts retain their own sources. Simultaneous native admission/cancellation, pending native result/cancel cuts, RunBpmn service embedding, held retry, provider/rollback and final affected-source upgrade/Studio acceptance remain required. T035/T039/T040, #2898/#2907 and the program stay open/partial. Draft PR3410 and full-PR review rounds0 of6 remain unchanged.
