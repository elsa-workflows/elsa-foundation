# Quickstart: Validate Groundwork Store Hardening

This guide is the implementation/review path for feature 094. A narrow green unit test is not a readiness verdict; use the ordered gates below.

## Prerequisites

- .NET 10 SDK selected by the repository.
- Access to the package feed containing the pinned Groundwork `0.0.1-preview.88` release.
- Docker-compatible container runtime for SQL Server, PostgreSQL, and MongoDB.
- Enough local resources to run MongoDB as a replica set for transaction scenarios.
- `Groundwork.Tool` restored from the repository-local tool manifest at `0.0.1-preview.88`, matching all
  Groundwork packages.

Groundwork PR #88 is the generic version-aware codec boundary in this release; PR #101 admits sort-only index
fields as bounded residual predicates; and PR #108 adds bounded linked hydration. Elsa-specific payload policies,
legacy-stamp parsing, JSON options, and concrete upcasters must remain marker-gated in Elsa provider packages; core modules must not
reference Groundwork.

Do not use a standalone MongoDB instance for scenarios that claim multi-document atomicity.

The repository's current Groundwork family is `0.0.1-preview.88`; do not combine it with a different
`Groundwork.Tool` or provider package version. PR #88 provides the generic version-aware codec consumed by this
family. Elsa owns only its per-kind policies, legacy-stamp parsing, JSON options, and concrete upcasters behind
the Elsa provider marker.

The original checkpoint/fence attachment and its unversioned evidence paths retain the reviewed
`0.0.1-preview.80` four-provider slice as immutable historical provenance. The later
`0.0.1-preview.81` slice lives under `versions/0.0.1-preview.81/`; the coverage ledger retains that
versioned attachment by tuple as prior-generation provenance. The `0.0.1-preview.86` checkpoint/fence slice
lives under `versions/0.0.1-preview.86/` as immutable prior-generation provenance. The current
`0.0.1-preview.88` slice lives under `versions/0.0.1-preview.88/` and is imported mechanically by tuple. All rows remain below
`evidence-complete`: this narrow 36-record slice cannot close the full provider-evidence gate.

**2026-07-25 preview.88 source alignment**: the seven Groundwork packages and `Groundwork.Tool` consume the
public `0.0.1-preview.88` release built from Groundwork merge
`6e79f7836ac7bb13c0153771531162886ca49971`. Publication ran from exact Elsa source
`b0545e166fd45aa872f265c88782a7034a09c357` (tree
`613afd96195b4ef28546a67f099d259e5ffbe448`) and passed the four-provider publisher gate
(1/1, 2m33s). The importer retained 36 records with attachment SHA-256
`f0b40406e1e5a044bb8e83e6090c3eb84b676124674cd948ed2440f227b065f2`.
Root verification passed the full architecture suite (313), focused publisher tests (9 passed,
four explicit publication tests skipped), benchmark protocol tests (60), and the OpenTelemetry adapter suite (75).
The pre-import audit recomputed all 36 artifact hashes, confirmed the exact 37-file staged set and one
exact-source provenance tuple, and found no connection-value or credential material.

**2026-07-25 preview.86 source alignment**: the seven Groundwork packages and `Groundwork.Tool` consume the
public `0.0.1-preview.86` release built from Groundwork
`fd6d1c1b3cb4ebfce03d4cd57e1420060e8c02ac`. The corrected publication ran from exact Elsa source
`2dc442ea31061971cae6a86a8e8f0a13904cbeb7` (tree
`ae590a5d927e83b9688afa878a02214ed81ee9e9`) and passed the four-provider publisher gate
(1/1, 2m20s). The importer retained 36 records with attachment SHA-256
`954a34a1bb3ce03881bedd167ba87c95d7d58d3f5abdb573e50e123361e0ef24`.
The pre-publication safety gate adds a version-neutral external-staging importer, create-new versioned
publication, exact 36-tuple/provenance/hash validation, four-provider result equivalence, physical
source-scenario execution-path binding, symlink-aware containment, post-capture source revalidation,
crash-window import recovery, immutable preview.80/preview.81 checks, and current-generation-only readiness
evaluation. Before publication, root verification passed the full architecture suite (305), focused publisher
tests (17 passed, two explicit publication tests skipped), benchmark protocol tests (60), and the design
fingerprint gate (1). After publication, an independent read-only audit recomputed all 36 result and artifact
hashes, confirmed nine equivalent four-provider scenario groups and the exact 37-file staged set, and found no
credential material before the mechanical import.

