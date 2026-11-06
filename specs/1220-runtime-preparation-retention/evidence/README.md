# T002 compiled contracts — 6 November 2026

Part of #2892. This is the contracts increment for draft PR #3951, not durable preparation retention, a provider qualification, T066 acceptance or an additional #3804 gate.

Source base: `c7b0aed97ac5ce9edadcbff4ff15b338604845c9`, containing current main `6adeeaa0aa731b8b62893f23d1d9d8e9519cd2fa`. Three independent T002 R1 reviewers (correctness, standards, acceptance) inspected all 12 paths at source tree `486c63354ae2191c21b7c0d8a95fb9b223b57d62`; all reported CLEAN. Subsequent changes only record these results and the completed T002 checkbox. This is not the full implementation PR review loop.

The held Core and EF declarations remain unchanged: `9cc6f6b831065c1233f4456d644aa2b273e98aa4ad70df4ce56b8ad8b7a25676` and `6a361822b285e465a9366841deb4f0b4d374f012ab8d4c4e5f1bf5fd12a93b25`. The existing reader/writer inherit fail-closed default members; legacy providers report no preparation capability and throw for unsupported preparation operations. Core stays EF-free. Only the same-domain Runtime implementation receives friend access to the opaque owner's internal constructor.

| Actual check | Result |
| --- | --- |
| Locked restore: Core, Runtime EF, Runtime tests, Architecture | Passed |
| Core compile with public API analyzer enabled | Passed after recording 370 analyzer-emitted additions |
| Runtime EF compile | Passed |
| `WorkflowPreparationContractTests` | 14 passed, 0 failed, 0 skipped |
| `RuntimeCoreEngineShapeGuardTests` | 5 passed, 0 failed, 0 skipped |
| Remove only exact contract-handle admission, rebuild, run existing Core engine guard | Expected failure: 1/1; sole offender `WorkflowPreparationOwnerSession` |
| Restore exact reviewed guard bytes, rebuild, rerun affected guard tests | 5 passed, 0 failed, 0 skipped |

`dotnet format analyzers ... --diagnostics RS0016` exited zero with a workspace warning and made no edits; that was not API qualification. The first unsuppressed Core build failed with exactly 370 unique RS0016 symbols, all from the new preparation surface. Their canonical analyzer strings were added verbatim to `PublicAPI.Unshipped.txt`, preserving every old row. The next analyzer-enabled Core build passed. No analyzer or gate was disabled. Existing build warnings remain visible in the retained raw logs.

The architecture correction recognizes only the exact approved contract type while checking its sealed shape, internal constructor, two readonly fields and fixed forwarding surface. General `Session`/engine predicates remain, with unrelated Session negatives and actual engine liveness. The contract tests exercise callback expiry and before-Join refusal. The causal counterfactual confirms the old general predicate misclassified this particular contract model; it grants no engine exemption (ADR 0033).

Commands used the normal `dotnet` on PATH, one local phase at a time, `DOTNET_PROCESSOR_COUNT=4`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`, `-m:1`, `-nr:false`, and `-p:UseSharedCompilation=false` for builds/tests. Restores used `--locked-mode --disable-parallel`; tests used `--no-restore`, the exact class filters above, and TRX output. All processes finished within their recorded bounds; the local heavy slot was released.

Machine-readable source, binary, TRX counters, command and raw-log hashes are in [t002-validation.json](t002-validation.json). Raw logs/TRX remain in the task's durable continuation checkpoint outside Git. No old-binary provider replay, whole project or architecture suite, schema/native topology, real Publishing adapter, cold-process retention/GC journey, full-PR CI or merge acceptance is claimed. Those remain in T003–T020. T001's minimal authorized helper adoption is still deferred until its first actual consumer.
