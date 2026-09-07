# EF Removal Intake Inventory

**Task**: T002 — mechanical intake freeze

**Source ref**: `origin/main` at `f769b516598eb807c9528e7c2e72085b346603e8`

**Captured intake baseline**: `tests/Elsa/Architecture/Baselines/ef-core-surface.json` (schema `1`, SHA-256 `909ff9369a0d2e2defc6f717a87580458a80fdfa2038b279307b49419581f16f`). This hash and the scoreboard below are the immutable intake record; the committed temporary scoreboard is refreshed as each admitted deletion slice removes stale entries.

**Frozen ASP.NET Core Identity oracle baseline**: retired with issue #1482 together with its ratchet test and protected Identity EF tree; the historical hashes remain above only as provenance for the intake review.
**Mechanical entry count**: `308`

This is an intake record, not permission to delete an entry. The canonical exact identities remain
the baseline JSON; the deterministic classifier below produces one ownership row for every one of
its entries without copying a second mutable 308-row list into a hand-maintained document.

## Mechanical scoreboard

| Scanner category | Count | Final requirement |
|---|---:|---|
| `EfProjects` | 8 | Empty |
| `DirectPackageReferences` | 24 | Empty |
| `CentralPackageVersions` | 9 | Empty |
| `SharedBuildPackageReferences` | 0 | Empty |
| `DirectEfProjectReferences` | 19 | Empty |
| `TransitiveEfProjectConsumers` | 22 | Empty |
| `TransitiveEfPackageConsumers` | 57 | Empty |
| `ResolvedEfPackageConsumers` | 103 | Empty and receipt-bound |
| `ProjectsMissingAssets` | 0 | Empty; every project has current receipt-bound assets |
| `MigrationFiles` | 11 | Empty |
| `DbContextFiles` | 11 | Empty |
| `RegistrationFiles` | 41 | Empty |
| `HostConfigurationFiles` | 3 | Empty |
| `EfFreeBoundaryViolations` | 0 | Empty |
| **Total** | **308** | **Absolute zero after #647** |

## Issue #1482 current reconciliation (2026-09-01)

The Identity EF leaf is absent from the current tree. The current shrink-only scoreboard contains
seven EF projects, 13 direct package edges, eight central package versions, 17 direct EF project
edges, 19 static transitive project consumers, 40 static transitive package consumers, 78 restored
package consumers, five migrations, nine context files, 25 registration files, and one host
configuration entry. The remaining entries are diagnostics, the explicitly retained OpenIddict
vendor boundary, and the shared EF substrate; they are not part of issue #1482's deletion scope.

The following current-source reconciliations are part of this slice:

- `src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/` and its solution edge are deleted.
- `tests/Elsa/Architecture/Baselines/frozen-aspnetcore-identity-ef-oracle.json` and
  `FrozenAspNetCoreIdentityEfOracleRatchetTests` are deleted; the temporary repository-wide baseline
  has only its stale Identity entries removed.
- `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/EfCoreIdentityStoreTests.cs` and the
  EF-vs-Groundwork Identity comparator files are deleted after their Groundwork replacements were
  verified; the durable divergence ledger remains historical evidence, not a #646 verdict.
- Shared API/Identity fixtures use `IdentityV2TestPersistence` and Groundwork Identity stores. The
  OpenIddict vendor EF test context and package remain intentionally retained under ADR 0042.

Issue #646 remains open without an accepted final performance verdict. This reconciliation therefore
does not certify spec 144's final zero-EF gate or advance T011; it records only the explicit Identity
EF deletion requested by issue #1482.

## Exact-entry classification

### Deterministic ownership rule

Apply the first matching rule to the literal baseline identity. These rules classify each entry
exactly once; `unknown` is an intake failure, not a permissible family.

| Priority | Predicate on literal baseline identity | `OwningFamily` |
|---:|---|---|
| 1 | starts with `Directory.Packages.props` or `src/Apps/Elsa.Server/` | `host-package` |
| 2 | contains `/Diagnostics/OpenTelemetry/` | `diagnostics-opentelemetry` |
| 3 | contains `/Diagnostics/StructuredLogs/` | `diagnostics-structured-logs` |
| 4 | starts with `tests/` | `test-oracle` |
| 5 | contains `/Foundation/Identity/OpenIddict/` | `openiddict` |
| 6 | contains `/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/` | `identity-ef-oracle` |
| 7 | contains `/Elsa/Persistence/EFCore/` | `shared-ef-substrate` |
| 8 | no prior match | `unknown` (fails T002) |

