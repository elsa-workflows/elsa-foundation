# Quickstart: Validate Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

This guide is the executable evidence checklist for issue #641. Commands assume the repository root and Release configuration.

## Prerequisites

- .NET 10 SDK.
- Docker or compatible provider containers for SQL Server, PostgreSQL, and MongoDB.
- A MongoDB replica set or sharded deployment for the transaction-required suite.
- One binary-compatible Groundwork version across `Groundwork.Core`, `Groundwork.Documents`, all four provider packages, and `Groundwork.Tool`.
- Provider connection values supplied through environment variables; never commit or pass secrets in process-visible command arguments.

## Baseline record (T001 complete; T004 deferred to T025)

The work unit is based on `origin/main` commit `d1548991f`, captured on 2026-07-20.
The repository currently pins the Groundwork library family, including SQL Server and
MongoDB, and `Groundwork.Tool` to `0.0.1-preview.72`. The public preview feed reports
`Groundwork.Tool` `0.0.1-preview.73` as its newest release; `0.0.1-preview.75` is not
available. This records the actual baseline for T001. T009—not T001—selects and pins
the later released binary-compatible library/tool family that provides the required
physical-storage and query APIs.

The captured provider images are SQLite (embedded), SQL Server
`mcr.microsoft.com/mssql/server:2022-CU21-ubuntu-22.04`, PostgreSQL
`postgres:17.6-alpine3.22`, and MongoDB `mongo:7.0.24`. One existing PostgreSQL
unified-host test still uses `postgres:16-alpine`; T053 selects and records its fixture
image as part of the provider contract work.

The pre-extraction EF behavioral suite inventories are 354 workflow-design tests and
467 activity-design tests. Their focused restore and discovery succeeded at this base;
full-solution Release discovery found 6,588 tests without executing them. The shared
fixture scaffold currently has 8 deterministic-fixture tests. See [baseline
evidence](evidence/baseline.md) for the commands and precise evidence scope.

### T021–T024 shared contract-suite scaffold

The shared project now contains abstract, executable-by-inheritance workflow and activity suites.
They exercise readiness, scoped lifecycle reads and writes, point-read scope isolation, wrong-scope
non-disclosure and write rejection, scope-local duplicate identities, true reusable-activity-draft
OCC, the workflow draft's intentional last-writer-wins policy, single-scope restart/result-hash parity,
and Groundwork-target cross-scope same-identity restart isolation; they also exercise draft and version
layout round-trips, clone provenance, validation rejection, descriptor serialization,
SemVer identity, missing outcomes, permanent-delete outcomes, and reconciliation idempotency. T023 adds
provider-neutral atomicity fault contracts for `AfterStagedWrite`, `BeforeProviderDecision`, and
`AfterDurableDecision`, with throw, cancellation, and non-success actions. Its canonical operation request
carries a caller-stable operation/idempotency key separately from the canonical request fingerprint. The
shared suite proves partial-staging rollback, non-success rollback, cancelled-token propagation, durable-result
reconciliation after acknowledgement loss, exact replay, same-key/different-fingerprint conflict without
mutation, scope-bound operation ledgers, and duplicate-outcome suppression. The scenarios reference only
Elsa core contracts plus `IDesignPersistenceContractFixture`; their atomicity snapshots expose canonical
aggregate-state and authoritative durable-result fingerprints for replay and conflict assertions. The reusable
test assembly is guarded against provider SDK assembly
references. T025 and T051–T054 must therefore place concrete EF-oracle and provider fixtures in separate
provider-specific test projects that reference this shared project; provider SDK references must not enter the
shared assembly. T024
currently asserts non-cancellation conflict classification plus unchanged state; T033/T035 own the
final domain-scoped write-conflict exception taxonomy and direct adapter mapping coverage. Fixture
staging is explicitly non-persisting; the activity suite proves both the candidate definition and
version are absent before it resolves and invokes the real `IActivityVersionReconciler`. A
provider-neutral behavioral harness executes that inherited scenario and proves that the resolved
reconciler is invoked on both idempotency passes. Captured domain events assert clone, validation,
discard, and reconciliation identities and outcomes, including empty validation errors. Canonical
restart snapshots contain the authored workflow state and activity descriptor, and assert each
against the deterministic input before comparing restart hashes.

