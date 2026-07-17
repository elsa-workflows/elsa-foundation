# Quickstart: Validate Groundwork Store Hardening

This guide is the implementation/review path for feature 094. A narrow green unit test is not a readiness verdict; use the ordered gates below.

## Prerequisites

- .NET 10 SDK selected by the repository.
- Access to the package feed containing the pinned Groundwork `0.0.1-preview.61` release.
- Docker-compatible container runtime for SQL Server, PostgreSQL, and MongoDB.
- Enough local resources to run MongoDB as a replica set for transaction scenarios.
- `Groundwork.Tool` restored from the repository-local tool manifest at `0.0.1-preview.61`, matching all
  Groundwork packages.

Groundwork PR #88 is the generic version-aware codec boundary in this release. Elsa-specific payload policies,
legacy-stamp parsing, JSON options, and concrete upcasters must remain marker-gated in Elsa provider packages; core modules must not
reference Groundwork.

Do not use a standalone MongoDB instance for scenarios that claim multi-document atomicity.

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

## 4. Run the shared provider matrix

After the conformance project is introduced:

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

## 5. Validate the production-shaped combined host

Run the unified-host tests for each provider leaf:

```bash
dotnet test tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/SqlServer/Tests/Elsa.Persistence.Groundwork.SqlServer.Tests.csproj \
  --configuration Release --no-build

dotnet test tests/Elsa/Persistence/Groundwork/MongoDb/Tests/Elsa.Persistence.Groundwork.MongoDb.Tests.csproj \
  --configuration Release --no-build
```

The bare unified provider matrix selects the six provider-level families: workflow runtime, secrets,
distributed runtime, workflows design, activities design, and workflows publishing. Identity is never
selected implicitly. The same matrix separately selects the Identity deployment-schema variant and explicit
Groundwork Identity feature, proving that all seven selected families share the exact admitted target. SQLite
and PostgreSQL execute the restart-oriented unified-host scenarios; SQL Server proves exact registration and
MongoDB proves the exact admission target. The MongoDB lane requires a writable transaction-capable replica set.

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

Runtime admission calls the provider's read-only `IPhysicalSchemaHistoryInspector`, computes the
same Groundwork diff in memory, and never acquires an application lock or invokes schema apply. It
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
the opt-in SQL Server/PostgreSQL/MongoDB matrix against Groundwork `0.0.1-preview.61` and the current Identity
storage manifest. Retain its provider identity, input/result digests, observable operations, and native route
evidence captured at 100,000 physical records. The accepted `preview.60` / Identity manifest v1.0.4 matrix and
all earlier artifacts are immutable historical provenance, not current pass evidence; the ledger remains
unlinked until fresh exact-head artifacts exist. The committed EF contract baseline is explicitly non-executed;
#646 owns live EF execution, equality, and timing.

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