**2026-07-24 historical #646 takeover evidence**: Groundwork PR #126 / Elsa PR #1039 advanced the seven packages to
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

The ordinary conformance run does not mutate checked-in evidence. To publish the reviewed checkpoint/fence
slice, publish first to an external staging directory, then import only that exact 36-record generation:

```bash
export ELSA_GROUNDWORK_EVIDENCE_OUTPUT="$(mktemp -d "${TMPDIR:-/tmp}/elsa-groundwork-evidence.XXXXXX")"
export ELSA_GROUNDWORK_SOURCE_COMMIT="$(git rev-parse HEAD)"
export ELSA_GROUNDWORK_SOURCE_TREE="$(git rev-parse 'HEAD^{tree}')"
export ELSA_GROUNDWORK_RUN_IDENTITY="runtime-checkpoint-fence-preview88"

# The versioned publisher independently rejects a later or dirty checkout and
# rejects output beneath this repository. These checks keep the shell flow
# fail-closed before a provider suite starts.
test -z "$(git status --porcelain)" || { echo "Refusing dirty source publication." >&2; exit 1; }

ELSA_PUBLISH_GROUNDWORK_RUNTIME_EVIDENCE=1 \
dotnet test tests/Elsa/Persistence/Groundwork/Conformance/Tests/Elsa.Persistence.Groundwork.Conformance.Tests.csproj \
  --configuration Release --no-build \
  --filter 'FullyQualifiedName=Elsa.Persistence.Groundwork.Conformance.Tests.RuntimeProviderEvidencePublicationTests.Publish_the_checkpoint_and_fence_provider_evidence_slice'

dotnet run --project tools/groundwork/Elsa.Groundwork.ProviderEvidenceImporter/Elsa.Groundwork.ProviderEvidenceImporter.csproj -- \
  --ledger specs/094-harden-groundwork-stores/coverage-ledger.json \
  --staging-root "$ELSA_GROUNDWORK_EVIDENCE_OUTPUT" \
  --source-repository "$(git rev-parse --show-toplevel)" \
  --provider-version "0.0.1-preview.88" \
  --elsa-commit "$ELSA_GROUNDWORK_SOURCE_COMMIT" \
  --elsa-tree "$ELSA_GROUNDWORK_SOURCE_TREE" \
  --run-identity "$ELSA_GROUNDWORK_RUN_IDENTITY"
```

The version-scoped publisher writes provider artifacts and one deterministic, merge-ready record set beneath
`versions/<Groundwork package version>/` so an older evidence generation remains immutable. Review and
mechanically import those records by
`(coverageEntryId, scenarioId, provider)` with `(coverageEntryId, scenarioId, provider, providerVersion)` as the
generation-retention key; do not hand-author or infer missing obligations. The importer requires exactly the
checkpoint/fence slice's 36 tuple keys, exact source commit/tree/run provenance, digest-verified staged artifacts,
and unchanged preview.80/preview.81/preview.86 history. Publication does
not advance a row status. A row remains incomplete until every declared query, concurrency, failure, and
restart obligation is present for all four providers and the linked #642/#644 authority evidence is current.

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
This refresh preserves T050's historical preview.81 evidence. It does not complete
T058/T069/T076/T093/T100 or advance any ledger row status; #646 inherits the remaining provider publication
and performance-verdict obligations.

### Preview.81 checkpoint/fence review disposition

