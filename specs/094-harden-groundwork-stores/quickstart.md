# Quickstart: Validate Groundwork Store Hardening

This guide is the implementation/review path for feature 094. A narrow green unit test is not a readiness verdict; use the ordered gates below.

## Prerequisites

- .NET 10 SDK selected by the repository.
- Access to the package feed containing the pinned Groundwork `0.0.1-preview.81` release.
- Docker-compatible container runtime for SQL Server, PostgreSQL, and MongoDB.
- Enough local resources to run MongoDB as a replica set for transaction scenarios.
- `Groundwork.Tool` restored from the repository-local tool manifest at `0.0.1-preview.81`, matching all
  Groundwork packages.

Groundwork PR #88 is the generic version-aware codec boundary in this release; PR #101 admits sort-only index
fields as bounded residual predicates; and PR #108 adds bounded linked hydration. Elsa-specific payload policies,
legacy-stamp parsing, JSON options, and concrete upcasters must remain marker-gated in Elsa provider packages; core modules must not
reference Groundwork.

Do not use a standalone MongoDB instance for scenarios that claim multi-document atomicity.

The repository's current Groundwork family is `0.0.1-preview.81`; do not combine it with a different
`Groundwork.Tool` or provider package version. PR #88 provides the generic version-aware codec consumed by this
family. Elsa owns only its per-kind policies, legacy-stamp parsing, JSON options, and concrete upcasters behind
the Elsa provider marker.

The original checkpoint/fence attachment and its unversioned evidence paths retain the reviewed
`0.0.1-preview.80` four-provider slice as immutable historical provenance. The current
`0.0.1-preview.81` slice lives under `versions/0.0.1-preview.81/`; the coverage ledger imports that
versioned attachment by tuple. A partial attachment does not advance a row to evidence-complete or close the
package-generation readiness gate.

**2026-07-24 #646 takeover evidence**: Groundwork PR #126 / Elsa PR #1039 advanced the seven packages to
`0.0.1-preview.81` for batched schema apply. Elsa PR #1040 aligned the tool manifest and current-version
ratchets; the versioned checkpoint/fence publication below then refreshed the tuple-keyed evidence without
rewriting the `.80` attachment or artifacts.

## 1. Inspect the frozen denominator

```bash
jq '.baselineRef, (.entries | length)' \
  specs/094-harden-groundwork-stores/coverage-ledger.json
```

Review [`contracts/coverage-ledger.md`](contracts/coverage-ledger.md). Every implementation PR must list the row identities it advances. A missing/memory-only row is expected to fail readiness, not disappear.

## 2. Restore and build

```bash
dotnet restore Elsa.Server.slnx
dotnet build Elsa.Server.slnx --configuration Release --no-restore
```

Confirm `Groundwork.Core`, `Groundwork.Documents`, every selected provider, and `Groundwork.Tool` resolve to one binary-compatible version.

## 3. Run unit and architecture gates

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build
```

If a listed project is renamed during implementation, update this quickstart in the same commit. Required architecture results include:

- no Groundwork reference from core modules;
- no growth in the EF surface baseline;
- complete ledger/registration/manifest reconciliation;
- no ready row missing provider, route, restart, failure, or #646 evidence;
- feature-registration and direct implementation branch coverage;
- scoped lifetimes for logic-bearing persistence services, with every non-scoped exception documented and tested;
- no tenant/access context or mutable operation state shared across independently created request scopes.

### Storage-scope gate

The provider-neutral default is one scoped `PersistenceAccessContext` using the nonblank scope `default`.
Multi-tenant hosts replace `IPersistenceAccessContextAccessor` with their own scoped selector before resolving
any store. Ordinary scoped, ordinary global, privileged scoped, privileged global, and privileged across-scope
access are distinct immutable values; privileged access always has a named purpose.

Run the direct scope/session evidence:

```bash
dotnet test tests/Elsa/Persistence/Core/Tests/Elsa.Persistence.Core.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~GroundworkStoreSession|FullyQualifiedName~GroundworkPrivilegedAccessRecorder'

dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~StorageScopeContractTests'
```

Provider startup may retain only immutable admitted resources. SQLite, SQL Server, and PostgreSQL construct a
fresh access-bound runtime per session. MongoDB retains one validate-only admitted handle and derives fresh
access-bound stores from it without reopening a client or repeating topology/schema admission. Explicit units
of work retain one session until commit/rollback/disposal; all other adapter operations acquire and release one
session. Singleton actors and recurring pumps must open a fresh DI scope per command or tick before resolving
logic-bearing persistence consumers.

### Document scope and admitted capability gate

Runtime, Secrets, and Distributed Runtime storage units are scoped. Identity is scoped except for the deliberately
global `identityGlobalProviderConfiguration` unit; global storage still requires the matching global access and
does not convey privileged write authority. Exact current kinds, physical forms, and manifest owners are catalogued
in the four Groundwork extension-point documents; do not reconstruct a scope from document JSON.

`GroundworkProviderCapabilityAdmission` publishes a capability snapshot only after the selected physical schema
has been admitted. The Groundwork distributed leaf advertises `LeaseFencing` only when that snapshot proves
`AtomicCommit` for `checkpointCommit` / `runtime-checkpoint-commit` and the observed
`multi-document-transactions` topology. The process-local distributed fallback reports no lease fencing.
Run the focused admission evidence in addition to the general conformance suite:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~ProviderCapabilityContractTests'

dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName~DistributedWorkflowExecutionActorProviderTests'
```

## 4. Run the shared provider matrix

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build
```

The single suite must execute file-backed SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB. Inspect the test artifacts for:

- exact provider/topology/package and composition fingerprints;
- independent-client concurrency results;
- disposal/reopen and process-restart results;
- failure-window outcomes;
- provider-native bounded-route evidence;
- matching provider-independent result digests.

Provider-specific expected domain results are a test defect.

### Publish reviewed provider evidence

The ordinary conformance run does not mutate checked-in evidence. To publish the exact executable slices
currently owned by #645, set an explicit output root and run each publisher separately:

```bash
export ELSA_GROUNDWORK_EVIDENCE_OUTPUT="$PWD/specs/094-harden-groundwork-stores"

ELSA_PUBLISH_GROUNDWORK_RUNTIME_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.RuntimeProviderEvidencePublicationTests.Publish_the_catalog_validated_runtime_provider_evidence_slice'

export ELSA_GROUNDWORK_SOURCE_COMMIT="bf452355867c8f76a11d9bca9191563a773a631a"
export ELSA_GROUNDWORK_SOURCE_TREE="8b3504d52cef5f4a19ae5318fc66f46aefcfd048"
export ELSA_GROUNDWORK_RUN_IDENTITY="runtime-checkpoint-fence-preview81"

# Reproduction is only valid from the retained source snapshot. These checks
# are chained to the publisher so a later or dirty checkout cannot publish.
test "$(git rev-parse HEAD)" = "$ELSA_GROUNDWORK_SOURCE_COMMIT" &&
test "$(git rev-parse 'HEAD^{tree}')" = "$ELSA_GROUNDWORK_SOURCE_TREE" &&
test -z "$(git status --porcelain)" &&
ELSA_PUBLISH_GROUNDWORK_RUNTIME_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.RuntimeProviderEvidencePublicationTests.Publish_the_checkpoint_and_fence_provider_evidence_slice'

ELSA_PUBLISH_GROUNDWORK_IAM_SECRETS_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.IamSecretsProviderEvidencePublicationTests.Publish_the_real_B6_provider_configuration_and_secret_matrices'

