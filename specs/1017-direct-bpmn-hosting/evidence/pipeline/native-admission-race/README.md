# Actual native admission versus BPMN cancellation

Executing source **54d8532b0a00c39626185a7d617790004b9f9448** qualifies both commit orders against the real EF SQLite writer. Each run publishes a real ETL pipeline, a closed native RunEtl worker and its direct BPMN service model through authenticated Workbench APIs. An owned SDK queues the genuine WorkflowStarted checkpoint while the model remains Running at revision 1. No execution state, source receipt, clock or worker result is seeded.

The owned SDK clients use the production BPMN feature, protocol registry, native drain orchestrator and EF persistence. A pass-through checkpoint validation hook records the first writer's actual proposal and observed model, pauses before its provider transaction, then releases that same fingerprint after the second SDK has committed. This deliberately controls the interleaving; it does not replace either writer. The first host's normal sweep interval is 300 seconds. All later recovery uses standard Workbench hosts and their normal 0.2-second sweep.

| Actual order | State before process loss | Fresh-host recovery |
|---|---|---|
| Cancellation first | Native proposal retains the complete revision-1 model admission guard. Cancellation commits revision 2. Releasing the unchanged native proposal causes the actual provider to defer it with “authorizing execution changed”; the original checkpoint remains queued and no native header exists. | The original rootless retirement marker is retained. Exact native-start-retired consumption at model revision 3 precedes start/cancel acknowledgments. NotAdmitted is true; no native result is invented. |
| Native admission first | Cancellation's revision-2 proposal still expects revision 1. The competing native SDK commits the header and root scheduling while the stored model remains revision 1. The unchanged cancellation proposal then commits revision 2. | The actual native worker becomes Cancelled. Its retained native-result source and exact model input receipt settle revision 3. NotAdmitted remains absent; the real native result is preserved. |

The native SDK uses the existing two-cycle drain budget to stop before root invocation. The admitted capture retains a scheduled root and its real StartActivity queue item; it has no incident or ETL run. A scheduler's one-item per-pass budget alone follows delivered continuations, so an earlier client attempt reached a missing invocation handler. That attempt was rejected and its stopped stores retained privately. The accepted captures use the corrected client and report reaching the explicit cycle budget. No production architecture or semantic changes were needed for these race cases.

Both first hosts lose their actual processes through SIGKILL (exit -9). A fresh host settles the same unedited stores, and a third host retains identical state, pins, interpreter history, receipts, acknowledgments and HTTP observations. Both runs have zero ETL runs and zero HTTP requests/effects. The admitted case therefore proves truthful cancellation before ETL invocation. The earlier [admitted-worker/ETL cancellation journey](../model-cancellation/README.md) retains its separate executing source and downstream delivery evidence.

The [matrix](manifest.json) binds **56 raw assets and 18 complete coupled stores**. Its two cases use six Workbench processes, six SDK processes and two HTTP fixtures. All fourteen qualified children, and every rejected/superseded attempt's owned children, have stopped. [Qualification](qualification.json) records the source and bounded checks. Temporary paths, ports and process IDs are observations; reproduction creates fresh owned bindings.

## Reproduction

Build a clean committed source through the normal build-slot wrapper:

```sh
dotnet build Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
  -c Release -p:ElsaBpmnRecoveryProbe=false
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent BpmnModel --model-cancel-before-admission --queued-native-stage checkpoint \
  --admission-race-order cancel-first \
  --dispatcher-assembly <built-NativeWorkDispatch-assembly> \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Repeat with `--admission-race-order admit-first` and another fresh directory. Portable verification:

```sh
python3 e2e-tests/etl/verify_native_admission_race.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/native-admission-race/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p 'test_verify*.py' -v
```

The verifier checks actual writer ordering, unchanged proposal fingerprints, revision-1 guard, retained native truth, physical queues/headers/checkpoint markers, exact consumption receipts before acknowledgment, material retention, whole-store integrity and third-host stability. Its ten rehashed negative controls reject removed guards, false ordering, rewritten proposals, incorrect worker truth, missing physical markers/headers, unbounded SDK execution, early cancellation acknowledgment and redirected queued attempts. All 51 portable tests pass. BPMN 384/384, focused EF model checkpoint tests 43/43, maps and test tiers 123 pass, zero skips. All 104 frozen baseline assets and the authoring fingerprint remain unchanged.

## Remaining acceptance

This closes both native-admission/cancellation interleavings on the selected EF SQLite/built-in actor profile. Pending native-result/cancel process cuts, RunBpmn service embedding, held retry, other providers/rollback and final affected-source upgrade/Studio qualification remain open. T034/T035/T038–T041, #2898, #2907 and program #2892 remain incomplete. PR #3410 stays draft; full-PR author-review rounds remain 0 of 6. No prior-source Studio or legacy-upgrade receipt is relabelled as final-source acceptance.
