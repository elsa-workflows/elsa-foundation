# Contract: Groundwork-Only Reference-Host Matrix

## Purpose

Prove that one host-level provider choice backs every enabled durable Elsa lane without EF fallback or feature omission.

## Mandatory providers

| Provider | Required topology | Dashboard gate |
|---|---|---|
| SQLite | Local durable database | Existing relational dialect |
| SQL Server | Supported server version | #932 SQL Server dialect required |
| PostgreSQL | Supported server version | Existing relational dialect |
| MongoDB | Transaction-capable replica set or sharded topology | #932 aggregation implementation required |

## Mandatory enabled lanes

Each provider composition covers:

- workflow runtime persistence;
- workflow/activity design persistence;
- IAM, secrets, and distributed runtime persistence;
- Structured Logs persistence;
- OpenTelemetry persistence;
- ASP.NET Core Identity stores;
- OpenIddict application, authorization, scope, and token stores;
- dashboard run-health and workflow portfolio sources.

## Composition assertions

For every provider:

1. Exactly one provider family is selected.
2. Every enabled durable contract resolves.
3. Every durable implementation is Groundwork-backed.
4. No EF service, context, migration task, or provider package resolves.
5. Schema validation succeeds before work is served.
6. Missing capability or schema produces an explicit readiness failure.
7. No required feature is silently disabled to make the composition pass.

## Behavioral evidence

Each matrix row links:

- feature-registration/resolution assertions;
- provider conformance results;
- tenancy and privileged/global-scope checks;
- restart/recovery and optimistic-concurrency results;
- diagnostics append/query/retention results;
- Identity/OpenIddict highest-seam results;
- dashboard run-health/portfolio results;
- native plan and schema fingerprint identities;
- accepted performance verdicts.

## Evidence safety

Retain provider/version/topology identifiers, commits, fingerprints, and result digests. Never retain connection values, credentials, tokens, or secrets.

## Intake freeze: current configuration declarations

**Recorded Elsa source head**: `f769b516598eb807c9528e7c2e72085b346603e8` (`origin/main`)

This is an intake record of feature declarations in the maintained composition files. It is
not feature-resolution, schema-readiness, startup, provider-conformance, or performance
evidence. A row remains **Not ready** until the required resolution and readiness evidence
is retained under this contract.

| Composition source | Selected provider feature | Diagnostics persistence | ASP.NET Core Identity persistence | OpenIddict persistence | Dashboard | Current status and blocking reason |
|---|---|---|---|---|---|---|
| `src/Apps/Elsa.Server/shells.json` | `GroundworkUnifiedPersistenceSqlite` | `DiagnosticsGroundworkPersistence` | `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` (EF) | `FoundationIdentityOpenIddict` (currently EF-backed) | `WorkflowsDashboard` declared | **Not ready** — Identity and OpenIddict retain EF; this declaration does not yet prove all-lanes resolution or schema readiness. |
| `src/Apps/Elsa.Server/shells.Production.json` (overlay) | Inherited from the base composition | Inherited from the base composition | `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` (EF) | `FoundationIdentityOpenIddict` (currently EF-backed) | Inherited from the base composition | **Not ready** — the production overlay retains EF selections. Future replacement must preserve its seeded-admin configuration without retaining EF. |
| `src/Apps/Elsa.Server/shells.baseline.json` | `GroundworkUnifiedPersistenceSqlite` | Absent | Absent | Absent | Absent | **Minimal, not an all-lanes reference host** — it is EF-free by scope, but it omits diagnostics, ASP.NET Core Identity persistence, OpenIddict persistence, and dashboard. |
| `docker/compose/elsa-server.shells.json` | `GroundworkUnifiedPersistencePostgreSql` | Absent while diagnostics front-end features are enabled | Absent | Absent | Absent | **Not ready** — the PostgreSQL demo omits mandatory diagnostics persistence, Identity, OpenIddict, and dashboard lanes. Its current explanation of diagnostics omission is not compatible with this final reference-host contract. |
| SQL Server reference composition | No maintained configuration row at this head | — | — | — | — | **Not created or admitted** — requires #932 evidence and the #647 SQL Server integration/readiness gates. |
| MongoDB reference composition | No maintained configuration row at this head | — | — | — | — | **Not created or admitted** — requires #932 evidence and the #647 MongoDB integration/readiness gates. |

The PostgreSQL demo also omits parts of the broader default runtime/design composition. The
omissions above are the mandatory durable lanes that prevent it from serving as a final
reference-host result.

## Target-row admission rule

A future target row must select exactly one existing unified provider feature:
`GroundworkUnifiedPersistenceSqlite`, `GroundworkUnifiedPersistenceSqlServer`,
`GroundworkUnifiedPersistencePostgreSql`, or `GroundworkUnifiedPersistenceMongoDb`; include
`DiagnosticsGroundworkPersistence`, `FoundationIdentityAspNetCoreIdentityGroundwork`, and
`WorkflowsDashboard`; and use the exact OpenIddict Groundwork feature delivered and cataloged
by #643. This contract deliberately does not invent that future OpenIddict feature identifier.

Feature presence alone never makes a row ready or passing. The row must additionally retain
the feature-resolution, no-EF, schema-readiness, provider-conformance, dashboard, and
performance evidence required above; an absent capability or schema must instead produce the
explicit readiness failure required by Composition assertion 6.
