# Unified Groundwork persistence — provider selection and schema operations

One host-level choice backs every Groundwork-persisted Elsa store family (runtime, secrets,
studio preferences, distributed runtime, workflows design, activities design, design atomic
writes, and publishing). A host selects exactly one provider by enabling one unified shell
feature; feature code never chooses physical providers (Spec 093 FR-017).

## Selecting a provider

| Provider | Shell feature | Registration entry point |
|---|---|---|
| SQLite | `GroundworkUnifiedPersistenceSqlite` | `AddGroundworkSqliteUnifiedPersistence(connectionString, …)` |
| PostgreSQL | `GroundworkUnifiedPersistencePostgreSql` | `AddGroundworkPostgreSqlUnifiedPersistence(connectionString, …)` |
| SQL Server | `GroundworkUnifiedPersistenceSqlServer` | `AddGroundworkSqlServerUnifiedPersistence(connectionString, …)` |
| MongoDB | `GroundworkUnifiedPersistenceMongoDb` | `AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName, …)` |

Exactly one provider leaf may be selected per host: `SelectGroundworkProviderLeaf` rejects a
second, different provider before either leaf can overwrite shared registrations. Repeating the
same provider is idempotent.

### Connection inputs

Connection strings are secret feature settings (`ConnectionString`, plus `DatabaseName` for
MongoDB). Supply them through the host's secret configuration surface or environment; never
commit them or pass them in process-visible command arguments. The schema CLI reads connections
from an environment variable via `--connection-env` for the same reason.

### MongoDB topology

Multi-document design writes require a transaction-capable MongoDB deployment (replica set or
sharded cluster). Runtime admission probes the topology (`hello`/`isWritablePrimary`, replica-set
name, and a snapshot-isolation transaction round trip) before publishing the provider; a
standalone deployment fails readiness before any design write. The base capability report
deliberately withholds the atomic-commit guarantee until deployment evidence is inspected.

## Schema operations (deployment pipelines)

Schema application is an operator/CLI responsibility. Startup admits the exact applied target and
never applies or repairs schema unless the host explicitly opted into safe startup auto-apply
(`AutoApplySchemaOnStartup`).

```bash
dotnet tool restore
dotnet groundwork validate \
  --manifest-assembly <path to the deployment schema assembly> \
  --manifest-type Elsa.Persistence.Groundwork.ReferenceComposition.GroundworkAllFeaturesDeploymentSchema \
  --provider <sqlite|postgresql|sqlserver|mongodb> \
  --offline --output json
dotnet groundwork plan   … --connection-env <ENV_VAR>
dotnet groundwork apply  … --connection-env <ENV_VAR> --safe
dotnet groundwork status … --connection-env <ENV_VAR>
```

Pinned CLI contract (tool `0.0.1-preview.78`; executable evidence in
`UnifiedSchemaToolContractTests`):

- `validate --offline` — exit `0` with deterministic target fingerprints; mutation-free; rejects a
  supplied connection.
- `plan` / `status` — exit `2` with outcome `pending` while operations are outstanding, exit `0`
  with outcome `ready` afterwards; never mutate the target.
- `validate` (live) — exit `0` gating manifest well-formedness while surfacing pending operations;
  readiness is `outcome == "ready"`.
- `apply --safe` — applies exactly the planned safe operations; a repeated apply is idempotent
  (`outcome ready`, `targetMutated=false`). Destructive or semantic changes are refused by the
  safe gate and require exact plan-bound authorization.

## Startup readiness

Each provider's admission initializer (Prepare phase) validates the applied target fingerprint
and pending operations against the composed manifest and fails startup with the exact
incompatibility; schema drift is a blocking diagnostic, never an empty store or a slower path.
`GroundworkSchemaReadinessTask` (Start phase) then verifies an admitted publication with explicit
transaction-boundary evidence exists at all — closing the mis-composition gap where no provider
admission ran — and never applies or falls back.

## Replacement contracts

The design lane's replacement seams (`IDesignAtomicWriter`, `IDraftOriginator`, and the named
store/command contracts) are documented in the workflow and activity design extension-point
catalogs; hosts specialize them with pre-composition registrations or post-composition
`Replace`.

## Known provider divergence

The workflows dashboard run-health/portfolio data sources currently ship SQLite and PostgreSQL
dialects only; SQL Server and MongoDB hosts compose every design/runtime store family but not the
dashboard aggregation lane. Tracked in elsa-workflows/elsa-foundation#932.

## No first-party EF design implementation

Workflow and activity design persistence ships exactly one first-party implementation family:
Groundwork. The temporary EF projects exist only as a behavioral oracle for Spec 093 and are
removed by its final user story; architecture ratchets reject reintroduction.
