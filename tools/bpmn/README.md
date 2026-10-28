# BPMN host qualification tools

`qualify-hosting.py` captures actual legacy-writer inputs for [spec1017](../../specs/1017-direct-bpmn-hosting/quickstart.md). It does not prove direct-model hosting, upgraded-host recovery or the complete writer-capture matrix.

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

python3 -m unittest discover -s e2e-tests/bpmn -p test_qualify_hosting.py -v
```

Output must be a new directory. A failed preparation retains its case journal/failure receipt and is not positive evidence. Successful bundles use relative paths and matched hashes; verification refuses missing assets, path escapes, changed snapshots, mismatched execution/pins/modes or absent frozen-contract proof. A bundle is a source-bound capture, not an independent signature or an upgraded-host acceptance result.

Pending callback/cohort/control/MI/compensation/Studio cuts still need their own actual captures. `allWriterCaptureFamiliesComplete` remains false. Later implementation tasks add actual upgrade/complete-host qualification phases; unsupported phases currently fail argument parsing rather than return a successful placeholder.