The shared-contract scaffold completed with `96` passed tests and one intentional legacy-EF N/A skip
(`97` total); the skipped probe proves that N/A profile evaluation happens before fixture creation. T025's executable
`LegacyEfOracle` profile runs exactly five of these rows: partial-staging failure, cancellation, same-scope
duplicate identities, workflow-draft last-writer-wins, and the single-scope restart snapshot. Its ten explicit
N/A rows are provider non-success decision; acknowledgement loss, exact replay, key-reuse conflict, and duplicate
delivery; same identities across scopes; foreign reads and writes; reusable-activity-draft OCC; and cross-scope
same-identity restart isolation. The profile records a
nonempty reason per N/A row. Test-only EF fault injection is permitted for partial staging and cancellation;
T025 must not add a production EF operation ledger, scope boundary, or reusable-draft OCC shim. T033/T035's
exception-taxonomy deferral remains unchanged.

### T004/T025 EF SQLite oracle baseline (2026-07-20)

`Elsa.Persistence.Groundwork.DesignConformance.EFCore.Tests` is a separate provider-leaf test project.
It applies both existing SQLite migration chains to one temporary file database, recreates the composed
service provider on restart, uses public design commands/stores plus the real activity reconciler, and
keeps its partial-staging/cancellation faults in a test-only EF `SaveChangesInterceptor`. Its four
inherited contract suites completed with `15` passed and `10` intentional LegacyEfOracle N/A skips
(`25` total). Its two focused evidence tests pass as well, so the complete EF provider leaf reports
`17` passed, `10` skipped, and `27` total.

The deterministic canonical result hashes captured after restart are:

- workflow draft: `8614e1ffb739e65f08a47c60da7c33891fff5f88c32ef5d88eed7b9fd5800369`
- workflow version: `3fcb29139ffcac2c0fc3d5450d4e2682d3b504b86f31326b16b17f4712ceb9d4`
- activity definition/version: `8bdeb707ea537e6357a8327b2d7404deb23af9e370c65a03f2e2883b182bf3f1`

These are workload behavior hashes, not test-inventory hashes; the evidence test pins them.

### T025 Groundwork SQLite Target-profile baseline (2026-07-20)

`Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests` is a separate provider-leaf baseline
project. For every scenario it creates a fresh file-backed SQLite target, drives production
`Groundwork.Tool` `validate --offline`, `plan`, and `apply --safe` commands against
`GroundworkAllFeaturesDeploymentSchema`, then builds the public
`AddGroundworkSqliteUnifiedPersistence` composition with startup auto-apply disabled. Runtime
startup only admits the applied target. Restart scenarios dispose and rebuild the service provider
against the same file without reapplying schema, and every design operation resolves from a fresh
DI scope bound through `IPersistenceAccessContextBinder`.

The immutable Target-profile catalog executed all `25` T021–T024 rows and proved the ratified
intentional-red matrix:

- `17` green lifecycle, reconciliation, scope, OCC, last-writer-wins, and restart rows;
- `7` classified red atomicity rows:
  `design-atomicity-operation-ledger-absent`;
- `1` classified red same-scope duplicate row:
  `same-scope-identity-rejection-absent`.

The runner fails if any expected green becomes red, any expected red becomes green, or a red row
does not match its narrow classification. It also derives and pins the loaded Groundwork Core,
Documents, SQLite, and local-tool package versions plus the exact target and plan fingerprints;
schema-tool processes have a bounded timeout and are killed and reaped on timeout or cancellation.
The run used the Groundwork library/tool family
`0.0.1-preview.77`, target fingerprint
`b87b33275b2f9f52a8756eeb039bb8227a93083a6adacb95cd6a16c2445253d3`, and plan fingerprint
`d8717f5ebca1755d8aa24473c37da3a2dc823f70d069ec38fa4de40ff2b012f8`.
The T030 reference-composition change intentionally advances both fingerprints by adding the
scope-bound, dedicated `designOperation` document table and its atomic-commit route; the Target-profile
behavior matrix remains `17` green and `8` narrowly classified intentional-red rows until T031–T032
route the workflow and activity commands through that ledger.
It also exposed and fixed target correctness defects: promotion now copies the validated
scope-bound draft's `TenantId` to both the immutable workflow version and its layout, and
submission stamps the active scope onto every newly created aggregate member.
Both provider fixtures dispatch through Elsa's scoped event pipeline; the EF cancellation probe
pins the same linked token at the public command, event handler, EF interceptor, and exception.

