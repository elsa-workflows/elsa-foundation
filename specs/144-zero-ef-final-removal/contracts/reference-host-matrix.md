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