Three adversarial reviewers inspected the frozen implementation/evidence range
`78033cf1167071123cb9fe5ef38653973bd65200..df7fedbdd531bd889a7bfd72a5d436ee53f8dc8e`
on correctness/mechanism, evidence integrity, and scope/test preservation:

| Axis | Initial finding | Disposition | Final verdict |
|---|---|---|---|
| Correctness/mechanism | Version namespaces and retained metadata were not enforced strongly enough. | The publisher now rejects version/path/provenance mismatches; the validator recomputes result hashes and compares the complete retained payload. Focused publisher/capture tests and an independent 36-record audit passed. | PASS |
| Evidence integrity | Synthetic observations and a separately acquired physical fingerprint could green-wash the retained result. | One driver execution now returns the raw actual observations and physical fingerprint, rechecks the fingerprint, and binds both with provenance into the record, artifact, and digest. All nine four-provider matrices and exact ledger imports were independently recomputed. | PASS |
| Scope/test preservation | The first refresh overwrote preview.80 history, then the runnable command could label a later checkout as the retained source snapshot. | Preview.80 was restored byte-for-byte and guarded by its attachment hash; preview.81 is version-namespaced. The reproduction command now short-circuits before publication unless HEAD, tree, and cleanliness exactly match the retained source snapshot; the originating reviewer exercised later, dirty, and exact-snapshot cases. | PASS |

### Preview.86 source-alignment review disposition

Three adversarial reviewers inspected the initial candidate range
`4a5f517f293b54c370a5d0073ce7424f685bb8c5..a7fd50a7f9e089481932a3d677fcaaa2c2b0b4ad`
and the remediated imported candidate through `dba0901a565144a8322cc398e2aab358117fa0f2`. They assumed the
implementation and evidence were green-washed and reviewed correctness/mechanism, evidence integrity, and
scope/test preservation independently.

| Axis | Confirmed finding | Disposition | Final verdict |
|---|---|---|---|
| Correctness/mechanism | Publication synthesized a coverage-obligation path instead of retaining the provider driver's physical execution path; source identity was checked only before capture; lexical containment admitted symlink escapes; a post-directory-move importer crash could strand a valid generation; and the exact-generation architecture guard rejected later valid current evidence outside the closed slice. | Publication now retains and validates the driver's source-scenario/fingerprint path, rechecks exact HEAD/tree/cleanliness before each versioned write and attachment, uses symlink-aware containment, recovers only a complete exact stranded generation, and filters the permanent attachment guard to the attachment's 36 tuple keys. A regression adds a valid non-slice current record and proves both the generic validator and exact attachment guard accept it. | PASS |
| Evidence integrity | The first preview.86 attachment could be mislabeled because its retained execution path was synthesized rather than returned by the real provider driver. | That generation was deleted. The corrected four-provider publication ran from exact commit `2dc442ea31061971cae6a86a8e8f0a13904cbeb7` / tree `ae590a5d927e83b9688afa878a02214ed81ee9e9`; an independent auditor recomputed all result and artifact hashes, physical paths, provenance, and the exact file set before import. | PASS |
| Scope/test preservation | Current-generation validation originally treated any later valid preview.86 evidence outside the partial checkpoint/fence slice as corruption, and pre-import documentation overstated the candidate state. | Both the production validator and permanent exact-attachment guard validate only the closed 36-tuple slice while the generic validator still validates additional declared evidence. Documentation now calls the import partial, preserves all historical generations, and advances no status or performance verdict. | PASS |

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
the opt-in SQL Server/PostgreSQL/MongoDB matrix against Groundwork `0.0.1-preview.88` and the current Identity
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

After diagnostics checkpoint #1048 and the next stimuli-only main update landed, branch merge
`28b7c7d11dc3d16d18d67960a8559324f6e9e678` integrated exact `origin/main`
`ca2813d91bc5e6189615f58b5c43405e06988130` without rebasing. A forced full restore then passed;
the complete benchmark protocol/integrity/catalog suite passed **16/16**, the real IAM EF and
Groundwork SQLite correctness targets passed **2/2**, and the combined architecture plus shrink-only
EF-surface ratchet selection passed **65/65**. This remains a harness and one-workload correctness
checkpoint only: it publishes no timing, physical-form selection, coverage-ledger verdict, or #646
completion claim.

