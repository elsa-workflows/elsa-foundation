# Actual legacy partial-fork capture

Captured 28 October 2026 from executing production source `438001406d980051584234e5d656eb8e6a67f47f`, using capture tooling at `96eb35af4` (full identity and runner hash in [manifest.json](manifest.json)). The current main descendant `d3ebb980e` changed documentation only; this capture is labeled with the actual executing build, not relabeled as that descendant.

Eight actual normally submitted/published/executed workflows retain a completed branch A and suspended branch B: legacy BpmnProcess root plus Sequence, Flowchart and CompensationScope embedding, each in Balanced and Durable mode. The API reports the actual mode, IDs, immutable pin and held activities in [cases.json](cases.json). The existing runtime remains the legacy activity adapter; these are not direct-model executions.

The explicitly owned host/controller/start/executable/content-root identity was verified before SIGSTOP. Whole Runtime/Design SQLite backups were taken while that same host was stopped, then it was resumed and all eight held observations compared again. No workflow-state SQL was used to create the fixtures. The user's backend/Studio and the owned Studio process remained running. Raw store backups and the actual product/library version/hash report are relative assets in the manifest; verification needs none of the old machine paths.

The closed Design snapshot passes the actual frozen-authoring comparison (`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`), with both expected published descriptors. Both databases pass SQLite integrity verification. Nine executing host/writer assemblies share the recorded source suffix and match their inspected binary hashes; the three released BPMN library products are recorded separately in [product-versions.json](product-versions.json).

## Verified commands

The [capture runner](../../../../../tools/bpmn/qualify-hosting.py) was run against the explicit owned-host configuration with a new output directory, then this portable bundle was copied into the repository and independently verified:

```sh
python3 tools/bpmn/qualify-hosting.py verify-capture \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/partial-forks/manifest.json

ELSA_BPMN_CAPTURE_MANIFEST="$PWD/specs/1017-direct-bpmn-hosting/evidence/baseline/partial-forks/manifest.json" \
  python3 -m unittest discover -s e2e-tests/bpmn -p test_qualify_hosting.py -v
```

Results: five declared asset hashes and eight actual held cases verify; twelve mechanical/capture-control tests pass. Four controls mutate copies of this actual bundle: corrupt database bytes, omit its hashed database declaration, substitute a case execution despite rehashing its API receipt, and bind the frozen receipt to another snapshot despite rehashing it. All are refused; the original bundle still verifies. These test capture integrity, not execution semantics or upgraded-host recovery.

The metadata reader builds successfully with zero errors; its first attempt found a tooling-local variable collision, fixed before the successful build. The first live ownership preflight refused macOS path aliases before any host stop or fixture creation; canonical path comparison and its regression test fixed that, followed by the successful actual capture.

## Limits and next gate

No upgraded-host recovery, new direct hosting or full writer-capture completion is claimed. T001 is complete for this capture family; T002 remains open for the affected callback, first-recipient/cohort, called-control/MI/compensation and Studio cuts. No production writer/schema has changed. These original held stores and IDs must remain available to the later normal-command upgrade/finish proof; do not synthesize replacement positive states.
