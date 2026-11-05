# Actual native ETL recovery reference

Three real production Workbench journeys at **9a3b397382e0a7dde27f830a9b236851d41c28fe** establish the native ETL behavior that the BPMN binding must preserve. This is a reference before the model/native bridge; it does **not** prove direct/RunBpmn ETL integration or model-held fault retry.

| Actual published workflow | ETL result after process loss | Native result | Physical output |
|---|---|---|---|
| Sequence: RunEtl → WriteLine | Succeeded, 4 accepted | Original RunEtl and successor complete once | 4 mapped HTTP records |
| Flowchart: RunEtl → WriteLine | Succeeded, 4 accepted | Original RunEtl and successor complete once | 4 mapped HTTP records |
| RunEtl as the real worker root | BusinessError, 3 accepted / 1 rejected | Workflow Completed; root outcome **BusinessError** | 3 mapped HTTP records and 1 safe JSONL error |

Each journey publishes an actual v2 pipeline through `/etl/v2`, publishes its native workflow through the ordinary workflow APIs and executes the exact publication. The delivered loopback HTTP fixture returns a real `429 Retry-After: 20` for shard B. Shard A finishes while B has one durable waiting attempt. The controller briefly stops only its own Workbench to take consistent read-only provider backups, then terminates that process with SIGKILL. A fresh standard Workbench opens the original stores, resumes the same invocation and completes it. A third Workbench preserves the exact terminal API views and receiver observations.

The complete native executable pin, immutable ETL artifact/invocation and real bookmark/result address survive. Retry does not occur before the original 20-second wire interval; it resumes the original two-page shard. Both partitions exhaust and settle with no outstanding records/bytes. The native bookmark is consumed, and the ETL terminal outbox is durably acknowledged. No execution rows or clocks are edited. External effects retain their at-least-once boundary; observed output counts are reported, not generalized to exactly-once I/O.

The HTTP request retry and a model/native worker attempt are separate. This recovery retains one native invocation; it does not create or prove a model retry attempt after terminal failure.

The worker-root case is decisive for the next binding: **native Completed does not imply ETL Succeeded**. Its ETL terminal is `businessError` with code `etl.rows-rejected`; its native root reports `BusinessError`. The safe error is `etl.invalid-value` for field `id`, without the original invalid value. The model binding must preserve this family rather than collapse workflow status into success.

The [manifest](manifest.json) binds 45 raw assets: eighteen whole-store snapshots, normal API observations, actual receiver bodies, safe-error output and three process/binary/source receipts. The six Workbench/ETL product identities share the exact source above. The observer uses the actual .NET SQLite provider in read-only mode and verifies unchanged source main bytes; Python reads only the resulting closed, hash-verified snapshots.

## Reproduction

Build `Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj` in Release with `-p:ElsaBpmnRecoveryProbe=false`; this also builds the standard Workbench and checked reader. Use one clean committed source for the controller and binaries. Node must be available for the existing loopback fixture. No PowerShell installation is required.

```sh
python3 e2e-tests/etl/qualify_http_recovery.py \
  --parent RunEtl --business-error \
  --workbench-assembly <built-Workbench-assembly> \
  --reader-assembly <built-DeliveryRecoveryRead-assembly> \
  --output <fresh-output-directory>
```

Use `--parent Sequence` and `--parent Flowchart` without `--business-error` for the two success cases. The copied host configuration enables the delivered ETL features and private SQLite stores/bindings; all endpoints are host-allowlisted loopback paths. The fixture's existing default remains `Retry-After: 3`; the longer response is an explicit recovery-test option, not a runtime retry-policy change. The runner stops only its own three Workbench children and its Node child, retaining private diagnostics on failure. User hosts are preserved.

Reproduction creates fresh owned bindings. The retained snapshots are portable for offline comparison; recorded temporary paths and listener ports are not reused as live resources.

```sh
python3 e2e-tests/etl/verify_http_recovery.py \
  --manifest specs/1017-direct-bpmn-hosting/evidence/pipeline/native-http-reference/manifest.json
python3 -m unittest discover -s e2e-tests/etl -p test_verify_http_recovery.py -v
```

The portable comparison checks source/process identity, SIGKILL, original pins and invocation, physical native/ETL state, bookmark consumption, immutable typed result address, partition accounting, outbox acknowledgment, actual HTTP timing/bodies and safe-error output. Rehashed corrupt copies reject a hidden business error, early retry, graceful shutdown mislabeled as process loss and a result address borrowed from a different native invocation.

[Qualification](qualification.json): all three live journeys and the five verifier/control tests pass; focused RunEtl activity/result-delivery tests pass28/28, with zero skips. Original104 baseline assets remain unchanged. Maps and test-tier guards pass.

T034–T040 remain open: actual RunBpmn/model-to-native admission, delivery/cancellation settlement, direct/embedded typed outcomes and bounded model incident retry are not implemented by this reference. In particular, this is recovery of a transient HTTP retry, not revival of a terminal ETL failure. The next implementation uses the demonstrated real worker boundary and its typed outcome. T041 still requires final affected-source upgrade/Studio qualification after bridge changes.