The frozen review of that candidate rejected five green-looking but non-durable mechanisms:
preexisting artifacts could be re-certified, comparison trusted stored summaries, cross-commit or
machine-mismatched targets could be compared, native-plan evidence was label-only, and matrix
admission did not consult the frozen provider/form contract. The remediated schema-v2 protocol now:

- binds two distinct four-process measurement sets to one cohort, one expected commit, and one exact
  harness-assembly SHA-256 in a hash-complete manifest;
- rejects stale planned paths, partial or mixed cohorts, changed admitted files, and unbound files;
- validates finite positive raw samples and reproduces stored summaries, while comparison and gates
  recompute their statistics from the raw samples;
- requires exact commit equality and stable machine-environment equality across targets;
- enforces the clean/exact repository HEAD and exact harness-assembly hash in both the public process
  runner and the adapter child before preparation, keeps delegate execution and manifest minting
  test-internal, binds every process to an opaque hash of the OS machine identity, and retains provider
  server version, required topology, and material configuration values so adapter setup differences
  remain visible;
- binds structured route evidence to an actual hashed JSON file and its cohort, measurement set,
  workload input, provider, adapter, physical form, scale, commit, host, provider metadata, and
  composition fingerprint, while every `(measurement set, route)` exclusively owns a distinct
  manifest-bound raw provider-plan JSON/text/XML artifact whose digest and secret-safe structured
  content are validated; and
- admits only providers and physical forms named by the immutable workload contract.

Root revalidation passed the expanded harness suite **53/53**, the real IAM EF and Groundwork SQLite
correctness selection **2/2**, the full architecture suite **285/285** (including the architecture
plus shrink-only EF-surface ratchet selection **65/65**), and a zero-warning Release harness build.
Adapter identity is not present in the immutable
Spec 094 workload schema, so exact adapter allowlisting remains explicitly open rather than inferred.
Likewise, route-specific expected limits/cardinalities require a versioned execution profile or
successor workload; this checkpoint validates that the facts are complete, bounded, target-bound,
and file-backed without mutating the frozen workload document. No timing or ledger verdict is
claimed.

The first re-review did not accept generic OS/runtime/architecture/core-count equality as proof that
both targets ran on one host, and the evidence reviewer did not accept a structured plan summary as a
substitute for the raw provider plan or a caller-supplied commit as proof of source provenance. Those
findings drove the clean-HEAD check, opaque OS-machine fingerprint, adapter-observed provider facts,
seed/input binding, per-route raw-plan retention, cross-set raw-plan ownership, and structured
JSON/XML credential rejection described above. Unit fixtures use synthetic
plans only to exercise the protocol; this checkpoint still has no executable timing adapter and
therefore retains or claims no production plan, measurement, or verdict.

### #646 reproducible workload contract-vector checkpoint

The program owner ratified new v1.1.0 successors for the ten non-Identity, non-Secret workloads on
2026-07-24. The benchmark project now owns deterministic contract-vector definitions: parameters,
operation names, expected observations, canonical serialization, and SHA-256 generation. These definitions
are not public-operation runners; future real EF and Groundwork adapter projects must execute them. Run:

```bash
dotnet run --project benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks.csproj \
  -c Release -- workload-vectors
```

The command emits the exact version, seed, input fingerprint, expected result digest, and benchmark-admission
status that `runtime.json` and `distributed-runtime.json` must contain. `WorkloadCatalog` binds every semantic
JSON input field to the code-owned parameter set, checks independent literal goldens for all twelve
then-current workloads,
and rejects drift before matrix admission. The v1.0 values remain immutable history; this checkpoint does not
claim that they were reproducible or executed.

