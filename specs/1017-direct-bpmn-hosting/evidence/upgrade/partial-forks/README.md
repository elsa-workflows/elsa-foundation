# Actual legacy partial-fork upgrade

The changed production Workbench recovers the original [pre-writer capture](../../baseline/partial-forks/manifest.json) from source `438001406d980051584234e5d656eb8e6a67f47f`. Executing qualification source: `f3b56e516d4e6bd05bfa82995f512f27f4520fe8`. No old-looking state is generated or patched: the test copies the actual whole Runtime and Design stores into its new owned content root, then uses authenticated product commands.

All eight executions preserve their complete native artifact pin, physical wait identities, prior completed branch and persistence mode. The normal remaining Event stimulus resumes each original wait, joins and completes exactly once. Legacy BPMN roots and Sequence, Flowchart and CompensationScope each pass in Balanced and Durable mode. The changed host adds the two model-hosting migrations while retaining the original applied migration and all 59 original executable payloads/incarnations. Original capture files remain byte-identical and have no WAL/SHM/journal sidecars.

[Process observations](legacy-partial-forks/process.json) include exact binary versions/hashes, both migration histories, original artifact hashes, recovered/completed API views and normal delivery receipts. The stopped upgraded Runtime and Design backups are bound by [the manifest](manifest.json); both pass integrity checks, and final physical workflow rows agree with the completed observations. The [frozen-authoring receipt](frozen-contract.json) verifies both actual recommended published BPMN descriptors in the upgraded Design store against the unchanged authoring fingerprint. Identity and configuration are excluded.

[Qualification](qualification.json): the exact-source whole hosting suite passes 4/4, including the existing native/direct/runtime-only witnesses and this upgrade. Seventeen capture/upgrade verifier tests pass with no skips when the actual baseline is supplied. Five of these mutate copies of the real upgraded evidence: native terminal state, original executable bytes, wait identity, baseline binding and frozen-store binding must still be refused after rehashing. A separate pre-commit production mutation misclassified existing native artifacts as model material; the real normal-resume test failed. The migration is restored and the exact-source positive suite passes.

Reproduce from committed source using a new owned output directory:

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/partial-forks/manifest.json \
  --output /tmp/elsa-bpmn-legacy-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest /tmp/elsa-bpmn-legacy-upgrade/manifest.json
```

Verify this retained bundle without starting a host:

```sh
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/partial-forks/manifest.json
```

This advances T030 for one actual capture family. Pending callbacks/native-parent delivery, broadcast cohorts, multi-instance/compensation/called-control captures, actual Studio behavior and other-provider upgrade need their own changed-host verification. Real native/ETL service bindings, operational races/settlement/admission and clustered placement remain open. T030, #2898, #2907 and the full program are not complete; full-PR review rounds remain zero.
