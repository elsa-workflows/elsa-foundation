# Actual model-owned signal recovery

Executing source: `8a1aa37942393923b4f55330d6da75a1d5eb5b12`. The [manifest](manifest.json) retains 79 qualification assets, including 30 complete Runtime/Design stores, for five actual journeys: direct, native Root, Sequence, Flowchart and CompensationScope. Fifteen owned production Workbench processes were stopped with SIGKILL; each case has three processes and two Event workers. No persisted execution state or clock was edited.

Each published model contains two real `intermediateCatchEvent` signal catches behind a parallel fork/join. Definition format 5 pins the complete published native Event worker for each binding. Those workers wait with `CanStartWorkflow=false`; the model owns BPMN progression and has no fabricated native execution header. Human-work inspection excludes both waits. After process loss, an authenticated public API starts the real published PublishEvent sender. Its original durable first-delivery cohort contains exactly the two captured native Event registrations. Both workers return typed EventResult values through their exact original model origins. The model consumes both results and completes. Each native parent receives typed Done, settles before result-source acknowledgment, and continues its successor exactly once where present. A further cold host preserves the final state and receipts.

The new model definition reuses opaque work frames 2/5, the existing native admission/result bridge, and the existing durable Event cohort. This evidence does **not** yet qualify delivery-time inclusion of a later listener, rearm protection during source retry, pre-ACK process loss or an installed empty cohort. T021 remains unchecked. There is no new model-recipient ledger or general event bus. Message catches, throws, filters and correlation are refused by this initial format. Final affected-source legacy upgrade and Studio qualification remain separate gates.

Whole BPMN unit tests pass 456/456; actual signal hosting passes 5/5, zero skips. Removing the CanStartWorkflow guard made its behavioral refusal controls fail; the guard is restored. The portable verifier independently checks captured model/native pins, registrations, typed results, consumption-before-ACK, retained material bytes and whole stores. Fourteen portable checks include thirteen rehashed corrupt-copy controls covering missing consumption, changed worker/model/source cohorts, premature ACK, product/process identities and incomplete stores. All 104 frozen baseline assets and the original baseline manifest remain unchanged.

Diagnostics are retained separately and never counted as qualification. At the earlier requested source `4a25c30ba6d50c662742c5a44adb52791eda46a0`, publication refused the actual stable `Elsa.Event` key and unauthored absent correlation literal; these assumptions were corrected before the successful journeys. An interrupted initial run supplied an incorrect source label and left four partial failure captures. Its actual binary source was not recorded in those receipts; it is explicitly excluded. The diagnostic bundle contains 27 assets, including 18 failed-store captures.

Verification:

```sh
python3 e2e-tests/bpmn/verify_signal_recovery.py specs/1017-direct-bpmn-hosting/evidence/events/signal-recovery/manifest.json
python3 -m unittest discover -s e2e-tests/bpmn -p test_verify_signal_recovery.py
```

The producing host test is `SignalWorkbenchTests` in `Elsa.Activities.Bpmn.Hosting.Tests`. Fresh captures require a committed source, source-matched builds, a new `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY`, and the actual full commit in `ELSA_BPMN_HOST_SOURCE_REVISION`. Local temporary paths are not continuation inputs.