### T030 shared atomic-write coordinator evidence (2026-07-20)

Implementation candidate `10469b79902b9cb2235f41dcddd1c148c5c7d898` centralizes stable
operation identity, canonical request fingerprints, replay/conflict inspection, rollback,
cancellation, marker-race handling, and independently bounded acknowledgement reconciliation in
`GroundworkDesignAtomicWrite`. Workflow and activity design registrations share one scoped helper
and one append-only, scope-bound `designOperation` manifest source. The marker uses a dedicated
physical document table, joins the mutated document kinds in one `CrossUnitAtomic` commit scope,
and requires both the `AtomicCommit` capability and the `multi-document-transactions` topology.
Prior successful deliveries return `Replayed` so callers suppress duplicate post-commit outcomes;
a successful commit recovered after a lost acknowledgement returns `Reconciled` so the current
attempt can complete its post-commit outcome phase exactly once, just as it does for `Committed`.
The internal marker unit is explicitly tracked by the architecture inventory without changing the
frozen spec 094 ALL32 public-contract denominator.

The exact implementation candidate passed:

- `GroundworkDesignAtomicWrite*`: `18/18`;
- full Groundwork querying: `112/112`;
- workflow design Groundwork: `60/60`;
- activity design Groundwork: `51/51`;
- Groundwork composition: `58/58`;
- Groundwork unified host: `23/23`;
- Groundwork core persistence: `637/637`;
- full architecture ratchets: `261/261`;
- SQLite Target-profile schema and 25-scenario behavior baseline: passed, preserving `17` green
  and `8` narrowly classified intentional-red rows;
- full `Elsa.Server.slnx` Release build: `0` errors and `147` accepted existing warnings;
- the exact CI-generated container-free filter: all `77` test projects passed.

An independent read-only review was clean after verifying terminal rollback observation against
Groundwork preview.77's normative `IDocumentUnitOfWork` contract. T031–T032 still own command
adoption, so this slice does not advance any of the seven intentional-red atomicity rows.

### T031–T035 design-command adoption evidence (2026-07-20)

Every workflow definition/version/draft/promote/submit/delete command and both activity
definition/version commands now route through the typed atomic facade. Public contracts require
an explicit caller-stable `DesignOperationKey`; operation identity remains separate from the
canonical request fingerprint, and generated definition/version/draft identities come back from
the durable authoritative result. Source reconciliation uses separate materialization commands so
API allocation policy does not leak into source-owned projection.

Groundwork reads and writes now surface provider-neutral `DesignPersistenceException` outcomes
with explicit Workflow/Activity and Provider/Serialization classifications. Only positively
classified provider, corrupt-payload, marker/result corruption, or explicitly scoped
serialization operations are mapped. Cancellation, readiness, validation, operation-key
conflict/rejection, uncertain-commit, and other domain outcomes retain their existing contracts.
Repeated provider composition resolves every public store/command, the shared atomic helper, and
each manifest source exactly once per scope.

Lost-acknowledgement reconciliation now detaches post-commit lifecycle-event enqueue from the
caller token that may have been cancelled after the durable commit. Create, update, and discard
tests prove one enqueue after reconciliation and none on the subsequent replay, preserving the
established best-effort/at-most-once deferred-event policy without claiming durable event
delivery. Promotion performs its marker lookup before source reads and locks, so an exact replay
returns the authoritative version even after the source draft is discarded. Submit validation
runs only after a marker miss, so mutable activity-structure projections cannot invalidate an
already committed exact replay. Clone owns a distinct operation kind whose canonical request is
the immutable source-version identity. Its marker lookup runs before source expansion, identity
allocation, locking, staging, or event publication, so exact replay returns the authoritative
draft even after the source version and layout are removed; reusing the key with a different
source identity conflicts before reading that source.