ELSA_PUBLISH_GROUNDWORK_DISTRIBUTED_PLACEMENT_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.DistributedProviderEvidencePublicationTests.Publish_the_real_distributed_ordinary_round_trip_matrices'
```

Each successful publisher writes provider artifacts and one deterministic, merge-ready record set. Legacy
publishers use `evidence/` and `ledger-attachments/`; a version-scoped refresh writes both beneath
`versions/<Groundwork package version>/` so an older evidence generation remains immutable. Review and
mechanically import those records by
`(coverageEntryId, scenarioId, provider)`; do not hand-author or infer missing obligations. Publication does
not advance a row status. A row remains incomplete until every declared query, concurrency, failure, and
restart obligation is present for all four providers and the linked #644/#660 authority evidence is current.

### Preview.81 checkpoint/fence evidence refresh (2026-07-24)

Against Elsa source commit `bf452355867c8f76a11d9bca9191563a773a631a` (tree
`8b3504d52cef5f4a19ae5318fc66f46aefcfd048`) and Groundwork `0.0.1-preview.81`, the checkpoint/fence
publisher passed for SQLite, SQL Server, PostgreSQL, and MongoDB (1/1, 1m58s). It produced 36 unique
`(coverageEntryId, scenarioId, provider)` records across `runtime-checkpoint-commit`,
`runtime-execution-liveness`, and `runtime-post-commit-outbox`; every retained artifact digest was
recomputed successfully before the attachment was imported mechanically by that tuple. Each artifact is
version-namespaced and retains the raw scenario observations plus the exact commit, tree, and run identity.
The observations and compiled physical-target fingerprint now come from the same driver execution, and each
record's result hash plus exact artifact-payload comparison binds those observations and provenance.
The original `.80` attachment hash and all 36 referenced historical artifact hashes are guarded separately.

An earlier full publication reached SQL Server and exposed that the fixture's reset query could select
system sessions, which SQL Server refuses to kill. The fixture now limits the reset set to user processes;
the focused SQL Server fence scenario and this complete publication both passed after the correction.
This refresh preserves T050's completed evidence at the current package family. It does not complete
T058/T069/T076/T093/T100 or advance any ledger row status; #646 inherits those remaining evidence and
performance-verdict obligations.

### Preview.81 checkpoint/fence review disposition

Three adversarial reviewers inspected the frozen implementation/evidence range
`78033cf1167071123cb9fe5ef38653973bd65200..df7fedbdd531bd889a7bfd72a5d436ee53f8dc8e`
on correctness/mechanism, evidence integrity, and scope/test preservation:

| Axis | Initial finding | Disposition | Final verdict |
|---|---|---|---|
| Correctness/mechanism | Version namespaces and retained metadata were not enforced strongly enough. | The publisher now rejects version/path/provenance mismatches; the validator recomputes result hashes and compares the complete retained payload. Focused publisher/capture tests and an independent 36-record audit passed. | PASS |
| Evidence integrity | Synthetic observations and a separately acquired physical fingerprint could green-wash the retained result. | One driver execution now returns the raw actual observations and physical fingerprint, rechecks the fingerprint, and binds both with provenance into the record, artifact, and digest. All nine four-provider matrices and exact ledger imports were independently recomputed. | PASS |
| Scope/test preservation | The first refresh overwrote preview.80 history, then the runnable command could label a later checkout as the retained source snapshot. | Preview.80 was restored byte-for-byte and guarded by its attachment hash; preview.81 is version-namespaced. The reproduction command now short-circuits before publication unless HEAD, tree, and cleanliness exactly match the retained source snapshot; the originating reviewer exercised later, dirty, and exact-snapshot cases. | PASS |

For the performance handoff, `requiredNativeRoutes` in the versioned workload documents are exact current
`BoundedQueryDeclaration.Identity` values, not coverage-ledger `queryShapes` or descriptive “bounded” aliases.
For example the current workload set uses `list-claimable`, `list-due`,
`list-owned-live-placements`, and `lease-visible-commands-by-execution`; checkpoint commit has no bounded-query
route because its evidence is admitted atomic-path/topology evidence. The complete, canonical mapping is in
[`workloads/`](workloads/) and its interpretation is in
[`contracts/performance-handoff.md`](contracts/performance-handoff.md).

## 5. Validate the production-shaped combined host

Run the unified-host tests for each provider leaf:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj \
  --configuration Release --no-build
```

