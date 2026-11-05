# Evidence: the ETL runtime store's schema on all four EF engines (spec 1203)

## Post-demo whole ETL qualification — 5 November

[Fresh source-bound receipt](qualification-resumed-etl.json) at tested head `154762f4714bc861d46289d30c055a51f6bb9d00`, tree `a300119e65e5ffd5d99fa3ff6bf4c2672c0686cf`, integrating delivered main `697e9a342`: **1328/1328**, zero failed/skipped/aborted/timed-out cases. Discovery lists1328 and matches every executed case name exactly. All three retained-ID corruption controls pass in that complete project. The earlier3 red→3 green bite remains at its recorded pre/post sources; it is not relabelled as a new mutation.

One locked Release whole-project invocation ran with `-m:1 -p:BuildInParallel=false -p:RestoreLockedMode=true`, followed by no-build/no-restore discovery. Test362.56s; discovery0.68s; phase364.28s. Child `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` were retained. This limits MSBuild workers; xUnit kept its existing defaults. No retry or other project/provider/browser/Studio matrix was launched. Existing compiler/package warnings remain in the hashed raw build log; zero test warnings is not a compiler-warning claim.

The observer recorded69 owned processes, including62 ordinary ETL worker fixtures, across three observed groups. No owned live descendant remained after the leader, no cleanup signal was needed, and the heavy slot was returned. No new owned shared compiler was observed; baseline/user processes were preserved. Explicit launches and sampled ancestry are distinguished from a complete kernel trace.

Native-provider whole execution, architecture/generator gates, fresh scoped R9, all three actual exact-head required checks, protected delivery and resulting-main verification remain pending. Server registration is still guarded. This pass does not prove upgraded-host recovery or Studio compatibility. The counts below retain their historical sources; this entry supersedes only their pending whole-ETL status. This documentation update contains no product/source correction and does not renew execution at a later evidence-only commit.

## Earlier closeout — 5 November (before pause)

Parent #3737 protected-merged at `db74c19a9`; schema #3803 now targets main. Ordinary merge `6ca230209` retains descendant main `5a3ad480a` and its shared Core publication-contract integration. No migration, frozen authoring/state or writer is removed.

Full review7 at `d7105b668` found one P2: an invalid retained run ID could throw an argument error through the engine key seam and block a later valid delivery. Corrected at `cc4253459` by classifying that retained-data validation as `InvalidDataException` before key derivation. Client-input validation remains an argument error, and the global scanner's exception filter is not widened.

[Source-bound controls and receipts](review-round7.json): the three whitespace/untrimmed/control-character cases fail **3/3 before** the correction and pass **3/3 after**, with zero skips. The whole shared SQLite runtime-store contract then passes **262/262**, zero failed/skipped; discovery also lists262 cases. These are a filtered contract run, **not** a new whole-project ETL pass. The first control attempt stopped at compilation because of a test-helper reference; it is separately recorded and is not a product bite. No test container or composed host was started for these controls.

Fresh whole ETL/native-provider/architecture/generator qualification and all exact-head required checks remain pending. Historical whole-suite counts below retain their real sources. The new shared cases still need execution on all three servers; runtime registration remains guarded until spec1206. Full review8 follows on the completed candidate. Direct-parent mainCI41945105078 was cancelled; descendant CI41947341383 and fresh resulting-main host qualification remain separate pending gates. Broader recovery and Studio acceptance stay open.

## Requirements and tests

