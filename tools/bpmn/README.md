# BPMN host qualification tools

`qualify-hosting.py` captures and verifies actual legacy-writer inputs and resumes six legacy upgrade families for [spec1017](../../specs/1017-direct-bpmn-hosting/quickstart.md). Individual commands prove only their declared family. Direct-model/native/ETL journeys use the separate hosting tests and `e2e-tests/bpmn/qualify_*.py` controllers indexed by their receipts.

The first capture family parks eight normally published/executed partial parallel forks: legacy BpmnProcess root and Sequence/Flowchart/CompensationScope parents, each in Balanced and Durable mode. It completes branch A through the ordinary Event stimulus and retains branch B. It then verifies the explicitly owned host/controller/PID/start/executable/root identity, stops only that host for consistent whole Runtime/Design backups, resumes it even when backup fails, and compares the held API observations again. The frozen authoring verifier reads the closed Design snapshot.

The supplied controller must be `e2e-tests/bpmn/run_owned_broadcast_probe.py`. Capture is currently qualified only for this POSIX/SQLite cut. Credentials come from `ELSA_CAPTURE_USERNAME`/`ELSA_CAPTURE_PASSWORD`, defaulting to the existing isolated-test account; they are not written to evidence. Use an isolated owned content root, never user stores. The runner must be committed without local edits before capture, so its source and content hash can be retained.

Build `DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` through the normal build-slot wrapper. Its metadata-only `--inspect-host-products <actual-workbench-dll>` mode reads the actual nine host/writer product versions and hashes plus three released BPMN library binaries. It executes no target assembly. Supply that JSON to `--product-versions`; the runner verifies every listed binary against the actual host directory.

```sh
python3 tools/bpmn/qualify-hosting.py capture-baseline \
  --assembly /absolute/owned/build/Elsa.Workbench.dll \
  --content-root /absolute/owned/content \
  --owner-manifest /absolute/owned/controller-manifest.json \
  --base-url http://127.0.0.1:53186 \
  --product-versions /absolute/actual-product-versions.json \
  --output /absolute/new-evidence-directory

python3 tools/bpmn/qualify-hosting.py verify-capture \
  --manifest /absolute/evidence-directory/manifest.json

ELSA_BPMN_CAPTURE_MANIFEST="$PWD/specs/1017-direct-bpmn-hosting/evidence/baseline/partial-forks/manifest.json" \
  python3 -m unittest discover -s e2e-tests/bpmn -p test_qualify_hosting.py -v
```

Output must be a new directory. A failed preparation retains its case journal/failure receipt and is not positive evidence. Successful bundles use relative paths and matched hashes; verification refuses missing assets, path escapes, changed snapshots, mismatched execution/pins/modes or absent frozen-contract proof. A bundle is a source-bound capture, not an independent signature or an upgraded-host acceptance result.

The partial-fork capture's `allWriterCaptureFamiliesComplete=false` is deliberately local to that producer. The frozen [complete baseline manifest](../../specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json) accounts for all original captures. `resume-upgrade --family` selects `partial-forks`, `native-parents`, `called-ack`, `broadcast`, `controls` or `multi-instance`; `verify-upgrade --manifest` checks the matching retained receipt without starting a host. The [final affected-source qualification](../../specs/1017-direct-bpmn-hosting/evidence/final-compatibility/README.md) retains all six families and the separate actual Studio repeat. There is no generic `qualify` phase.

For Studio, build the browser workspace before Studio.Web and use `run_owned_studio_upgrade.py` over the `original` or `layout` captured pair. The browser performs actual export/edit/save/publish/run/inspect steps; controller `restart`/`finish` commands retain coupled provider-backed snapshots and stop only its own children. `verify_studio_upgrade.py` checks the source-bound receipt. Existing embedded Studio compatibility does not establish first-class direct-model Studio support.
# Predecessor queue-reader witness

`DeliveryRecoveryRead --check-predecessor-queue-reader <built-old-Workbench.dll> <new-fixture.sqlite>` writes the
current EF queue and loads the supplied predecessor's checked reader in an isolated assembly context. It verifies
that an unchanged native v1 control reads and the actual current model v2 row is refused with schema skew.
The fixture path must be new. Output includes the predecessor provider/Core product commits and hashes.
The native control's v1 stamp is deliberately constructed; this witness proves predecessor reader refusal, with
separate tests for the actual held-v1 write gate. It does not prove host admission, worker placement or recovery.

## Native RunBpmn recovery

`verify_runbpmn_recovery.py` checks the [actual human-work embedding captures](../../specs/1017-direct-bpmn-hosting/evidence/embedding/human-native-recovery/README.md) without starting hosts or changing stores. Eight real native/model journeys cover root, Sequence, Flowchart and CompensationScope completion/whole-parent cancellation after a real process loss. Four same-process direct counterparts compare released-interpreter states. The verifier checks all32 closed stores and exact native/model settlement receipts; `test_verify_runbpmn_recovery.py` supplies seven rehashed corrupt-copy controls. The receipt explicitly leaves ETL embedding, additional crash/race cuts and final upgrade/Studio acceptance open.

`verify_runbpmn_admission_cancel.py` checks the separate [actual root cancellation before first model admission](../../specs/1017-direct-bpmn-hosting/evidence/embedding/cancel-before-admission/README.md). The normal alteration pump cancels the real parent while start/cancel sources remain owed; process-loss recovery records cancellation without starting semantics and settles its original delivery responsibility. Four closed stores and five corrupt-copy controls preserve this narrower claim. Run both control suites with `python3 -m unittest discover -s tools/bpmn -p 'test_verify_runbpmn*.py' -v`.
