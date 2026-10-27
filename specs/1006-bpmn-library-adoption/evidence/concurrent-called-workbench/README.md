# Actual concurrent called-control Workbench fixtures

See [the receipt](../concurrent-called-workbench-recovery.md) and its [source/artifact manifest](../concurrent-called-workbench-recovery.json). Four complete Runtime snapshots follow two real public-host cases, each with two simultaneous tokens at one call element, one bound DispatchWorkflow node, null iterations and distinct child executions. The fifth database is their complete coherent Design/Publishing store. Identity/token storage was separate and is excluded. Original catalog directory identifiers remain historical provenance; do not recreate paths or rewrite database bytes.

The standard host uses production source `f0411e2fedb80eba38f3bee9451e16ef7d6992c0`, including the [finite-timer correction](../finite-timer-recovery.md). The build explicitly set `SourceRevisionId=f0411e2fe`, an unambiguous prefix of that commit. Its actual Bpmn.Model/Semantics/Interchange DLLs match the released 0.5.0 package bytes; its binary metadata contains no recovery probe. Runtime mode is Balanced. Use the isolated module-store configuration and ownership precautions in the [legacy host guide](../legacy-workbench-called/README.md), a private identity database, normal Tasks/resumption, and an explicitly owned free port.

The runner performs only public authoring/publishing/execution, observation and correlated ResumeOnly Event APIs:

```powershell
./e2e-tests/bpmn/Test-BpmnConcurrentCalledHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostSource -Phase Prepare -EvidencePath $preparedApi
# Freeze a closed coherent backup. The checked reader below produces $selection from its actual second semantic token.
./e2e-tests/bpmn/Test-BpmnConcurrentCalledHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostSource -Phase HandleSelected -PreparedEvidencePath $preparedApi -SelectionEvidencePath $selection -EvidencePath $selectedApi
# Freeze another closed backup. Stop/await this controller's exact owned child only, then start a new standard process over the same original stores.
./e2e-tests/bpmn/Test-BpmnConcurrentCalledHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostSource -Phase ObserveSelected -PreparedEvidencePath $preparedApi -SelectionEvidencePath $selection -EvidencePath $recoveredApi
# Freeze the recovered backup before continuing.
./e2e-tests/bpmn/Test-BpmnConcurrentCalledHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostSource -Phase HandleOther -PreparedEvidencePath $preparedApi -SelectionEvidencePath $selection -EvidencePath $finishedApi
```

`Prepare` creates both boundary policies. Each fork has two flows into the same single call element/binding, rather than two elements sharing one binding. Both child executions initially wait at an emit Event. The immutable checked prepared snapshot selects `activeWork[1]`, resolves its exact call through provenance `ExecutionPathId`, then resolves the authoritative child dispatch. `HandleSelected` emits only for that child, completes its warning Event, and completes its finish Event if noninterrupting. `HandleOther` later repeats for the other child. Each Event uses `CanStartWorkflow=false` and the canonical pure `WorkflowInstance.instanceId` correlation; each public stimulus must resume exactly its intended workflow and start none.

Build `tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` with locked restore, decompress a gzip database to a new private closed file without sidecars, and run:

```text
dotnet <reader.dll> --inspect-opted-called <runtime.sqlite> <uncompressed-sha256> default <api.json> <phase>
```

Matching triples: `prepared-runtime`/`prepare-api.json`/`concurrent-prepared`; `selected-runtime`/`selected-api.json`/`concurrent-selected`; `recovered-runtime`/`recovered-api.json`/`concurrent-recovered`; `finished-runtime`/`finished-api.json`/`concurrent-finished`. Adjacent database JSON files contain hashes. The full prepared output is the selection receipt consumed by the public runner.

All four checked outputs use complete explicit projections. Within each case, prepared `other` equals selected `other`; selected/recovered entire `calledWorkflows` collections equal; selected `selected` equals finished `selected`. Each call projection includes its complete call, dispatch, child workflow, all child activities/incidents, original keyed start source, control/cancel/result sources and exact physical waits with generation/scope. No fields are excluded from these comparisons. Parent global activity state is expected to advance during each child's handling and is compared in full only across the restart. Final parents complete with both calls retired, expected branch counts and no waits/incidents/poison or semantic structural state.

The generic `test_opted_called_snapshot_reader.py` accepts compressed databases using the same phase/API/hash plus `--reader-assembly` and `--output`. Six controls per phase accept the actual whole snapshot and reject wrong hash, foreign API artifact hash/version, an owed physical orphan hidden behind a canonical source, and one exact actual calling dispatch whose consumer declaration is removed in a private copy. Prepared controls also independently require the returned selection to equal the second token in the actual root state. A compiled observation-only mutation swaps selected/other token indexes and reverses the reader's internal selection check: it returns the first token and the independent prepared control fails. The mutation never reaches a positive host or changes production adapter code; exact source restoration passes again.

Four earlier coded/escalation reader suites plus the legacy reader pass 29 compatibility controls. Together with the new 24 controls, 53 pass. Those rechecks retain the original positive snapshots and source scope; they do not restamp historical host executions as current-head or Studio proof. The original helper function bodies were copied exactly to `_CalledControlCommon.ps1`, with only the final separator blank line normalized at EOF; the existing public coded/escalation runner changes only its dot-source routing.

This qualifies two actual Balanced concurrent-call policies and one real process restart. It does not count adapter evaluations or prove acknowledgment loss, simultaneous SQL claims, other host/database engines, final Studio or production acceptance. T045 and the remaining delivery gates stay open.