The bare unified provider matrix selects the six provider-level families: workflow runtime, secrets,
distributed runtime, workflows design, activities design, and workflows publishing. Identity is never
selected implicitly. The same matrix separately selects the Identity deployment-schema variant and explicit
Groundwork Identity feature, proving that all seven selected families share the exact admitted target. The
SQLite, PostgreSQL, SQL Server, and MongoDB provider leaves are all production-shaped host paths and must remain
in the solution/test gate; SQL Server and PostgreSQL require their real container hosts, and MongoDB requires a
writable transaction-capable replica set. Do not substitute a provider's package-registration assertion for its
provider/topology host evidence.

Also run invalid compositions: missing source, duplicate unit, unsupported route/capability, wrong MongoDB topology, and scope-policy conflict. Each must fail before serving work with a stable owner-aware diagnostic.

## 6. Exercise schema tooling

Build the concrete host schema-source assembly, then set the connection value only through an environment variable. The shipped unified leaves register
`GroundworkAllFeaturesDeploymentSchema` as their six-family runtime authority; hosts that explicitly select
Groundwork Identity use `GroundworkAllFeaturesWithIdentityDeploymentSchema` instead. Both types live in
`Elsa.Persistence.Groundwork.ReferenceComposition.dll`:

```bash
dotnet groundwork validate \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork plan \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork status \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --output json

dotnet groundwork apply \
  --manifest-assembly <path>/Elsa.Persistence.Groundwork.ReferenceComposition.dll \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <provider> \
  --connection-env GROUNDWORK_DEPLOYMENT_CONNECTION \
  --safe \
  --output json
```

The source passed to `--manifest-type` must be public, parameterless, and implement
`IPhysicalSchemaManifestSource`. Custom hosts derive a concrete source from
`GroundworkDeploymentSchemaManifestSource`, select their exact feature manifest-source types, override
`CreateStorageNamingPolicy` when names are transformed, and register that same source through
`AddGroundworkStorageComposition<TDeploymentSource>()`. The source type's built assembly is then passed
to every CLI command. Its parameterless construction must be deterministic and configuration-complete:
all manifest and host-naming inputs must be encoded by the source type so runtime and the separate CLI
process reconstruct the same policy definition and resolved target without shared in-memory state. The
shipped unified leaves do this with
`GroundworkAllFeaturesDeploymentSchema`; do not point the CLI at the constructor-bound runtime snapshot
type or at a test fixture. Groundwork's physical target fingerprint remains the value
reported by the CLI and compared by runtime admission. Elsa's separate composition fingerprint also
includes the selected feature sources, their manifest versions, durable requirements, topology
evidence, and naming-policy identity; do not substitute it for the physical target fingerprint.

Use provider values `sqlite`, `postgresql`, `sqlserver`, or `mongodb`. For MongoDB, pass
`--database <database-name>` whenever the URI supplied through `--connection-env` does not contain a
database path; for example, a replica-set URI such as `mongodb://host1,host2/?replicaSet=rs0`
requires `--database elsa`. The runtime host's configured database name and the CLI database must be
identical.

### Exact command outcomes

| Command | Exit | Outcome | Meaning and mutation contract |
|---|---:|---|---|
| `validate --offline` | `0` | `ready` | Manifest, naming, and routes compile; no connection or provider inspection occurs. |
| `validate` | `0` | `ready` | Live applied history and physical objects are compatible. The report can still list pending operations; validation never applies them. |
| `validate` | `3` | `blocked` | Compilation, history, or live physical-state validation failed. |
| `plan` / `status` | `0` | `ready` | The exact target has no pending operations. |
| `plan` / `status` | `2` | `pending` | One or more operations are pending; retain the reported plan fingerprint for review/apply policy. |
| `plan` / `status` | `3` | `blocked` | The diff is not applicable under the greenfield/additive policy. |
| `apply` | `0` | `applied` or `ready` | The authorized plan was applied, or the target already matched. |
| `apply` | `3` | `blocked` | Validation or application planning rejected the target. |
| `apply` | `4` | `authorization-required` | The exact plan needs additional safe/destructive/semantic authorization; no target state was published. |
| Any command | `5` | `invalid` | Invocation, source loading, provider selection, or connection input is invalid. |
| Any command | `10` | `failed` | Execution failed; exception details are suppressed from output. |
| Any command | `130` | `cancelled` | Cancellation was observed and unapplied target state was not recorded. |