The SQLite Target profile now executes all seven provider-transaction atomicity scenarios through
a real scoped `GroundworkDesignAtomicWrite` over two admitted workflow document kinds plus the
durable marker. Its fixture-local post-commit observation proves retry/replay control flow only;
it is not a domain-event publication. Separately, the composed SQLite host invokes the public
`ICreateDraftCommand`, starts Elsa's real background event dispatcher, captures
`OnDraftCreated` in its ScopeA pipeline handler only after verifying the draft through
`IWorkflowDefinitionDraftStore`. Its test-only fault seam injects a throw after one staged write,
a rejected provider decision, cancellation, and a post-durable lost acknowledgement. Exact replay
uses the same logical operation identity in two storage scopes, proving that isolation comes from
the storage boundary rather than from scope
material embedded in the key. `OnDraftCreated` intentionally does not carry a storage scope and
the background dispatcher opens a fresh scope, so this lifecycle-event evidence is explicitly
limited to the ScopeA fixture; it does not prove cross-scope event-context propagation. Evidence
schema `3` advances the immutable
catalog from `17/8` to `25` green and `0` intentional-red scenarios while retaining target
fingerprint `b87b33275b2f9f52a8756eeb039bb8227a93083a6adacb95cd6a16c2445253d3`
and plan fingerprint `d8717f5ebca1755d8aa24473c37da3a2dc823f70d069ec38fa4de40ff2b012f8`.

The integrated pre-review candidate passed:

- persistence core: `43/43`;
- Groundwork querying: `126/126`;
- workflow design Groundwork: `81/81`;
- activity design Groundwork: `67/67`;
- focused SQLite atomicity/lifecycle suite: `8/8` — `7/7` provider-transaction scenarios plus
  `1/1` public `ICreateDraftCommand`/`OnDraftCreated` durability scenario;
- SQLite 25-scenario Target-profile baseline: `1/1`, with all `25` logical scenarios green;
- shared design conformance: `96` passed and `1` intentional profile-shape skip;
- temporary EF oracle: `17` passed and `10` explicitly inapplicable skips;
- Groundwork unified host: `23/23`;
- workflow design domain suite: `358/358`;
- activity design domain suite: `490/490`;
- workflow design API: `50/50`;
- activity design API: `5/5`;
- workflow publishing API: `381/381`;
- full `Elsa.Server.slnx` Release build: `0` errors.

T036 records the exact committed candidate and independent review after this evidence is committed.

Ordinary CI writes no evidence file. Operators can opt in to the sanitized allowlisted JSON
artifact without exposing connection strings, database paths, or scope identities:

```bash
ELSA_DESIGN_GROUNDWORK_BASELINE_EVIDENCE_DIR=/path/to/evidence \
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj -c Release
```

This closes only T025's Target-profile baseline. T051's broader SQLite fixture, schema-drift hooks,
and later full provider conformance remain pending. The green lifecycle rows still traverse the
existing load-all/client-evaluation read path and are not bounded-query evidence; T026–T029 remain
the authority for removing that path.

## 1. Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx -c Release --no-restore
```

Expected: zero build errors. New warnings introduced by the feature are failures; existing accepted advisories must be recorded separately.

## 2. Run focused adapter suites

```bash
dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj -c Release --no-build
```

Expected: registration, serialization, command, atomic rollback, and unified-host scenarios all pass.

### Phase 2 bounded-query substrate evidence (T019)

On 2026-07-20, the canonical in-memory Groundwork test substrate was updated to
validate the selected physical bounded-query declaration before counting provider I/O,
while retaining a legacy-declaration fallback for manifests that have not yet been
physicalized. A physical declaration takes precedence when the transitional manifest
also retains the corresponding legacy declaration, matching provider route binding.
The substrate now rejects undeclared operators, paths, disjunction, ordering, paging,
latest-per-key selection, required residual omissions, unsupported terminal
operations, and physical `Documents` queries without a positive page limit before I/O;
it also observes cancellation before I/O. The legacy-declaration fallback remains limited
to manifests that have not yet been physicalized. The ratified contract requires stable
ordering when the public query shape requests it (for example, latest-version and
catalog routes); it does not add an implicit order requirement to every otherwise
unordered document page.

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj \
  -c Release --no-restore
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  -c Release --no-restore
```

