# Data Model: Publishing API Migration Evidence

## Route contract case

- Owner, endpoint identity, HTTP method, route template, operation identity, tag, and permission disposition.
- Request scenario: route/query/header/body/content-type, principal, tenant/resource context, and cancellation state.
- Expected response: status, headers, content type, body bytes or structured JSON, ProblemDetails facets, redirect/Location behavior, and collaborator trace.
- Provenance: pre-migration source, runner identity, capture timestamp category, and fixture hash.

## OpenAPI operation contract

- Stable operation ID and Publishing tag/owner metadata.
- Method/path, parameters, request body, response statuses, headers, content types, schemas, and security requirements.
- Stable contract type identities from Publishing API Core or existing shared Core assemblies.

## Publication transaction

- Source identity: workflow version or activity draft plus tenant and requested slot/policy.
- Concurrency/idempotency: expected publication/revision, review or preflight token, idempotency key, request fingerprint, and lock/lease identity.
- Steps: validation, compilation, persistence, activation, trigger/projection indexing, record/slot transition, and retirement.
- Compensation: prior authority/projection state and rollback outcome.
- Terminal result: created, replayed, conflicted, rejected, canceled, failed, or outcome unknown.

## Publication slot and policy view

- Definition, slot, active/retired publication identity, revision, policy mode, timestamps, and visible projection.
- State transitions preserve compare-and-swap authority and prior serving state on failure.

## Test-run resource

- Workflow version or draft/activity draft identity, test-run ID, idempotency identity, request fingerprint, status, retained source/artifact/scope, created/updated/expiry timestamps, cancellation, and terminal diagnostic.
- Disposal order closes Runtime scope/background resources before deleting projections or releasing the owner.

## Authorization matrix case

- Authentication identities and normalized/trusted classification.
- Required read/manage action and exact/implied/wildcard/unrelated grant relationship.
- Tenant/resource context and inner activity-publication decision.
- Expected 401/403/success outcome plus evaluator and collaborator invocation counts.

## Owner generation

- Load context, owner assembly, feature, mapper, endpoint set, stable contract assembly, source-generated serializer, OpenAPI provider/document, DI provider/scopes, stores, publishers, compilers, authorizers, and test-run/background resources.
- Lifecycle: configure, map, authorize, invoke, serialize/document, remove, dispose/drain, unload, collect.

## Approved difference

- Exact endpoint/case/operation/facet key.
- Exact before and after values, reason, owner, review reference, and optional follow-up.
- Validation rejects duplicate, unused, stale, one-sided, no-op, broad, unknown, or false-valued entries.
