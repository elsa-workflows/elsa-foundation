# Four-provider persistence checkpoint

This internal checkpoint completes T021–T024: Rules provider contexts, separate generated migration sets, shared real-engine store contracts, and required-provider evidence tooling. It does not complete #3181. Installed-package identity, independent-process recovery, actual native continuation, upgrade/rollback, Workbench REST and final PR/main gates remain pending.

## Implemented boundary

The Rules EF feature selects SQLite, PostgreSQL, SQL Server or MySQL through the existing module binding and schema gate. Each context supplies a live database clock, ordinal string storage, bounded indexed keys and provider-appropriate large text/binary columns. MySQL explicitly uses utf8mb4 for supplementary Unicode. The product package still references only the common EF closure; engine packages belong to the test/migration host and the deploying host. Pooled and non-pooled factories resolve the same store/reader implementation.

The three added Initial migrations were generated through the real tooling contexts. Existing SQLite migration history was retained unchanged. All four providers passed `has-pending-model-changes`. The migration helper selects one provider, retains its history, rejects duplicate names and redirected destination paths, and excludes deployment-specific schema/connection settings during generation.

The provider fixture creates a separate disposable database for each case. Configured server connections stay in environment variables; only generated database names are dropped. Without a supplied connection, native-engine containers are owned by the fixture and disposed after execution. Require-all mode rejects unavailable engines. The driver records complete TRX counters, per-provider results and source fingerprints before/after execution; missing, skipped or failed results and changing source prevent qualification. It redacts configured connection/password values from retained runner output.

## Executed matrix

The final driver run rebuilt restored source and passed **8 cases, zero failed/skipped**. Each case runs the shared invariant journey, rather than counting each assertion as a test. Both pooling settings passed on every engine.

| Provider | Actual server version | Passed cases |
|---|---|---|
| SQLite | 3.50.4 | 2 |
| PostgreSQL | 16.15 | 2 |
| SQL Server | 16.00.4185 | 2 |
| MySQL | 8.4.11 | 2 |

The journey covers real migration application/validation; live time advancing within a transaction; direct success and budget fault; exact scoped identities, nullable tenants, case and Unicode normalization; exact/one-over UTF-8 limits for tenant/partition/invocation; bounded hash keys and a simulated hash collision retaining the full identity; supplementary Unicode facts; competing admission and receipt-consumption reads; model revision independent from renewal; receipt replay retaining the first physical attempt; interpreter-free common receipt consumption; stable paged snapshots; pending-receipt cancellation; work expiry after save with full rollback and reclaim; direct terminal results without an outbox; terminal/fact/receipt/outbox atomic rollback after an injected save failure; and delivery expiry after save with rollback, reclaim, stale-fence rejection and successful recovery.

Delivery also exercises unsupported result schemas, wrong scope/fence, missing or mismatched proof, acknowledgement replay and changed-proof conflict. Its proof validator is synthetic. These tests prove Rules store behavior, not a durable native parent commit. Competing delivery-claim/acknowledgement interleavings have focused SQLite coverage from the earlier checkpoint; they are not claimed as newly run four-provider race cases. Schema coexistence with retained native state belongs to T026.

The existing focused Rules suite also passed **191 tests, zero failed/skipped** after the provider implementation. Architecture/API checks initially passed 85 of 86: the dependency-closure check lacked evaluated Debug assets for the new test/tooling project. Narrow Debug/Release restores supplied those assets, and that one check then passed. This is 86 completed checks across those runs, not a falsely described uninterrupted green run. Existing tooling dependency/analyzer warnings remain outside this checkpoint.

## Review and control evidence

Root integration and bounded delegated reviews covered context SQL/mapping, generated migrations, feature registration, fixtures, tests, driver and migration tooling. Corrections included exact exception assertions, pending-cancellation confirmation semantics, stronger nonempty transition checks, failed-race cleanup, source-stability/counter checks, output redaction and destination checks. The test expecting LeaseLost after an already committed cancellation was corrected to the existing TerminalUnchanged contract; production cancellation semantics were not changed.

The clock removal control changed PostgreSQL `clock_timestamp()` to `transaction_timestamp()` in the actual context. Both pooling cases failed the intended assertion, “The provider clock must advance inside a transaction,” with no compilation failure or skips. Original source was restored byte-for-byte, then the complete four-engine suite passed. [Control receipt](provider-clock-control.json) and [qualification receipt](provider-qualification.json) retain hashes and results. The qualification records a stable, dirty precommit source fingerprint at base `646cfc234`; evidence prose and task-status updates were added afterward. It is not misrepresented as an exact-commit CI run.

Local maps regenerated and the map/solution-filter checks passed; generated findings report no runtime-to-design direct reference signal. Current generated maps are ignored local artifacts and were not staged. Diff and script syntax checks passed. No workflow or permission configuration changed; only the existing EF suite registry gained the Rules project.

## Commands and retained artifacts

```sh
python3 tools/rules/verify-providers.py --require-all --evidence-dir /tmp/3181-providers-final
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
# Following the narrow evaluated-asset restores, rerun the sole setup failure:
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
```

Final raw artifacts: `/tmp/3181-providers-final/run-4f8d9ed51ade46bea5b5b8d0b892d15d/{receipt.json,rules-providers.trx,command.log}`. Other retained local logs use `/tmp/3181-providers-`: `clock-control.log`, `sqlite-regression.log`, `architecture.log`, `closure-restored.log`, `model-{build,Sqlite,PostgreSql,SqlServer,MySql}.log`, and `maps-{all,check}.log`/`filters-check.log`. Durable receipts contain hashes; raw logs remain local. Physical external effects remain at least once, and historical unrelated timeout causes remain unproved.
