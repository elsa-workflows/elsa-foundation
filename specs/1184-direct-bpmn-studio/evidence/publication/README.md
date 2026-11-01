# Authored direct publication: actual host control

Executing source: `9d3fd45f092cc88d55d59b8b8f674ba1f62c9098`. [Raw33-file archive](https://github.com/elsa-workflows/elsa-foundation/releases/download/contributor-preview-2026-10-07.1/elsa-bpmn-authored-publication-9d3fd45f0.zip), [verified receipt](receipt.json), [portability result](portability.json).

The actual production Workbench test submitted/reopened/promoted a supported authored human process, published it through the optional version-addressed endpoint, round-tripped its binding through XML, started a direct model and retained its human wait across a process restart. It completed exact work, restarted again and replayed completion without another revision. All16 captured products were recorded; every Elsa product matches the executing source.

Four consistent whole stores are retained: Design/Runtime at wait and completion. The existing read-only provider observer verified revision1→2, adapter frame1, one→two input receipts, exact authored source/version2.0.0, unchanged executable/authority/incarnation and zero native workflow/activity/bookmark rows. Each store passed integrity/hash checks. This is direct execution without a native BpmnProcess wrapper.

| Check | Observed result |
|---|---|
| New source-aware runtime controls before implementation |6failed with the default-refusing operation; initial PublicAPI tracking build diagnostic retained separately from these behavior failures. |
| Source-aware/legacy replay/provenance checks |8passed;93affected direct-runtime cases also passed. |
| Interchange/publisher/optional route/capability tests |167passed after the real-host catalog correction. |
| Existing capability domain tests |13passed. |
| Affected Core/Runtime/ABI-file/shared-host architecture checks |34passed. |
| Committed solution-filter freshness |Initially2stale files; regenerated only those changes, then passed. |
| First actual host attempt, source48259a150 |Failed before start: catalog FindVersion does not hydrate Definition; publisher dereferenced that absent parent. |
| Corrected actual host, source9d3fd45f0 |1passed,0failed,0skipped; two actual restarts and completion replay. |
| Old compiled capability consumer |Unchanged binary passed against current API/Core assemblies, including old type names, constructors, registration deduplication and conflict catch. |
| Downloaded archive portability |Every member verified; extracted checkpoint verifier and relative-path old-client runner passed. This check did not repeat the host journey. |

The capability declaration models moved unchanged into their domain Core so the publisher consumes a contract rather than its implementation. Existing API entry points/type forwarding and Workbench shared-package declarations are preserved. Existing external dependency versions/hashes were unchanged; lock changes record the project edges.

Reproduce the host control from its source with `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` set to a fresh directory and `ELSA_BPMN_HOST_SOURCE_REVISION` set to that full source SHA:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj --configuration Release --filter 'FullyQualifiedName~DirectBpmnStudioPublicationTests'
python3.14 specs/1184-direct-bpmn-studio/evidence/publication/verify.py --evidence /path/to/evidence --reader tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll
```

The initial authored profile is closed human work; service selection remains T020–021. **Direct Studio browser acceptance, new inspection/discovery, actual runtime-only host qualification, both frozen saved-Studio families, claims/ETL UI, final package-version gates and full PR review remain open.** The host and unit controls do not establish them. No full PR review round has started. #2907 and the full program remain open.