#### Stable-candidate verification

The latest tested source/test candidate is
`d8ba8550aaab5bb88fb2af87aa10c40f1547b72e`. Verification completed at
`2026-07-20T13:11:38+02:00` (CEST, `Europe/Amsterdam`) with these exact results:

- `dotnet test tests/Elsa/Persistence/Groundwork/Querying/Tests/Elsa.Persistence.Groundwork.Querying.Tests.csproj -c Release --no-restore`
  — passed `84/84` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `627/627` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `52/52` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj -c Release --no-restore`
  — passed `48/48` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj -c Release --no-restore`
  — passed `88/88` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj -c Release --no-restore`
  — passed `74/74` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-restore`
  — passed `354/354` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-restore`
  — passed `490/490` (`0` failed, `0` skipped).
- `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore`
  — passed `251/251` (`0` failed, `0` skipped).
- `dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603`
  — succeeded with `0` errors and `41` existing warnings.

The architecture ratchet used the complete repository assets restored earlier in the
work unit. The documentation-only follow-up changes only this quickstart. The commands
above are not rerun on that documentation commit; their evidence remains attached to
the tested source/test candidate SHA stated above.

## 3. Run the shared contracts and four-provider black-box suites

The shared project contains provider-neutral contracts and their behavioral harness:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj -c Release --no-build
```

The implementation phase adds each concrete fixture in a separate leaf test project. Those projects
reference the shared contract project and may reference only their own provider SDK:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/SqlServer.Tests/Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/PostgreSql.Tests/Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Persistence/Groundwork/DesignConformance/MongoDb.Tests/Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests.csproj -c Release --no-build
```

Required provider-specific fixtures:

- SQLite, including close/reopen restart;
- SQL Server container;
- PostgreSQL container;
- MongoDB replica-set/sharded transaction fixture;
- MongoDB standalone negative-readiness fixture.

Expected: identical result hashes and domain outcomes for all public design stores/commands; isolation, OCC, atomic rollback, ambiguous acknowledgement, cancellation, restart, and schema drift scenarios pass.

The provider-specific projects must also boot the actual `src/Apps/Elsa.Server/` reference host for
this composition matrix, rather than proving only isolated fixtures:

| Shape | SQLite | SQL Server | PostgreSQL | MongoDB |
|---|---:|---:|---:|---:|
| Design-only | required | required | required | required |
| Runtime-only | required | required | required | required |
| Combined design + runtime | required | required | required | required |

Design-only and combined execute representative design create/read/update/delete/version flows plus readiness before traffic. Runtime-only proves design features are absent while runtime persistence remains operational. Each cell restarts the host and retains one host-level provider selection.

## 4. Prove bounded execution

Run the scale dataset/query suite with plan capture enabled. Evidence must cover every row in [the bounded query catalog](contracts/design-persistence-contract.md#bounded-query-catalog).

Expected for each provider/query:

- the declared query identity resolves to one certified handler;
- scope and document-kind discrimination are present;
- the intended physical entity table/index is selected;
- selective queries do not scan a shared collection or load all documents into application memory;
- count/any/first/latest operations remain server-side;
- unsupported shapes fail before provider I/O.

Store raw plan artifacts under the benchmark evidence directory defined by issue #646; do not paste provider plan blobs into this spec.

## 5. Validate schema operations

Build the assembly containing the unified `IPhysicalSchemaManifestSource`, then install the exactly matching local Groundwork tool version.

```bash
dotnet tool restore
dotnet groundwork --version
dotnet groundwork validate \
  --manifest-assembly ./src/Apps/Elsa.Server/bin/Release/net10.0/Elsa.Server.dll \
  --manifest-type ElsaGroundworkSchema \
  --provider sqlite \
  --offline \
  --output json