`secret-create-read-list` remains v1.0.0 and Blocked under
`comparator.secret.real-ef-required` until #646 has a real EF Secret repository comparator. No
synthetic comparator or waiver was added. Its explicit blocked admission is enforced before matrix child
launch and again by comparison and gate, including against forged complete artifacts. It publishes no timings,
physical-form selection, performance verdict, or coverage-ledger advancement; T100 remains open.

### Ratified diagnostics workload and additive ledger denominator

On 2026-07-25 the program owner ratified `diagnostics-durable-history` as the thirteenth workload,
split into Structured Logs and OpenTelemetry suboperations. The reviewed contract vector fixes one
seed, every semantic input, the public-operation sequence, and an independent literal result digest.
SQLite retains the same-provider EF diagnostics oracle. SQL Server, PostgreSQL, and MongoDB require
independently reviewed numeric absolute budgets with correctness, native-plan, physical-form,
provider-work, queue-loss, drain, and restart evidence. No numeric budgets or executable
absolute-budget gate exist yet, so the workload fails closed under
`gate.diagnostics.absolute-budget-required`.

The same amendment adds `diagnostics-structured-log-store` and
`diagnostics-open-telemetry-store` as first-class coverage-ledger rows. The original ALL32 floor
remains immutable; the current exact denominator is 34. `runtime-diagnostics-settings` remains a
separate in-memory contract and supplies no evidence for either durable diagnostics row.

This checkpoint passed:

- workload catalog and independent-golden tests: 11/11, with the remediated full harness at 65/65;
- coverage-ledger and performance-handoff architecture tests: 69/69;
- diagnostics feature/composition tests: 11/11; and
- unified reference-host/schema composition tests: 31/31.

After `dotnet restore Elsa.Server.slnx --force-evaluate`, the complete architecture suite passed
306/306, including the restored-solution EF surface ratchet.

The digest-verified `host-selection-all34` artifact records the eight selected feature identities,
including `elsa-diagnostics`. It is composition evidence only. Both diagnostics rows remain
`implemented` with empty provider evidence and no `performanceVerdict`; no row status advanced, the
workload is blocked from matrix/comparison/gate execution, and T100 remains open.

### Diagnostics workload amendment review disposition

Three adversarial read-only reviewers inspected the frozen implementation range
`e5b8d02ed1c2499f75e33d17da409f1bf13601b6..90df018c0338ec1db5e5a4cdc8f2601f2cabe71f`
on 2026-07-25. Each assumed the checkpoint had green-washed its completion claims:

| Axis | Confirmed finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The initial candidate admitted diagnostics without an executable numeric absolute-budget gate, and later reviews found both direct construction of the public `MatrixPlan` and cloning the public workload record with a forged `ready` admission could bypass caller-controlled checks. Diagnostics now remains explicitly Blocked under `gate.diagnostics.absolute-budget-required`; the shared guard consults the code-owned blocked-ID registry before trusting record state, and catalog, request/artifact admission, matrix planning and execution, comparison, and gate evaluation all reject it before child execution. A regression test forges `ready`, constructs the public plan directly, and proves the child is never invoked. | PASS |
| Evidence integrity | The reviewer independently recomputed the workload source, input, and result digests, verified the exact 34-row denominator and composition artifact, and confirmed both diagnostics rows still have empty provider evidence and no verdict. No numeric budget, timing, physical-form selection, or provider result was invented; the checkpoint records only the ratified workload contract and blocked admission. | PASS |
| Scope/test preservation | The amendment is additive to ALL32, preserves immutable historical evidence and the load-bearing EF oracles, and does not claim #646 completion or advance T100. The diagnostics denominator is isolated from the existing #645 scenario catalog, while source-wide conformance requires the additive workload, both rows, and `host-selection-all34`. | PASS |

Root revalidation of the final code candidate passed the complete non-provider benchmark suite
**65/65**. The evidence reviewer independently repeated **65/65** and the workload-catalog selection
**11/11**. A broader architecture rerun was blocked at build time by the unrelated
`IncidentResolutionBatchExecutor.cs` missing `Elsa.Workflows.Primitives` reference outside this
range; the hosted candidate checks remain the integration authority for that pre-existing failure.