Exit `2` is an expected result for a plan/status deployment gate, not a tool failure. Deployment
apply requires exit `0`. Destructive or semantic operations require the exact retained plan
fingerprint and exact operation approvals.

This Elsa composition's runtime admission calls the provider's read-only `IPhysicalSchemaHistoryInspector`,
computes the same Groundwork diff in memory, and never acquires an application lock or invokes schema apply. It
blocks startup with `ELSA-GW-SCHEMA-PENDING` when an applicable plan remains and with
`ELSA-GW-SCHEMA-DRIFT` when durable history or live physical state is incompatible. Operators then
run and review the CLI workflow above; runtime never silently repairs or applies schema.

## 7. Supply and consume #646 evidence

For each workload in [`contracts/performance-handoff.md`](contracts/performance-handoff.md):

1. run the correctness baseline and retain its digest;
2. hand the versioned workload definition to #646;
3. link the reproducible raw/report artifact and verdict into mapped ledger rows;
4. remediate every Redesign outcome and rerun;
5. leave Blocked or missing verdicts incomplete.

Do not time setup, schema application, or a workload whose correctness/provider gate is failing.
For `iam-normalized-lookup-update`, run the real physical Groundwork correctness path with mandatory SQLite and
the opt-in SQL Server/PostgreSQL/MongoDB matrix against Groundwork `0.0.1-preview.81` and the current Identity
storage manifest. Retain its provider identity, input/result digests, observable operations, and native route
evidence captured at 100,000 physical records. The accepted `preview.76`/`preview.77` artifacts, the earlier `preview.60` /
Identity manifest v1.0.4 matrix, and all older artifacts are immutable historical provenance, not current pass
evidence; the ledger remains unlinked until fresh exact-head artifacts exist. The committed EF contract baseline is explicitly non-executed;
#646 owns live EF execution, equality, and timing.

### #646 IAM adapter checkpoint

The #646 harness now owns one provider-neutral `iam-normalized-lookup-update` scenario and executes it
through the real ASP.NET Core Identity store contracts. On 2026-07-24, the timing-free SQLite
checkpoint ran the same fixed seed and observations through:

- `ApplicationIdentityDbContext` registered with `AddEntityFrameworkStores`;
- `GroundworkIdentityUserStore` and `GroundworkIdentityRoleStore` over the production SQLite driver.

Both targets reproduced input fingerprint
`5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9` and result digest
`32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc`. The focused workload and
SQLite correctness selection passed 11/11. This checkpoint does not authorize timing: EF native-plan
capture, the executable matrix target, and the remaining workload adapters are still open, so no
performance verdict or coverage-ledger row advances.

Hosted CI initially rejected the checkpoint because the benchmark test project did not follow the
domain-tree path convention and the EF comparator had been placed in the EF-free Groundwork
conformance project. The remediation moved protocol tests to
`tests/Elsa/Groundwork/StorePerformance/Benchmarks/Tests`, moved the EF-vs-Groundwork SQLite
correctness case into the already-EF-owning Foundation Identity test project, and removed the new EF
edge from Groundwork conformance. After a forced full restore, the benchmark suite passed 16/16, the
two SQLite correctness targets passed 2/2, and the project-path, Groundwork EF-free boundary, and
shrink-only EF-surface guards passed 3/3. The baseline did not expand.

## 8. Readiness audit

Before a lane is declared ready:

- compare every ledger row with the exact branch HEAD;
- verify every mandatory provider uses an active production path;
- verify wrong-scope, privileged, concurrency, failure, and restart evidence;
- verify every scale-bearing query is bounded at the provider;
- verify #644/#660 ownership boundaries and #646 verdicts;
- rerun the EF-surface and core-dependency ratchets;
- verify all existing behavioral test objectives remain covered;
- update extension-point catalogs, program-goal/decision-map state, generated maps, and issue links.

Only then may the row become `ready`. Final EF deletion remains a later program gate across all persistence lanes.