```

Repeat live `plan`, `status`, `validate`, and `apply --safe` for `sqlite`, `sqlserver`, `postgresql`, and `mongodb` using `--connection-env`. Accept exit code `0` or `2` for the plan gate; require `0` after safe application and live validation.

Expected:

- deterministic manifest/route/plan fingerprints;
- resolved names follow host policy then provider normalization;
- projected-field backfills are restart-safe;
- schema drift or unsupported topology yields a blocking diagnostic;
- no command silently mutates on validate/status.

The exact manifest type name is finalized during implementation and this guide must be updated if it differs from `ElsaGroundworkSchema`.

## 6. Capture EF-oracle and physical-form evidence

Using the fixed workload from issue #646, run identical seeds, payloads, query shapes, concurrency, and result hashing against:

1. temporary EF normalized design tables;
2. Groundwork shared documents plus linked indexes;
3. Groundwork dedicated document tables;
4. Groundwork physical entity tables.

The 1K dataset is the required correctness/smoke scale, 100K is the required acceptance scale for every workload and mandatory provider, and 1M is required for every scale-bearing query/form comparison on every mandatory provider. An architect-approved workload exclusion must be recorded before timing; machine capacity does not silently waive a scale.

Run every row in the [Benchmark Acceptance Catalog](contracts/design-persistence-contract.md#benchmark-acceptance-catalog). For each measured case, run three independent processes after one untimed warm-up per process. Each measured process must complete at least 100 operations and 30 seconds of steady-state work. Retain raw per-operation samples, fixed seed, payload hash, result hash, provider/server settings, machine metadata, allocation, round trips/database work, storage, write amplification, migration/backfill cost, and native plans. Compute per-run p50/p95/p99 and throughput, use the median of the three runs for the EF ratio gates, and report 95% bootstrap confidence intervals for form comparisons. Apply gates per catalog row; do not use a workload aggregate to hide a failing operation.

Exit gate:

- correctness before timing;
- p95 `<= 1.25x` EF;
- throughput `>= 80%` EF;
- p99 `<= 2x` EF;
- selected entity forms improve median p95 or median throughput by at least 10% over each other Groundwork form at both 100K and 1M, with the improvement direction present in all three runs and the 95% bootstrap confidence interval excluding zero;
- same-provider EF ratios are required wherever an EF oracle exists; MongoDB records its absolute baseline and must pass correctness, bounded-plan, and form-selection gates without a fabricated EF comparison.

## 7. Run design behavior and architecture suites

```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release --no-build
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-build
```

Expected: all preserved domain-objective tests pass; core-provider boundaries and the design-EF removal ratchet pass.

The exact stable-candidate results for all three commands are recorded in
[Stable-candidate verification](#stable-candidate-verification).

## 8. Audit design EF removal

After the evidence gates pass and the EF design projects are removed:

```bash
rg -n 'EntityFrameworkCore|EFCore|DbContext' \
  src/Elsa/Workflows/Design/Persistence \
  src/Elsa/Activities/Design/Persistence \
  tests/Elsa/Workflows/Design \
  tests/Elsa/Activities/Design

dotnet list src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Core/Elsa.Activities.Design.Persistence.Core.csproj package --include-transitive
dotnet list src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj package --include-transitive
dotnet list src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj package --include-transitive
```

Expected: the search has no design EF implementation/mechanism hits, and none of the four retained projects resolves a `Microsoft.EntityFrameworkCore*` package. Domain documentation may mention the historical migration only where explicitly allowed by the final architecture guard.

## 9. Full verification and handoff

```bash
dotnet test Elsa.Server.slnx -c Release --no-build
git diff --check
```

Refresh the narrowest dependency and extension-point maps, review the generated findings, run an independent FR/SC audit against exact HEAD, and attach:

- test summaries and provider versions;
- schema fingerprints and CLI outputs;
- query-plan evidence index;
- benchmark raw summaries and decision;
- direct/transitive dependency audit;
- final commit and PR/check links.

Issue #641 closes only after the merged `main` state contains the removal and all evidence remains reproducible.
