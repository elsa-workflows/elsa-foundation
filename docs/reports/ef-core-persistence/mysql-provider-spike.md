# EF Core 10 MySQL provider feasibility spike

**Issue:** [#1675](https://github.com/elsa-workflows/elsa-foundation/issues/1675)

**Parent:** [#1673](https://github.com/elsa-workflows/elsa-foundation/issues/1673)

**Status:** Adopt with constraints; executable proof passed

**Bounded proof path:** `tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/`

**Report path:** `docs/reports/ef-core-persistence/mysql-provider-spike.md`

**Last verified:** 2026-09-12

This report records the candidate facts and executable evidence for the
isolated MySQL spike. Package metadata, documentation, and a container starting
successfully were not treated as proof. The focused proof ran against the
pinned image with 12 passed tests, no failures, and no skips; the recommendation
and constraints below follow from those results.

## Question and scope

Which exact stable EF Core 10-compatible MySQL provider, version, server image,
and mechanics can satisfy the EF Core persistence program without leaking
persistence concerns into domain contracts?

This spike owns only MySQL-specific provider selection and executable feasibility
evidence. It starts after [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671)
and ADR [#1666](https://github.com/elsa-workflows/elsa-foundation/issues/1666)
are merged and verified on `main`.

The proof is test-only. It may add package/version declarations needed by the
isolated proof, but it must not add a production MySQL feature, shipped
provider registration, default composition, or module replacement.

## Non-goals and ownership boundaries

- [#1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669) owns the reusable four-provider migration lifecycle, migration artifact isolation, locking, and operator flow.
- [#1674](https://github.com/elsa-workflows/elsa-foundation/issues/1674) owns the generic cross-module transaction topology, enlistment, and split-database policy.
- [#1678](https://github.com/elsa-workflows/elsa-foundation/issues/1678) and the owning module issues own provider-neutral foundation and production module implementation.
- This spike does not remove Groundwork or MongoDB, add production registration, flip defaults, convert production data, change public/domain APIs, or implement generic migrations or transactions.
- This spike contains no performance benchmark, timing measurement, throughput result, budget, performance gate, or performance workflow activity. The correctness checks below are timing-independent.

## Candidate record

The following are verified package, repository, and environment facts. They are
inputs to the proof, not proof results.

| Concern | Candidate or observed value | Evidence and interpretation |
| --- | --- | --- |
| EF Core provider package | Oracle `MySql.EntityFrameworkCore` **10.0.9** | [NuGet package metadata](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9) marks the package stable, reports SemVer metadata `10.0.9+MySQL26.7.0`, includes `net10.0`, and declares EF Core 10-compatible dependencies. |
| EF Core provider package name at runtime | `MySql.EntityFrameworkCore` | Verified from `Database.ProviderName`; the provider informational version begins with `10.0.9` and the loaded provider/connector assembly versions begin with `26.7.0`. |
| ADO.NET connector | `MySql.Data` **26.7.0** | [NuGet package metadata](https://www.nuget.org/packages/MySql.Data/26.7.0) identifies `MySql.Data` as Connector/NET's core package and lists `net10.0` compatibility. |
| Provider dependency floor for `net10.0` | `Microsoft.EntityFrameworkCore >= 10.0.9`, `Microsoft.EntityFrameworkCore.Relational >= 10.0.9`, `MySql.Data >= 26.7.0` | Declared on the [provider's `net10.0` dependency group](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9). The repository pins EF Core **10.0.10**, so the exact 10.0.10 combination still requires executable validation. |
| Repository EF Core pin | **10.0.10** | `Directory.Packages.props`, `Microsoft.EntityFrameworkCore` and related EF Core packages. |
| Repository target framework | **`net10.0`** | `Directory.Build.props` sets the shared target framework. |
| Local SDK | **10.0.300** | Verified locally with `dotnet --version` on 2026-09-12. |
| Local .NET runtime | `Microsoft.NETCore.App 10.0.8` and `Microsoft.AspNetCore.App 10.0.8` installed | Verified locally with `dotnet --list-runtimes`; the proof must record the runtime selected by its actual test command. |
| Provider API | `UseMySQL` | The [official Connector/NET EF Core documentation](https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core.html) shows `DbContextOptionsBuilder.UseMySQL(...)`. |
| MySQL server requirement | MySQL **8.0 or later** | Stated in the official [Connector/NET EF Core requirements](https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core.html). |
| Pinned test image | `docker.io/library/mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a` | The `8.4.11` tag and immutable multi-platform digest were verified locally with Docker tooling. The [official MySQL image page](https://hub.docker.com/_/mysql) is the image source. |
| Testcontainers integration | `Testcontainers.MySql` **4.15.0** | [NuGet metadata](https://www.nuget.org/packages/Testcontainers.MySql/4.15.0) lists the package as compatible with `net10.0`; it is a test-environment dependency, not a production provider. |
| Oracle package license | `GPL-2.0-only WITH Universal-FOSS-exception-1.0` | This expression is shown in the [provider's NuGet metadata](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9) and also on [`MySql.Data` 26.7.0](https://www.nuget.org/packages/MySql.Data/26.7.0). This is not a legal conclusion. Legal/commercial distribution review is required before either package is shipped in an Elsa distribution; test-only use does not settle that review. |

### Candidate selection and rejection record

The initial candidate is the stable Oracle package pair above. The official
MySQL package page says that `MySql.EntityFrameworkCore` adds EF Core 8, 9, and
10 support, while the versioned Connector/NET guide contains a support table
that describes Connector/NET 9.4.0 in the EF Core 10.0-preview column. That
version-sensitive documentation is a reason to run the exact package and EF
Core 10.0.10 proof, not a reason to claim compatibility in advance.

The stable [Pomelo 9.0.0 release](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases/tag/9.0.0)
is rejected for this spike because its stable line targets EF Core 9 rather
than the repository's EF Core 10 line. No preview or nightly Pomelo package or
unreleased branch is adopted as a substitute for a stable EF Core 10 candidate.

## Execution record

The final local verification ran on branch `codex/ef-core10-mysql-spike`, based
on verified-green `main` commit `f487aa0f0131b4414a0d349baab03d9f080c6200`.
The immutable review head must be recorded in the PR comments after commit and
push; this report cannot truthfully name that future commit.

| Evidence | Exact command actually run | Result |
| --- | --- | --- |
| Environment and package graph | `dotnet --version`; `dotnet --list-runtimes`; `docker image inspect mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a --format '{{.Id}} {{.Architecture}} {{index .RepoDigests 0}}'`; `dotnet list tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj package --include-transitive` | Passed: SDK 10.0.300, selected runtime 10.0.8, arm64 image resolved to the pinned digest, and the requested provider/connector/EF versions resolved exactly. |
| Clean package-source mapping restore | `dotnet restore tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj --packages /tmp/elsa-mysql-nuget.Tt3NpA --force --no-cache` | Passed after adding `MySql.*`, `Google.Protobuf`, and `K4os.*` to the repository's `nuget.org` source mapping. |
| Focused project build | `dotnet build tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj --configuration Release --nologo --no-restore` | Passed with 0 warnings and 0 errors. |
| Testcontainers proof against the pinned image | `dotnet test tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj --configuration Release --no-build --no-restore --logger 'console;verbosity=minimal'` | Passed: 12 tests, 0 failed, 0 skipped, running .NET 10.0.8 against the pinned MySQL 8.4.11 image. |
| Schema/SQL capture and migration inspection | `dotnet ef migrations script --project tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj --startup-project tests/Elsa/Persistence/EntityFrameworkCore/MySql/FeasibilityTests/Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests.csproj --context SecretsMySqlDbContext --configuration Release --no-build` | Passed: generated script creates the module-scoped history table, binary-collated table and key/lookup columns, JSON and binary columns, UTC-ticks column, composite primary key, and list index. |
| Solution inclusion and generated-filter check | `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters`; `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check` | Passed; the proof project is included in `Elsa.Server.slnx` and the generated integration filter. |
| Architecture contracts | `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --configuration Release --no-restore --logger 'console;verbosity=minimal'` | Passed: 257 tests, 0 failed, 0 skipped after placing the proof project in the canonical collapsed solution folder. |
| Generated-map refresh and freshness | `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`; `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | Passed; the six affected snapshots were refreshed and the authoritative byte-for-byte freshness check reports that the generated maps describe the tree. |

The completed proof used a fresh named database per test on the immutable image
above and asserted the actual provider and connector versions observed at
runtime, rather than relying only on package declarations. No spike command
measured elapsed time, throughput, a budget, or performance.

## Acceptance evidence

All eleven issue-defined evidence groups passed. Negative cases assert the
specific exception category or detection result used by downstream code; they
do not accept an arbitrary exception.

| # | Evidence group | Required result to record | Status |
| ---: | --- | --- | --- |
| 1 | Binding and model | `Oracle_provider_binds_to_ef10_and_exposes_the_selected_versions` verifies provider name, provider/connector versions, and the actual relational model. | **Passed** |
| 2 | Fresh install and migrations | `Fresh_install_reapply_and_no_pending_model_changes_are_proven` applies the MySQL migration to an empty database, verifies schema/history/indexes, reapplies it, and reports no pending migrations or model changes. | **Passed** |
| 3 | Wrong artifact / pending model negatives | `Wrong_migration_assembly_is_rejected_before_domain_work` proves the spike's test-owned preflight guard gets the exact `InvalidOperationException` before a domain table exists; EF does not supply that policy automatically, so #1669 owns the reusable production guard. `Pending_model_change_is_detected_without_running_domain_work` returns `true` for a deliberately distinct cached model. | **Passed within the spike boundary** |
| 4 | Date and time | `DateTimeOffset_uses_utc_ticks_bigint_while_json_keeps_the_original_offset` proves UTC and non-zero-offset values with non-microsecond-aligned tick boundaries round-trip exactly as UTC ticks while JSON preserves each original offset. | **Passed** |
| 5 | Collations and normalized keys | `Binary_collation_and_Elsa_normalized_keys_preserve_ordinal_identity_and_unicode_equivalence` starts from a conflicting `utf8mb4_0900_ai_ci` database default, verifies seven live columns are `utf8mb4_bin`, stores ASCII- and Unicode-case-distinct keys, and exercises ASCII/Unicode normalization. | **Passed** |
| 6 | JSON | `Json_round_trip_query_and_partial_update_preserve_unmodified_payload` queries a nested path, applies `JSON_SET`, and verifies nested, numeric, Unicode-safe, and unmodified values semantically. | **Passed** |
| 7 | Optimistic concurrency | `Two_context_updates_produce_one_success_and_one_detectable_concurrency_conflict` verifies one save and one `DbUpdateConcurrencyException`, with the winning payload and changed token retained. | **Passed** |
| 8 | Unique conflicts | `Duplicate_normalized_key_exposes_stable_MySql_metadata` verifies `DbUpdateException` wrapping `MySqlException` number 1062 and the primary-key diagnostic. | **Passed** |
| 9 | Conditional DML | `ExecuteUpdate_and_delete_return_exact_match_counts_for_guarded_operations` verifies exact affected-row results 1/0 for both update and delete. | **Passed** |
| 10 | Transactions | `Shared_connection_transaction_commits_or_rolls_back_and_non_enlisted_context_isolated` keeps two distinct contexts alive and enlists both in one externally owned connection/transaction, proves commit and rollback, proves a separate connection cannot observe uncommitted work, and proves its independent write commits outside the shared unit. | **Passed** |
| 11 | Restart | `Restart_reconnects_to_migrated_data_and_preserves_concurrency_state` disposes all prior contexts/connections, reconnects, and verifies data, token, and migration state. | **Passed** |

## Schema and SQL observations

These are evidence slots, not predictions. Fill them with generated SQL or
schema inspection from the proof and link each observation to its test name.

| Observation | Evidence to capture | Status |
| --- | --- | --- |
| Migration history | `__EFMigrationsHistory_ElsaSecretsMySqlSpike` contains `20260912081709_Initial`; reapply is idempotent and both pending-migration and pending-model results are empty/false for the baseline model. | Verified |
| Tables and keys | `elsa_secrets`; composite primary key `(TenantId, NormalizedName)`; index `IX_elsa_secrets_tenantId_status_normalizedName`; seven identity/lookup columns verified live as `utf8mb4_bin` despite a conflicting database default. | Verified |
| Type mappings | `varchar(256)` identity columns, `longtext` lookup/search projections, `json` payload, `varbinary(16)` concurrency token, and `bigint` UTC ticks for full-fidelity `DateTimeOffset` projection. | Verified |
| Generated DML | JSON-path query/update works; guarded update/delete return 1 then 0; duplicate identity produces EF `DbUpdateException` with MySQL error 1062; stale token produces `DbUpdateConcurrencyException`. | Verified |
| Transaction/enlistment SQL | Contexts constructed with one external `MySqlConnection` enlist in its `MySqlTransaction`; commit and rollback are observed, and the non-enlisted connection cannot observe the uncommitted row. | Verified |
| Restart state | Reopened context reads the same payload and 16-byte concurrency token and reports no pending migration. | Verified |

## Known limitations and review risks

- The official Connector/NET guide states that MySQL Server 8.0 or later is required and that memory-optimized tables are not supported. Elsa-specific use of those or other unsupported features is not inferred from this spike.
- The provider's package metadata and the versioned Connector/NET support table expose different levels of EF Core 10 wording; the exact `MySql.EntityFrameworkCore` 10.0.9 plus EF Core 10.0.10 combination therefore requires the executable proof.
- Native MySQL `datetime(6)` cannot retain .NET's 100 ns precision or the original offset. The proof therefore stores the searchable projection as UTC ticks in `BIGINT`; the JSON document retains the original offset. Production mappings must preserve this convention.
- Oracle-specific `ForMySQLHasCollation` metadata is required for the migration SQL to emit explicit `COLLATE utf8mb4_bin`; relying on generic or database-default collation is not acceptable.
- Oracle's migration scaffolder emits an unqualified `MySQLModelBuilderExtensions.HasCharSet` call while the generated model also contains `.UseCollation(...)`. Importing the Oracle extension namespace makes that call ambiguous with EF's relational extension, so the proof fully qualifies `HasCharSet` in generated designer/snapshot files. #1669 must decide how generation tooling makes that repair deterministic.
- Repository package source mapping must admit `MySql.*` plus the connector's `Google.Protobuf` and `K4os.*` dependencies for clean restores.
- Oracle's GPL-2.0-only-with-exception licensing requires legal/commercial distribution review before a production dependency decision. The package choice cannot be finalized from the NuGet license expression alone.
- Testcontainers and a pinned Docker image prove only the isolated test environment. They do not prove production deployment policy, migrations operations, cross-module transaction topology, or module correctness.
- There is deliberately no performance or timing evidence in this spike.

## Decision

**Adopt with constraints.** Oracle `MySql.EntityFrameworkCore` 10.0.9 with
`MySql.Data` 26.7.0 is executable with EF Core 10.0.10 and satisfies the bounded
correctness contract against MySQL 8.4.11. Adoption is conditional on:

- legal/commercial distribution approval for the GPL-with-FOSS-exception packages;
- UTC-ticks `BIGINT` for full-fidelity searchable `DateTimeOffset` projections;
- explicit Oracle collation metadata and live schema verification against a conflicting database default;
- deterministic repair or wrapping of the migration-scaffolding namespace ambiguity;
- explicit duplicate-key classification from `DbUpdateException` / `MySqlException` metadata;
- production migration lifecycle and transaction-topology decisions remaining with #1669 and #1674.

This is a provider feasibility decision, not production registration or a
module-replacement decision.

## Follow-ups after the proof

The spike must leave these as explicit follow-up work; it must not claim any of
them complete:

- **[#1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669):** carry the selected provider's migration assembly identity, history-table isolation, provider-specific DDL limitations, fresh-install and pending-model observations, and operator prerequisites into the generic four-provider migration lifecycle.
- **[#1674](https://github.com/elsa-workflows/elsa-foundation/issues/1674):** carry the MySQL shared-connection/shared-transaction, enlistment, rollback, isolation, affected-row, and split-database findings into the generic transaction topology. Any unsupported behavior must become an explicit refusal/constraint.
- **[#1678](https://github.com/elsa-workflows/elsa-foundation/issues/1678):** carry provider-neutral mechanics only after removing MySQL-specific details from domain contracts; retain provider binding, model validation, context lifetime, migration, concurrency, and diagnostics seams in the shared EF foundation.
- **Owning module issues:** provide the selected provider mapping and constraints to Runtime/distributed Runtime [#1672/#1676], Design/Publishing/Dashboard/import [#1677], Diagnostics [#1681], Identity [#1682], Secrets [#1679], and Studio Preferences [#1680]. Each owner must prove its own schema and behavioral surface; this spike does not close those units.

## Sources used

### Official external sources

- [MySql.EntityFrameworkCore 10.0.9 — NuGet](https://www.nuget.org/packages/MySql.EntityFrameworkCore/10.0.9)
- [MySql.Data 26.7.0 — NuGet](https://www.nuget.org/packages/MySql.Data/26.7.0)
- [MySQL Connector/NET: Entity Framework Core support](https://dev.mysql.com/doc/connector-net/en/connector-net-entityframework-core.html)
- [Testcontainers.MySql 4.15.0 — NuGet](https://www.nuget.org/packages/Testcontainers.MySql/4.15.0)
- [MySQL official Docker image](https://hub.docker.com/_/mysql)
- [Pomelo.EntityFrameworkCore.MySql 9.0.0 release](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/releases/tag/9.0.0)

### Repository and program sources

- [Issue #1675](https://github.com/elsa-workflows/elsa-foundation/issues/1675)
- [`Directory.Packages.props`](https://github.com/elsa-workflows/elsa-foundation/blob/main/Directory.Packages.props) for the EF Core 10.0.10 pin.
- [`Directory.Build.props`](https://github.com/elsa-workflows/elsa-foundation/blob/main/Directory.Build.props) for the shared `net10.0` target.
- [`docs/reports/ef-core-persistence/repository-surface-register.md`](./repository-surface-register.md) and [`storage-unit-register.md`](./storage-unit-register.md) for program ownership and downstream persistence surfaces.