### #646 workload-successor review disposition

Three adversarial read-only reviewers inspected the frozen range
`c4b5fa00499e8448ecf48250fc088ba75186556a..01043fb94acceac69bac3d185235d3ec56e204aa`
on 2026-07-24 and rejected it. Their findings and the remediations included in the replacement
candidate are:

| Axis | Required finding and disposition | Candidate status |
|---|---|---|
| Correctness/mechanism | The JSON fingerprints could match while semantic input fields drifted from the code-owned parameters. `WorkloadCatalog` now compares every semantic JSON field with the code-owned parameter set before admitting the workload, and a negative test proves that recomputing the source digest cannot bypass this check. | PASS |
| Evidence integrity | Expected results were derived dynamically from the same implementation under test, so they were not an independent ratchet. All twelve then-current workloads have separately maintained literal golden vectors, duplicated independently in the conformance test, and catalog admission rejects any mismatch. | PASS |
| Scope/test preservation | The checkpoint described definitions as executable workloads even though it adds contract vectors rather than public-operation runners. The CLI and documentation now use `workload-vectors`, explicitly reserve execution for future real adapters, and remove the unsupported T099 evidence claim. | PASS |
| Evidence integrity | Secret's Blocked state existed only in prose, so synthetic or forged artifacts could still enter matrix, comparison, or gate paths. The closed workload schema now carries `benchmarkAdmission`; matrix planning, comparison, and gating reject Secret with `comparator.secret.real-ef-required`, including forged complete inputs. | PASS |
| Correctness/evidence integrity | The first replacement review found that ready IAM still checked only hashes, version, and seed; reviewed source-digest drift could therefore change its semantic inputs, scenario, or operation sequence. IAM admission now binds all six semantic inputs, scenario identity, and exact operation sequence to `IamNormalizedLookupWorkload`; three negative cases recompute the source digest and prove each class of drift fails closed. | PASS |

Root revalidation of the remediated candidate passed the benchmark harness **60/60**, the focused
workload-correctness conformance selection **5/5**, and the performance-handoff architecture
selection **7/7**. GitGuardian identified the Secret workload's literal identifier beside its two
public SHA-256 golden vectors as a generic high-entropy secret; the dictionary now references the
separately declared workload identifier, retaining the exact immutable vectors without placing a
secret-labeled token beside them. The originating reviewers re-inspected exact clean range
`c4b5fa00499e8448ecf48250fc088ba75186556a..d15d64c9b65559cb554b0371c3c8cfd114f0fe2c`
and returned PASS on correctness/mechanism, evidence integrity, and scope/test preservation. Their
independent verification included the **60/60**, **5/5**, and **7/7** selections above, the real
Identity EF/Groundwork SQLite oracle **2/2**, the full architecture/ratchet suite **285/285**, three
recomputed-digest IAM attacks, and a synthetic Secret matrix attempt that failed before child launch
with `comparator.secret.real-ef-required`.

### #646 harness review disposition

Three adversarial read-only reviewers inspected the frozen range
`ca2813d91bc5e6189615f58b5c43405e06988130..688a8066ef59384601e2c574789388c0727a2f5e`
on 2026-07-24. Each assumed the implementation had green-washed its claims:

| Axis | Required finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The originating reviewer rejected CLI-only source validation because direct `MatrixPlan`/delegate use or a different child could bypass it. The public runner and adapter child now validate the clean exact HEAD and executing harness assembly before launch/preparation; the harness hash is bound through request, native-plan evidence, manifest, and comparison admission; delegate execution and manifest minting are test-internal. The originating reviewer re-ran 53/53 and withdrew the blocker. | PASS |
| Evidence integrity | Successive reviews rejected stale artifact reuse, stored-summary trust, label-only plan evidence, caller-only source/host/provider assertions, shared raw plans, unsafe raw plan/configuration content, endpoint/URI leakage, and false rejection of parameterized endpoint placeholders. Schema-v2 freshness, hash/exclusive ownership, observed provenance, safe structured/raw evidence, and raw-metric recomputation now fail closed. The final reviewer passed 53/53 and a warning-as-error Release build. | PASS |
| Scope/test preservation | The checkpoint remains additive: frozen workloads, coverage ledger, EF ratchet, source projects, and the load-bearing Identity EF oracle are unchanged. All existing conformance tests remain, two SQLite correctness tests are additive, and the documentation explicitly claims no production timing, verdict, ledger advancement, or #646 completion. The reviewer independently confirmed 53 harness tests, 2 IAM tests, and 285 architecture tests. | PASS |