| Requirement | Evidence |
|---|---|
| FR-001: provider contexts | `EtlRuntimeSqlServerDbContext`, `EtlRuntimePostgreSqlDbContext` and `EtlRuntimeMySqlDbContext` (over `EtlRuntimeServerDbContext`) beside SQLite, each applying `EfOrdinalCollation`; MySQL adds the `utf8mb4` character set. `Runtime_key_columns_use_the_engines_binary_collation_and_hashed_run_keys` reads each server's recorded collation for `ScopeKey`, `RunKey` and `BatchId`. No `[EfModule]` entry is added: the registration still refuses these engines (FR-007). |
| FR-002: run-key seam | `EtlRuntimeDbContext.RunKeyOf`: the base64 UTF-16 encoding on SQLite (unchanged), `EfRelationalIdentity.HashLengthFramed("etl-run-key-v1", runId)` on the server engines, where every `RunKey` column is 64 characters. The stores derive keys and check identities through it (`ValidateRun` compares the stored key with `RunKeyOf(runId)` and the decoded `RunId` with the run id). `Run_ids_of_maximum_length_or_differing_only_by_case_are_distinct_runs` passes on all four engines. |
| FR-003: per-engine check constraints | `EtlRuntimeConstraintDialect` renders the 33 constraint templates after main reconciliation (quoting, `length`/`LEN`/`CHAR_LENGTH`, `1`/`0` or `TRUE`/`FALSE`). The SQLite rendering is byte-identical: the SQLite model snapshot is unchanged and its pending-model-changes check passes. `Check_constraints_refuse_impossible_connector_run_counters` passes on all four engines. The two long alternate keys are renamed on the server engines only. |
| FR-004: exact totals | No change was needed: the existing run-partition totals and inspector counts pass the contracts on all three server engines (including Oracle's MySQL provider) as they are. |
| FR-005: generated migrations | `Migrations/EtlRuntime/{SqlServer,PostgreSql,MySql}/*_Initial` and additive `ContinuousCompaction` migrations generated by `tools/etl/generate-etl-design-migrations.sh <Name> <Provider> runtime` through `Elsa.Etl.Tooling`'s runtime factories, as internal classes; the SQLite chain is untouched. `A_fresh_runtime_database_migrates_validates_and_matches_the_model` on each server engine. |
| FR-006: contracts and CI | The runtime-store tests that do not race are contract classes (`Extensions/Etl/tests/Shared/RuntimeStore`), run on SQLite by `Elsa.Etl.Tests` (`EtlRuntimeStoreTests`, with SQLite-only cases in `EtlRuntimeStoreSqliteOnlyTests` and `EtlConnectorRequestConstraintTests`) and on PostgreSQL 16, SQL Server 2022 and MySQL 8.4 by `Elsa.Etl.Persistence.ProviderTests` (container suite `etl-ef`, shard `ef-3`). Server engines run one command per connection at a time, so two store reads were restructured to run no query inside an open reader: the HTTP page audit checks accepted offsets after its page stream ends, and the acceptance history reads settled batches in keyset chunks of 64. |
| FR-007: registration still refuses | `The_runtime_registration_still_refuses_server_engines_until_their_transitions_are_qualified` on each server engine; the ETL README and the persistence README say the runtime store is SQLite-only until spec 1206. |

## Suites

Fresh local qualification after reconciliation with main; receipts and discovery reconciliation are in [`qualification.json`](qualification.json). All complete suite executions below have zero failures and zero skips.

| Command (Release) | Executing source | Result |
|---|---|---|
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release` | `b56db13f5` | **1325 / 1325** |
| `ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS=1 dotnet test Extensions/Etl/tests/Persistence/ProviderTests -c Release` | `6e74eb3ca` | **801 / 801** on PostgreSQL 16, SQL Server 2022 and MySQL 8.4; 267 per engine (232 runtime/schema + 35 design) |
| `bash tools/architecture/restore-ci-project-graph.sh --locked-mode` | `b56db13f5` | Both Release and isolated Debug restores pass, including locked `Elsa.Server.slnx` restoration |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release` | `b56db13f5` | **616 / 616**; no dependency-guard change required |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- solution-filters-check` | `b56db13f5` | Pass; no committed filters change |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- test-tiers-check` | `b56db13f5` | Pass; 133 projects, each in one tier and shard |

`--no-build --list-tests` lists 1325 ETL and 801 provider cases, exactly matching execution. Architecture lists 601 cases and executes 616: all 361 listed methods execute, with two deferred theories expanding by 15 cases; their exact names and counts are recorded in the receipt. No listed method is missing and no unexpected method appears.

After the provider run, code commits `cdb654ea7` and `b56db13f5` adapt four incoming helper calls in three SQLite-only test files. ProviderTests includes none of those files, and all its source/build inputs remain unchanged. Subsequent qualification-document changes do not rerun application products or renew actual-host evidence. Exact published-head required CI remains a separate gate.

Diagnostics retained separately: the pre-reconciliation ETL run passed 1317/1317; a later build stopped before test execution at an incoming `TestDatabase` caller (CS0103), corrected in `b56db13f5`. Neither is substituted for the final 1325-case run. The prior session's 696-case provider claim remains historical at its recorded source; this fresh whole project executes 801 cases. Existing package-audit warnings remain tracked in #3707; no suppression is added.

## Bite-proofs

[`bite-proofs.json`](bite-proofs.json) has five entries. The first four bit at `a93f1d045`; the new collation bite bit at `6e74eb3ca` after adopting main:
- the server run-key hash: with the SQLite encoding, maximum-length run ids overflow the 64-character `RunKey` on SQL Server;
- the PostgreSQL boolean literals, the MySQL `CHAR_LENGTH` and the SQL Server `LEN`: each wrong rendering makes the model disagree with the committed migration, so the model-agreement test fails on that engine;
- the binary collation on the server contexts: removing `EfOrdinalCollation.Apply` from `EtlRuntimeServerDbContext.ConfigureProvider` makes all three server model-agreement cases fail (3 failed, 0 passed); restoring it passes all three (0 failed, 3 passed).

**Not bite-proven:**
- **The HTTP page audit's accepted-offset check** (`VerifyAcceptedItemCountsAsync`). Removing the call survives the runtime-store suite: the corruptions that would reach it (a settled batch on the wrong page, a page's accepted count changed) are refused first by the acceptance history's ordered batch audit or the page chain's own offset checks. The check was a per-page query inside the stream before this slice and was moved, not added.
- **The acceptance history's keyset chunking** (`ConnectorBatchesInOrder`): the contracts settle fewer than 64 batches per partition, so no test crosses a chunk boundary.
- **`ValidateRun`'s key comparison**: no test stores a run whose `RunKey` differs from `RunKeyOf(RunId)`.
- **The check constraints' refusal on each boolean column**: `Check_constraints_refuse_impossible_connector_run_counters` refuses two counters; the boolean renderings are bite-proven through model agreement only.
- **The SQLite-only request-constraint tests** stay SQLite-only because they write raw SQLite text; the same constraints on the server engines are covered by model agreement.
- **The MySQL `utf8mb4` annotation and the shortened alternate-key names**: tested through model agreement, not mutated.
- **Tooling**: the script's `runtime` argument and the runtime factories were exercised by generating the migrations, not by tests.