The `test-oracle` family is intentionally before source Identity/OpenIddict rules: its Foundation
Identity and Modularity consumers mix both temporary oracle paths, so the method-level retention
ledger—not an arbitrary path split—owns their final disposition.

An auditor can mechanically prove the one-to-one mapping and reproduce the exact entries with:

```bash
cd /path/to/elsa-foundation
baseline=tests/Elsa/Architecture/Baselines/ef-core-surface.json

jq -r '
  def owner:
    if startswith("Directory.Packages.props") or startswith("src/Apps/Elsa.Server/") then "host-package"
    elif contains("/Diagnostics/OpenTelemetry/") then "diagnostics-opentelemetry"
    elif contains("/Diagnostics/StructuredLogs/") then "diagnostics-structured-logs"
    elif startswith("tests/") then "test-oracle"
    elif contains("/Foundation/Identity/OpenIddict/") then "openiddict"
    elif contains("/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/") then "identity-ef-oracle"
    elif contains("/Elsa/Persistence/EFCore/") then "shared-ef-substrate"
    else "unknown" end;
  [.Surface | to_entries[] | .key as $category | .value[] | {category: $category, identity: ., owner: owner}]
  | if length != 308 then error("baseline entry total changed; refresh this intake") else . end
  | if any(.[]; .owner == "unknown") then error("unclassified intake entry") else . end
  | group_by(.owner)[]
  | "\(.[0].owner)\t\(length)\t\(map("\(.category)\t\(.identity)") | join("\n"))"
' "$baseline"

# A separate full literal enumeration, suitable for a review attachment:
jq -r '.Surface | to_entries[] | .key as $category | .value[] | "\($category)\t\(.)"' "$baseline"
```

The first command must emit 308 tab-separated rows collectively, with no `unknown` group. It
therefore proves that every canonical baseline identity has exactly one family mapping. The second
command is the literal source of record for every individual path and `consumer -> dependency`
tuple.

### Family content and exact categories

| Owning family | Exact source roots / mechanical entry shapes | Categories represented |
|---|---|---|
| `diagnostics-opentelemetry` | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/{Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.csproj,Sqlite/Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Sqlite.csproj}`; OTel EF/SQLite project edges and package consumers; `DbContext/OpenTelemetryDbContext.cs`; two SQLite migration artifacts; EF feature registrations | projects, direct/static/resolved packages, project edges, migration, context, registrations |
| `diagnostics-structured-logs` | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/{Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.csproj,Sqlite/Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite.csproj}`; Structured Logs EF/SQLite edges and package consumers; `DbContext/StructuredLogsDbContext.cs`; three SQLite migration artifacts; EF feature registrations | projects, direct/static/resolved packages, project edges, migration, context, registrations |
| `openiddict` | `src/Elsa/Foundation/Identity/OpenIddict/Elsa.Foundation.Identity.OpenIddict.csproj` → `Microsoft.EntityFrameworkCore.{Design,InMemory,Sqlite}`, `OpenIddict.EntityFrameworkCore`; its EF DbContext, SQLite factory, three migration artifacts, and `AddDbContext`/`UseEntityFrameworkCore` registration path | direct/static/resolved packages, migration, context, registrations |
| `identity-ef-oracle` | `src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj` → Identity EF/Core/Design/InMemory/SQLite; `ApplicationIdentityDbContext`; three migrations; feature/factory/store registrations | projects, direct/static/resolved packages, project edges, migration, context, registrations |
| `shared-ef-substrate` | `src/Elsa/Persistence/EFCore/{Elsa.Persistence.EFCore.csproj,Sqlite/Elsa.Persistence.EFCore.Sqlite.csproj}`; base/SQLite package/edge consumers; `ElsaDbContextBase.cs`; shared feature and SQLite registrations | projects, direct/static/resolved packages, project edges, context, registrations |
| `test-oracle` | diagnostics hosts; `tests/Elsa/Foundation/Identity/Tests`; `tests/Elsa/Modularity/Tests`; and `tests/Elsa/Persistence/EFCore/Tests` including its EF-named project, provider packages, six DbContext-scanned tests, and provider registrations | projects, direct/static/resolved packages, project edges, contexts, registrations |
| `host-package` | `src/Apps/Elsa.Server/{Elsa.Server.csproj,Program.cs,appsettings.json,shells.json,shells.Production.json}` and all nine `Directory.Packages.props` versions | direct/static/resolved packages, project edges, registrations, host configuration |

