# Evidence and Lifetime Data Model

This work changes no Activities Design domain entity or persistence schema. The entities below describe the
migration evidence and unload boundary.

## Route Contract Case

- Stable logical endpoint name and owner
- HTTP method and normalized route template
- Public/protected disposition and one catalog action
- Request headers, query, route values, content type, and body bytes
- Expected status, headers, content type, and body bytes
- Case kind: anonymous, denied, success, binding failure, domain failure, cancellation, or concurrency
- Historical source/runner/fixture identity

**Invariant**: every one of the 38 registrations has an anonymous case and at least one meaningful authenticated
case; every endpoint family includes success, binding, and domain-failure coverage.

## API Description Operation

- Stable operation ID and Activities Design tag/owner metadata
- Method, path, parameters, request body, response statuses, schemas, headers, and security requirements
- Before/after document identity and consumed comparison facets

**Invariant**: all 38 operations are consumed; a generated-but-uncompared field is not evidence.

## Approved Difference

- Unique route/case/facet key
- Exact before and after values
- Reason, owner, review disposition, and follow-up if temporary
- Consumption state in both real artifacts

**Invariant**: approvals are two-sided, non-no-op, unique, recognized, value-correct, and fully consumed.

## Authorization Matrix Case

- Authentication presence, trusted authentication type, normalized principal state, and ambiguity state
- Exact/implied/wildcard/missing grant relationship
- Tenant and resource/provider key
- Route catalog action and optional inner provider-payload/authoring action
- Expected HTTP status and evaluator/resource/provider invocation counts

**Invariant**: anonymous/untrusted fail closed, trusted missing action denies, and no grant bypasses tenant/resource
or provider-payload checks.

## Stable API Contract Type

- Existing public namespace and full type name
- Request/response/ProblemDetails role
- Stable API Core assembly ownership
- Former implementation assembly forwarding requirement
- Source-generated JSON and OpenAPI metadata coverage

**Invariant**: no API-visible contract or metadata object is owned by the collectible implementation assembly.

## Owner Generation

- Collectible implementation load context and assembly/type weak references
- Mapped endpoints/delegates and ownership metadata
- Authentication/authorization services and configured replacement contracts
- Provider/store/adapter instances and scopes
- Source-generated serializer context/type metadata
- Native API description/OpenAPI document services
- Disposal, endpoint-removal, drain, and unload state

**State transitions**: load → configure → map → authorize/invoke → describe/serialize → drain/remove → dispose →
unload → collected.

**Invariant**: after removal/disposal/unload, every implementation-generation weak reference becomes dead within
the established bounded collection loop; stable API Core types intentionally remain host-owned.

## Capture Receipt

- Baseline source commit/tree and parent relationship to migration
- Archived runner script/program and dependency blob/tree identities
- Capture command and environment inputs
- Route manifest, HTTP fixture, OpenAPI fixture, approval fixture, and hashes
- Case and operation counts

**Invariant**: a clean checkout can reproduce the committed fixtures byte-for-byte without resolving a
squash-lost or local-only Git object and without executing uncommitted runner content.