After `origin/main` advanced to `3940e74f280107c454a9021b4107c8e084c41bf1`, merge commit
`b68783fe8e1c78a559215bcaf60b90c0e637103c` integrated it without rebasing. All three reviewers
re-inspected exact range `3940e74f280107c454a9021b4107c8e084c41bf1..b68783fe8e1c78a559215bcaf60b90c0e637103c`
and returned PASS: the incoming scheduling scripts and `shells.json` were base-only, the harness
surfaces were byte-identical to the reviewed parent, and the integrated head again passed 53/53,
2/2, 285/285, and the zero-warning Release harness build.

When `origin/main` advanced again to `23300c68cca7840af5c8b7b4ceaa32e4c8c105af`, merge commit
`b5f762cda5e2c0ec2523267173206ceb5f8d986c` integrated the base-only `TestScripts` to `e2e-tests`
rename and agent-guidance changes. The three reviewers re-inspected exact range
`23300c68cca7840af5c8b7b4ceaa32e4c8c105af..b5f762cda5e2c0ec2523267173206ceb5f8d986c`
and returned PASS; root revalidation again passed 53/53, 2/2, 285/285, and the zero-warning Release
harness build.

### Bookmark lookup correctness-runner review disposition

Three adversarial read-only reviewers inspected the initial frozen range
`6751087c613b150f4c435d11230dbde00eade37e..d3c1db47240407e0e21bf00f477dcaea8f41b738`
on 2026-07-29. Each assumed the container-free checkpoint had green-washed its correctness or
completion claims:

| Axis | Confirmed finding and disposition | Verdict |
|---|---|---|
| Correctness/mechanism | The initial adapter accepted independent state-store and stimulus-index objects even though the production runtime requires the index to be implemented by the same store, so discarded writes plus a prerecorded index could pass. It also tested cross-scope isolation only from primary to secondary. At `312bd3cbf18266410e4d3e6fe5dbef100941108e`, each scope accepts one state-store object and derives the index from that same instance, rejects state-only/discarded-save adapters, probes isolation in both directions, and has selective asymmetric-leak tests that preserve local lookup while leaking only the opposite scope. The originating reviewer re-verified both fixes and found no new blocker. | PASS |
| Evidence integrity | The reviewer independently matched workload ID/version/seed, semantic parameters, input fingerprint, result digest, operation sequence, and observations to the frozen Spec 094 source. After remediation, the unchanged zero cross-scope observation is emitted only after both directional probes pass. The PR metadata was corrected to the exact `312bd3cbf18266410e4d3e6fe5dbef100941108e` head with 12/12 tests, a zero-warning Release build, and a clean diff check; the originating reviewer re-verified the record. No provider, EF, timing, physical-form, native-plan, ledger, or verdict evidence is claimed. | PASS |
| Scope/test preservation | The exact source delta remains limited to the provider-neutral runner, its focused tests, and the Runtime Core project reference. The public adapter contains no provider, connection, timing, manifest, ledger, matrix, or physical-form input. The same-instance and bidirectional-isolation fakes model only the confirmed correctness failures and do not advance a performance task. | PASS |

Root verification at the remediated source head passed the focused workload suite **12/12**,
the benchmark project Release build with **0 warnings/errors**, and `git diff --check`. No database
container, provider suite, timing run, physical-form selection, coverage-ledger edit, or performance
verdict was produced.

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