The canonical baseline holds these exact cardinalities that matter when reviewing the compact
family descriptions above:

- Eight EF-named projects: two OpenTelemetry, two Structured Logs, one Identity oracle, two shared
  substrate, and one EF test project.
- Eleven migrations: 2 OpenTelemetry, 3 Structured Logs, 3 Identity oracle, and 3 OpenIddict.
- Eleven DbContext-scanned files: 1 OpenTelemetry, 1 Structured Logs, 1 Identity oracle, 1
  OpenIddict, 1 shared substrate, and 6 EF test sources.
- Three host-config entries: EF logging in `appsettings.json` and Identity EF feature keys in
  `shells.json` and `shells.Production.json`.
- No current shared-build package references, missing-assets entries, or EF-free boundary
  violations.

### Exhaustive package-consumer sets

The following compact notation is lossless: each listed package is one exact baseline
`consumer -> package` entry. It provides an auditable reading view of all 57 static and 103
resolved consumer entries while the literal JSON remains canonical.

| Consumer | Static transitive EF packages | Resolved EF packages |
|---|---|---|
| `src/Apps/Elsa.Server/Elsa.Server.csproj` | Identity EF, EF Core, Design, InMemory, SQLite, OpenIddict EF | static set plus Abstractions, Analyzers, Relational, SQLite.Core, OpenIddict EF.Models |
| OTel EF | EF Core, Relational | EF Core, Abstractions, Analyzers, Relational |
| OTel SQLite EF | EF Core, Design, Relational, SQLite | EF Core, Abstractions, Analyzers, Design, Relational, SQLite, SQLite.Core |
| Structured Logs EF | EF Core, Relational | EF Core, Abstractions, Analyzers, Relational |
| Structured Logs SQLite EF | EF Core, Design, Relational, SQLite | EF Core, Abstractions, Analyzers, Design, Relational, SQLite, SQLite.Core |
| Identity oracle | Identity EF, EF Core, Design, InMemory, SQLite | Identity EF, EF Core, Abstractions, Analyzers, Design, InMemory, Relational, SQLite, SQLite.Core |
| OpenIddict | EF Core Design, InMemory, SQLite, OpenIddict EF | EF Core, Abstractions, Analyzers, Design, InMemory, Relational, SQLite, SQLite.Core, OpenIddict EF, OpenIddict EF.Models |
| Shared EF | EF Core, Relational | EF Core, Abstractions, Analyzers, Relational |
| Shared EF SQLite | EF Core, Relational, SQLite | EF Core, Abstractions, Analyzers, Relational, SQLite, SQLite.Core |
| OTel tests | EF Core, Design, Relational, SQLite | EF Core, Abstractions, Analyzers, Relational, SQLite, SQLite.Core |
| Structured Logs tests | EF Core, Design, Relational, SQLite | EF Core, Abstractions, Analyzers, Relational, SQLite, SQLite.Core |
| Foundation Identity tests | Identity EF, EF Core, Design, InMemory, SQLite, OpenIddict EF | Identity EF, EF Core, Abstractions, Analyzers, Design, InMemory, Relational, SQLite, SQLite.Core, OpenIddict EF, OpenIddict EF.Models |
| Modularity tests | Identity EF, EF Core, Design, InMemory, SQLite, OpenIddict EF | Identity EF, EF Core, Abstractions, Analyzers, InMemory, Relational, SQLite, SQLite.Core, OpenIddict EF, OpenIddict EF.Models |
| EF persistence tests | EF Core, Relational, SQL Server, SQLite, Npgsql EF PostgreSQL | EF Core, Abstractions, Analyzers, Relational, SQL Server, SQLite, SQLite.Core, Npgsql EF PostgreSQL |

