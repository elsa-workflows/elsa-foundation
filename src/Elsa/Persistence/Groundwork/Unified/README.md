# Unified Groundwork persistence — provider selection and schema operations

The unified features are a **convenience preset**: they declare one Groundwork target and bind
every store family to it (runtime, secrets, studio preferences, distributed runtime, workflows
design, activities design, design atomic writes, and publishing). Feature code never chooses
physical providers (Spec 093 FR-017).

A host that wants lanes in **different databases** does not use these. It enables
`GroundworkTargets` plus the provider leaf features, then enables the per-lane persistence
features and names a target on each — see [issue #1156](https://github.com/elsa-workflows/elsa-foundation/issues/1156).
The unified preset remains the right choice when one database is wanted for everything.

## Selecting a provider

| Provider | Shell feature | Registration entry point |
|---|---|---|
| SQLite | `GroundworkUnifiedPersistenceSqlite` | `AddGroundworkSqliteUnifiedPersistence(connectionString, …)` |
| PostgreSQL | `GroundworkUnifiedPersistencePostgreSql` | `AddGroundworkPostgreSqlUnifiedPersistence(connectionString, …)` |
| SQL Server | `GroundworkUnifiedPersistenceSqlServer` | `AddGroundworkSqlServerUnifiedPersistence(connectionString, …)` |
| MongoDB | `GroundworkUnifiedPersistenceMongoDb` | `AddGroundworkMongoDbUnifiedPersistence(connectionString, databaseName, …)` |

A host may declare **several** Groundwork targets, on the same or different providers. What is
rejected is declaring one target name twice against different stores: an exact repeat is
idempotent, a second and different connection under the same name throws rather than being
silently discarded. Each target composes and admits only the lanes bound to it, and derives its
own manifest identity, so two targets never contend for one Groundwork schema-state row.

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

Schema application is normally an operator/CLI responsibility. Startup admits the exact applied target and
never applies or repairs schema unless the host explicitly opted into safe startup auto-apply
(`AutoApplySchemaOnStartup`). For unified deployments, that option covers both safe pending document-schema
operations and missing diagnostic-record streams. It never rewrites drifted stream definitions, applies
destructive operations, or turns runtime session opening into a deployment path.

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

Pinned CLI contract (tool `0.0.1-preview.103`; executable evidence in
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
Unified providers run diagnostic-stream deployment at Prepare order `1`, after document admission at
order `0` and before the diagnostics lifecycle in the Default phase. When auto-apply is disabled, missing
streams retain the existing `GW-DIAG-DEPLOY-001` failure. With auto-apply enabled, missing streams are created
idempotently and incompatible persisted definitions retain `GW-DIAG-DEPLOY-002`.
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
