# Actual opted-in called-control Workbench evidence

These are whole module stores and observations from the standard source-built Workbench. See [the receipt](../opted-called-workbench-recovery.md) and its [source/artifact manifest](../opted-called-workbench-recovery.json). No synthetic control, claim, activity or source was installed in the positive stores.

`coded-runtime.sqlite.gz` follows two actual authored error cases. `parked-runtime.sqlite.gz` holds six actual escalation cases immediately before process exit. `recovered-runtime.sqlite.gz` follows a fresh standard process opening the same live stores, before any continuation. `finished-runtime.sqlite.gz` follows real public Event continuations. `design.sqlite.gz` is the complete coherent Activities/Workflows Design and Publishing store captured after the first process exits. It contains the original published definitions for all eight cases. Runtime owns its complete executable material. Identity/token storage was separate from both module stores and is excluded.

The databases retain original CLR catalog identifiers, including owned build-directory provenance. They are historical data; do not recreate those paths or rewrite the stored bytes. Database-identity/schema-finalization tables describe module storage identity and migrations, and contain no identity-provider accounts or tokens. All original table data is retained. The original source-host binary closure is identical to the already qualified [released-0.5.0 host closure](../legacy-workbench-called/upgraded-host-binaries.json).

For a fresh execution, use an isolated current Workbench with the documented [module-store configuration](../legacy-workbench-called/README.md), a private identity database, Balanced persistence and the normal outbox processor. Use a separate owned controller/process and explicit free port; never attach to or stop a bystander's host. The runner performs only public API calls:

```powershell
./e2e-tests/bpmn/Test-BpmnCalledControlHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostBuildSource -Phase CodedErrors -EvidencePath $codedReceipt
./e2e-tests/bpmn/Test-BpmnCalledControlHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostBuildSource -Phase PrepareEscalations -EvidencePath $preparedReceipt
# Freeze coherent private SQLite backups. Stop and await this controller's exact owned child only.
# Start a fresh standard process over the same original module stores.
./e2e-tests/bpmn/Test-BpmnCalledControlHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostBuildSource -Phase ObserveEscalations -PreparedEvidencePath $preparedReceipt -EvidencePath $recoveredReceipt
# Freeze another closed backup before continuing; compare all six checked records exactly.
./e2e-tests/bpmn/Test-BpmnCalledControlHost.ps1 -BaseUrl $ownedBaseUrl -SourceRevision $hostBuildSource -Phase FinishEscalations -PreparedEvidencePath $preparedReceipt -EvidencePath $finishedReceipt
```

Each Event has `CanStartWorkflow=false`. Its correlation comes from the canonical pure expression parameter `WorkflowInstance.instanceId`; public ResumeOnly dispatch must target exactly that workflow and start none. The six prepared policies are running escalation, terminal escalation and terminal escalation plus authored error, each with an interrupting or noninterrupting boundary. Five real warning waits hold the callers live; the noninterrupting combined-failure caller is already Faulted.

To independently inspect the portable fixture, decompress each database to a new closed private file without sidecars. Build `tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` with locked restore, then invoke:

```text
dotnet <reader.dll> --inspect-opted-called <runtime.sqlite> <uncompressed-sha256> default <api-receipt.json> <phase>
```

The matching triples are `coded-runtime`/`coded-api.json`/`coded`, `parked-runtime`/`prepare-api.json`/`escalation-parked`, `recovered-runtime`/`recovered-api.json`/`escalation-recovered`, and `finished-runtime`/`finished-api.json`/`escalation-finished`. Adjacent database JSON files contain hashes. `parked-checked.json` and `recovered-checked.json` have exactly equal complete `calledWorkflows` collections; whole database files may differ due unrelated operational startup state. The checked reader opens immutable/query-only stores, validates the hash before and after, and emits a bounded failure type on rejection.

`test_opted_called_snapshot_reader.py` accepts these same compressed databases through `--database`, `--sha256`, `--api-receipt` and `--phase`, plus `--reader-assembly` and `--output`. It verifies six controls per phase: positive whole fixture, wrong hash, foreign API artifact hash, foreign API artifact version, owed physical orphan behind an unchanged canonical source, and a real dispatch whose consumer declaration is removed in a private negative copy. Invalid variants are explicitly closed/backed up, have no sidecars and pass SQLite integrity. They are never fed to the positive host. `reader-mutation.json` records a compiled missing-declaration mutation that incorrectly accepted its private invalid copy and therefore failed the control; the exact source and positive refusal were restored.

This bundle qualifies eight public-host cases and one real process restart in Balanced mode. It does not count control-adapter callback entries, duplicate delivery after acknowledgment loss, concurrent same-binding public-host calls, final Studio or production acceptance. Those remain separate gates. Three independent component audits do not consume a full-PR review round.