Names abbreviated in this table are expanded exactly by the literal-enumeration command above:
`Identity EF = Microsoft.AspNetCore.Identity.EntityFrameworkCore`; `EF Core =
Microsoft.EntityFrameworkCore`; `OpenIddict EF = OpenIddict.EntityFrameworkCore`; `Npgsql EF
PostgreSQL = Npgsql.EntityFrameworkCore.PostgreSQL`. Each suffix is the corresponding
`Microsoft.EntityFrameworkCore.*` identity.

## Admission gates and removal DAG

| Family / slice | Required gate IDs | Earliest tasks | Dependency order |
|---|---|---|---|
| OpenTelemetry EF | `diagnostics-four-provider` (#642/T009); `performance-verdicts` (#646/T011); T037/T040 test dispositions | T041, T043 | First leaf; before shared substrate |
| Structured Logs EF | `diagnostics-four-provider` (#642/T009); `performance-verdicts` (#646/T011); T037/T040 test dispositions | T042, T043 | First leaf; before shared substrate |
| OpenIddict EF | `openiddict-conformance` (#643/T010); `performance-verdicts` (#646/T011); T036/T040 | T044, T045 | After its lane/oracle evidence; before shared substrate |
| Identity EF oracle | `performance-verdicts` (#646/T011), specifically IAM/Identity evidence; T035/T040; temporary comparison import complete | T046, T045, T047 | After all load-bearing #646 Identity comparisons; before shared substrate |
| Shared EF substrate and EF tests | successful prior three leaf deletions; T038/T040 | T048–T050 | After diagnostics, OpenIddict, and Identity leave no dependent edge |
| Host / solution / central packages | #643, #932, T017 and T019–T028 for the early host slice; all prior deletion surfaces for final cleanup | T029–T031, then T051–T054 | Serial order: shells → production shells → server project → solution → central/project packages → appsettings/residue |
| Permanent guard | all previous categories actually zero and all-project receipt present | T059–T071 | Delete both temporary baselines only with completed oracle retirement |

```text
T009 + T011 ──> Diagnostics OTel / Structured Logs EF leaves ──┐
T010 + T011 ──> OpenIddict EF leaf ─────────────────────────────┤
T011 (Identity verdict) ──> frozen Identity EF oracle ──────────┤
                                                               ├─> shared EF substrate + EF tests
#643 + #932 + host gates ──> serialized Groundwork host slice ─┤    └─> solution/package/settings cleanup
                                                                       └─> absolute-zero guard
```

## Temporary benchmark-oracle and tool coverage

`benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/` is not a separate EF-named project and
therefore has no direct baseline row at this head. It is nevertheless a load-bearing **temporary
consumer of the frozen Identity EF oracle's observable results** through the #646 workload/evidence
chain. T047 must inventory and remove its retained EF comparison target/temporary adapter only
after T011's verdict import, while preserving #646 evidence identities. Do not use the absence of a
baseline row as evidence that the benchmark oracle is removable.

The same rule covers design-time EF tools, factories, migrations, and test infrastructure: scanner
rows identify source surface, but T003/T004's method-level test-retention ledger and T047's
benchmark-oracle disposition are additional mandatory evidence. The correct final inventory has no
scanner entries **and** no unaccounted temporary oracle/tool consumer.

## Scanner reconciliation and required final response

`EfCoreSurfaceSnapshot` has all 14 fields serialized by the baseline. T063 reconciled the intake
category-model split by adding `EfFreeBoundaryViolations` to `Categories()` and proving the
baseline comparator now reports an expansion in that category. The existing dedicated
`Core_and_groundwork_projects_are_ef_free_now` test remains in place. T064 is still open and must
make the production assertion require a valid current receipt plus an empty value for every
category; no temporary baseline/update behavior was retired early.

`ProjectsMissingAssets: []` at intake proved only that the old scanner found files. T061/T063/T068
now make dependency evidence receipt-bound and reject stale assets, changed dependency inputs,
driver/tool/worktree drift, copied assets identities, non-canonical NuGet configuration, and
project-set omission. Bash and PowerShell independently restored the same 246-project set and
produced matching project/input/assets identities; T064/T071 still own the permanent production
assertion and final exact-head zero certification.

No baseline field is currently stale or missing relative to the scanner record. The older
program-brief counts of 21 direct project edges, 34 static transitive project consumers, and 59
static transitive package consumers are stale; this exact-head intake is authoritative for #647.
