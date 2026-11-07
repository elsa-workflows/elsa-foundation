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


## T003 metadata and migration source increment (partial)

The source adds one unanchored preparation metadata table, checked codec/projections, consumer-context mapping, the proposed RuntimeArtifact 1.0→1.1→1.2 chain and compatible family-writer confirmation. It preserves the scoped-publication predecessor contract without installing a protected scope producer. Native participation, retention commands/query/GC integration, real owner acceptance and complete durable journeys remain open; T003 stays unchecked.

The exact 47-file code candidate is recorded in [t003-validation.json](t003-validation.json). The local code commit is `a5096310f5e1c887434e0aef1e24386b45f65f18`, with tree `91328419f0d11367bf3efc187ed7b7fa145def2b`; all 47 blob hashes match frozen candidate manifest `c4a08b5e6138014825a0ade9ded46c36fb040a750f7922471148049a985f6f18`. This is a source publication increment, not schema/API ratification or approval of the native adapter, SQLite lifecycle profile or settlement protocol.

Actual checks: the pre-generation source33 candidate passed 404 Runtime cases including 115 newly authored cases, while selected Persistence checks passed 27 and failed 2. A later one-case observability run retained its actual FAILED outcome and four command responses, proving that run's pending-model/apply/status failure sequence. Those historical outcomes are unchanged. The corrected source47 candidate then built and passed 13/13 migration-boundary cases plus 68/68 affected checks, including both previous failures, with zero skips. Offline SQL used all four supported providers and configured PostgreSQL/SQL Server schemas; the only fresh database execution control was synthetic SQLite.

Two isolated wrong-source variants reproduced both defects: six raw-Down cases FAILED at the first direct-generator no-exception refusal assertion, and six old-throw cases FAILED with its exact exception (two configured-schema factory failures; four default-schema Down inspections). These twelve outcomes are FAILED negative controls, not failures or extra passes of the corrected candidate. No physical database application occurred in those negative runs. The corrected source, original baseline, locks, binaries and evidence were unchanged.

The positive SQLite control preserves eleven physical row field values, including ContentJson, and applied migration IDs after refusal/reopen. Its rows are synthetic physical records, not valid retained/terminal protocol documents. It proves neither page-level database identity, full history ProductVersion equality, real provider-server upgrades, authenticated old-writer recovery nor domain/owner behavior. Actual retained predecessor data, native migrations/readiness/finalization and old-reader qualification remain pending; captured original inputs are never regenerated or overwritten.

The machine-readable receipt separates every phase, source identity, result and limitation. Public checkpoints are [corrected checks](https://github.com/elsa-workflows/elsa-foundation/pull/3951#issuecomment-6690326261) and [causal controls](https://github.com/elsa-workflows/elsa-foundation/pull/3951#issuecomment-6690639599). Raw logs/TRX are retained privately; this summary does not claim they are downloadable artifacts. No whole-project/architecture/maps/required-CI/full-PR or protected-delivery gate is supplied by focused checks alone. The existing six-round full-PR bound and review-scope mapping remain unchanged. #3804 receives no new merge gate.

Tests preceded that local commit on the exact matching source blobs. Their binaries retain the earlier working-copy HEAD provenance; no exact-commit rebuild or CI result is inferred from source equivalence.

## Draft CI correction — affected whole projects

The genuine automatic CI run [42261646884](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42261646884) failed on published head `d40575caa14dda8f0c575b1b3b750fbd9c64be21`. Its retained failure groups were the generic Core shape guard, four payload-column inventory rows, and six readable-version inventory assertions. That run remains failed.

A five-file test/guard correction reuses the existing exact full-shape opaque-handle predicate, pins the independently expected 36 payload columns with the retention converter witness, and pins readable versions 1.0/1.1/1.2 with two positive membership cases. Production/API/type/constructor/schema/codec behavior, ratchet entries, ordinary Session/engine negatives and future-2.0 refusal are unchanged. Three independent focused source reviews were clean.

| Whole local project on the frozen corrected source | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Architecture | 620 | 0 | 0 |
| EF Migrations | 525 | 0 | 0 |
| Cluster Readability | 124 | 0 | 0 |

Root independently verified source/lock/binary/log custody and fresh owned-process closure. Architecture's 605 discovery descriptors expand to 620 cases (603 exact plus source-bound groups of 11 and 6); Migrations' 511 descriptors expand to 525 (510 ordinary entries with duplicate truncated display multiplicity preserved, plus 15 source-bound contexts). The initial verifier's incomplete/refusal receipts remain unchanged, with separate additive reconciliation. Readability's 124 discovered and executed cases match directly, including all six former failures and both new positive versions.

The isolated generic-guard causal control built successfully and executed exactly seven cases: two intended FAILED outcomes (the generic guard naming only `WorkflowPreparationOwnerSession`, and the new Fact at its earlier handle `Assert.False`) and five unchanged specialized guard/control passes, zero skips. These failures belong to the intentionally incorrect isolated source, not the corrected candidate. The later constructor-negative assertions do not rerun after that earlier Fact failure; the 620-case positive suite supplies their proof. All three positive source/lock/binary inventories remained unchanged and all owned process groups were absent. The raw receipt is pinned in the machine-readable evidence. Batch source publication remains proposed. These local binaries were built on working-copy source based on d405; this is neither a new exact-commit CI pass nor full-current-PR approval. T003 remains unchecked. Authentic old-writer/provider upgrade and recovery, native participation, owner integration, maps/API/full-PR gates, T066, ETL/Studio and production acceptance remain open. Conservative review accounting counts the original post-PR T002 source R1 as **one consumed cumulative PR round**, retaining its 12-path/tree `486c63354ae2191c21b7c0d8a95fb9b223b57d62` scope. Its exact initiation classification is unrecoverable; it is counted rather than silently excluded. The next formal round is **cumulative R2**, within the unchanged six-round cap. Original Plan R1–R3 receipts explicitly reviewed design before PR creation (04:59–06:59 UTC versus 07:22:40 UTC). Any later-recovered formal receipt must be added immediately. No current 75-path review approval follows from that accounting or the focused source preflights.
