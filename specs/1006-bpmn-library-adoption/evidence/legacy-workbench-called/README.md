# Coherent actual old Workbench called-workflow fixture

The runtime and design/publication captures are a **pair**. They were written by the original source-built Workbench through public APIs and frozen after its owned writer exited. Identity/token storage was separate at capture time and is not included. Complete databases retain original absolute CLR catalog source-directory identifiers as historical provenance. Recovery does not require recreating them: an additional cold current-host control passed both continuations while the original old build directory and its private-tmp alias were physically unavailable, then restored its bytes. Do not rewrite that captured provenance. Never replace one member with a fresh catalog. The [qualification receipt](../legacy-called-workbench-recovery.md) records source identity, ordinary result policy, recovery outcomes and limits.

| File | Meaning |
|---|---|
| `runtime.sqlite.gz`, `design.sqlite.gz` | Complete original module databases; both callers and dispatched BPMN children are at real waits. |
| `old-resumed-runtime.sqlite.gz` | Complete result of the old-host control, using writable copies of the original pair. |
| `upgraded-resumed-runtime.sqlite.gz` | Complete result of adoption-host recovery, using separate byte-identical copies of the original pair. |
| `portability-resumed-runtime.sqlite.gz` | Additional cold current-host result while the original legacy build directory was unavailable. |
| `capture-api.json` | Actual public authoring/publication/start observations and exact original root/call/wait identities. |
| `old-api.json`, `upgraded-api.json`, `portability-api.json` | Actual normal Event continuation and original-invocation/pin/fault checks. |
| `capture-checked.json`, `old-checked.json`, `upgraded-checked.json`, `portability-checked.json` | Read-only checked EF observations, including physical waits, capability absence and real typed fault truth. |

`portability-proof.json` records old-directory absence before cold start and through actual continuation, writer exit and unchanged output restoration. JSON observations/manifests contain no machine paths; whole databases preserve their original attribution.

Each compressed database has an adjacent JSON receipt with raw and compressed SHA256. Decompress into new private files; keep originals unchanged. Use the existing `e2e-tests/_ServerLifecycle.ps1` ownership helpers with separate fresh content roots and explicit old/current built assembly paths. Build the old host from d81a4c88a2924708ea8afcc2db20b8a77b84f703; when using an archive beneath another checkout, pass `-p:SourceRevisionId=d81a4c88a2924708ea8afcc2db20b8a77b84f703`. Preserve its source/pin manifests rather than copying a current binary into an old source tree.

In **each** host's copied `shells.json`, configure:

```json
{
  "WorkflowsRuntimeEntityFrameworkCore": { "ConnectionString": "Data Source=runtime.db" },
  "ActivitiesDesignEntityFrameworkCore": { "ConnectionString": "Data Source=design.db" },
  "WorkflowsDesignEntityFrameworkCore": { "ConnectionString": "Data Source=design.db" },
  "WorkflowsPublishingEntityFrameworkCore": { "ConnectionString": "Data Source=design.db" },
  "WorkflowsRuntimeCheckpointPersistence": { "Mode": "Balanced", "MaxActivitiesPerCommit": 50 },
  "WorkflowsRuntimeResumption": { "SweepIntervalSeconds": 1 },
  "GitHubCopilotAgent": { "Enabled": false }
}
```

Apply these settings to the existing feature objects under `CShells.Shells.default.Features`; retain required signing-key and other committed settings. Copy the decompressed pair to that root as `runtime.db` and `design.db`. Let the other modules initialize in their own fresh default private database. Wait for the owned process's `/health/ready` and verify its listener identity. Continue through the public API, supplying that root's explicitly owned URL and a new evidence path:

```sh
pwsh -NoProfile -File e2e-tests/bpmn/Test-BpmnLegacyCalledUpgrade.ps1 \
  -Phase Resume -BaseUrl http://127.0.0.1:YOUR_OWNED_PORT \
  -CapturePath specs/1006-bpmn-library-adoption/evidence/legacy-workbench-called/capture-api.json \
  -EvidencePath YOUR_NEW_PRIVATE_RESULT.json -SourceRevision YOUR_VERIFIED_HOST_SOURCE
```

Stop only the owned host and freeze its resulting runtime database through SQLite backup after verifying writer exit. Build `tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` with locked restore and inspect the new closed snapshot using its actual complete hash:

```sh
dotnet tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll \
  --inspect-called YOUR_CLOSED_RUNTIME.sqlite YOUR_RAW_SHA256 default \
  specs/1006-bpmn-library-adoption/evidence/legacy-workbench-called/capture-api.json resumed
```

Use `captured` for the original runtime. The reader refuses live sidecars, wrong hashes, missing/changed pins, replaced invocations, unexpected capability/control, untruthful fault results and orphan physical sources. Its actual-fixture controls accept raw or compressed original captures:

```sh
python3 e2e-tests/bpmn/test_called_snapshot_reader.py \
  --reader-assembly tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll \
  --database specs/1006-bpmn-library-adoption/evidence/legacy-workbench-called/runtime.sqlite.gz \
  --sha256 d0fcbe5132a0ae93f50e67c3186fa95ea7994caf892feb98e038bb5e2bff43b4 \
  --capture specs/1006-bpmn-library-adoption/evidence/legacy-workbench-called/capture-api.json \
  --output YOUR_NEW_PRIVATE_CONTROLS.json
```