## Limits

- The runtime registration still refuses SQL Server, PostgreSQL and MySQL. Retried transitions, race tests and a Workbench journey with both stores on each engine are spec 1206.
- Racing runtime-store tests (claim races, lease expiry under contention) stay SQLite-only until spec 1206.
- Studio is unchanged by this slice.

## Continuation corrections

- Adopted main `73040cd7b` through parent #3737 at `e4dde3cdf`; retained every existing server Initial and SQLite migration. The additive server `ContinuousCompaction` migrations include the eight compaction columns and current predecessor constraint. [`sqlite-constraint-comparison.json`](sqlite-constraint-comparison.json) records all 33 SQLite expressions matching that base byte for byte. This source comparison is distinct from database model-agreement tests.
- All extracted runtime contracts use `SkippableFact` / `SkippableTheory`. [`optional-skip-proof.json`](optional-skip-proof.json) records an intentionally unavailable fixture: the old attribute reports one failure, the corrected attribute reports one skip, and required-provider mode still reports one failure. The temporary fixture injection was restored. An earlier attempt to select an unavailable Docker socket still reached the available provider and passed; it is not counted as an unavailability proof.
- Every test container generates its password per run. No password or connection string is committed by this continuation.
- FR-004 and the plan now describe the existing provider-qualified SQL aggregates, matching the code and task record.

## Current stack qualification — 4 November

[Fresh whole-project receipts](qualification-current-stack.json) at clean `24bc183925c604a8574c46d2ba57975aa3f8f94b`: **ETL1325/1325, provider804/804, architecture616/616**, zero failed/skipped; locked graph restore and both generator checks pass. Provider804 contains 801 native design/runtime-schema cases and three controlled design-database initialization cases from the parent; it does not mean 804 server cases. The earlier801 result retains its historical source. Parent comment/receipt-only commits66e9/6bc are subsequently adopted.

Continuation docs now name runtime successor1206 (1204 was independently reserved for named embedded blocks). New C# summaries keep traceability in these records rather than code-comment spec citations; the retained refusal keeps its exception/SQLite-only outcome with the obsolete spec-number suffix removed. No migration, writer, frozen authoring/state shape or public API is removed. Complete review round4 and final-main/required-check/base-first gates remain. The successor's 36 both-store host checks are separate evidence on PR3804, not evidence that this schema-only PR enables server runtime registration.

## Full review round4

[Corrections and controls](review-round4.json): the three independent full reviewers found an allocated runtime test-database leak on initialization failure and stale successor/diagnostic documentation. The fixture now releases the allocation and preserves the original exception even if cleanup fails, matching the design fixture. Three new controlled cases fail before the correction; afterward **9/9 focused cases** pass (six design/runtime controlled initialization cases and three native registration-refusal cases). READMEs now name successor1206, and the plan describes the plain guard message. No schema, authoring/state, migration, publicAPI or successful registration behavior changes. Prior whole-provider804 retains its actual source; the new whole807-case run, full review5 and delivery gates remain pending.

## Main reconciliation and complete provider follow-up

The fresh required-native whole provider project passes **807/807**, zero failed/skipped, at clean source `5c26c218c2dc1579559a1131a9711132a871a2e4`: 801 native cases and six controlled initialization cases. [Receipt](qualification-current-stack.json) retains raw counters/log/TRX hashes. Full round5 is clean on `f846aefcf`; subsequent main9da reconciliation comes through parent7190843f0 without a further ETL implementation change. Final full review6 and combined-head CI remain. No old result is relabelled as a new-main execution.

Main9da's activity-authoring module follows its pre-GA recreation policy; its legacy Runtime recovery harness explicitly selects a separate fresh ActivitiesDesign store. That source does not establish ordinary upgraded-host authoring or Studio compatibility. The existing frozen artifacts and legacy producer products remain separate retained evidence; this schema PR neither deletes a writer nor makes an upgrade/Studio claim.
