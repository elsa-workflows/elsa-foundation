# Secrets Minimal API migration — August 2026

## Recommendation

**Proceed for statically loaded first-party APIs, and revise the dynamic-unload design before claiming parity there.** The complete ten-operation Secrets surface migrated from FastEndpoints to one explicit module-owned Minimal API mapper with zero approved HTTP or consumed OpenAPI differences. The migration preserved granular Foundation authorization, tenant isolation, lifecycle behavior, non-disclosure, and mixed-host coexistence. Materialized routes, JSON execution, feature services, and serializer owners release cleanly; actual ASP.NET OpenAPI generation still retains the collectible API context after all harness-owned references are disposed.

Continue to migrate statically composed modules one by one. Each module must retain its own immutable-before evidence because the compatibility work in Secrets reflects that module's actual FastEndpoints behavior, not a universal translation layer. Treat OpenAPI-enabled dynamically unloadable modules as blocked until #1349 records and validates a mitigation or an explicit lifetime policy.

## Scope and implementation

`SecretsApiFeature` now implements CShells `IWebShellFeature`, registers the module services and permission contributor, and delegates to `SecretsApi.MapSecretsApi(IEndpointRouteBuilder)`. The mapper owns exactly ten routes:

| Capability | Routes | Foundation permission |
|---|---|---|
| Metadata discovery | list, get, descriptors, picker | any of `*`, `secrets:read` |
| Create and metadata update | create, update | any of `*`, `secrets:write` |
| Value rotation | rotate | any of `*`, `secrets:update-value` |
| Lifecycle removal | revoke, delete | any of `*`, `secrets:delete` |
| Provider test | test | any of `*`, `secrets:test` |

The permission catalog owns eight stable Secrets permissions under `Elsa.Secrets.Api`. Only `secrets:write` implies `secrets:read`; wildcard remains an administrative grant rather than a catalog entry. The ten legacy endpoint classes, their transition-exception records, and the production FastEndpoints dependency were removed.

## Evidence matrix

| Gate | Evidence | Result |
|---|---|---|
| HTTP compatibility | 35 immutable FastEndpoints observations across all ten operations plus eight real-binder differential cases for invalid and repeated scalar query values | Zero approved differences after restoring FastEndpoints' 400 response body and content type |
| Consumed OpenAPI | Ten canonical operation projections generated from the real ASP.NET Core OpenAPI document before and after | Zero approved differences |
| Stable capture | Two complete legacy capture runs plus ten route-manifest captures | Byte-identical after reviewed volatile-field normalization |
| Read/discovery behavior | Singular/plural filters, status and active-only behavior, paging, descriptors, picker bounds, deleted exclusion, same-name two-tenant records, and metadata redaction | Passed |
| Lifecycle behavior | Create, update, rotate, revoke, delete, and test across valid, duplicate, missing, malformed, repeated, route-authority, and no-mutation branches | Passed |
| Foundation authorization | Anonymous challenge; exact, implied, wildcard, missing, adjacent, untrusted, ambiguous, resource-denied, and missing-tenant outcomes | Passed |
| Disclosure | API response bodies, headers, ProblemDetails, and OpenAPI response schemas checked against unique sensitive markers; manager-level audit safety remains covered by the existing audit suite | No disclosure found |
| Mixed authoring models | All Secrets Minimal routes plus real secured FastEndpoints authorization, query-binding, and route-binding canaries in one TestServer host | Both authoring models reached the same instrumented Foundation evaluator; invalid/repeated scalar binding and route-name precedence matched |
| Production dependency | Project, assembly-reference, source-discovery, transition-registration, and architecture guards | No production FastEndpoints dependency or registration |
| Collectibility | Repeated collectible production assembly cycles after materialized routing, representative JSON execution, actual ASP.NET OpenAPI document-provider generation, and staged route/service/serializer retention and release | Routes, traffic, services, and serializer owners release; OpenAPI generation retains the context after owner disposal and is reported as a blocker |

The committed before baselines were generated while the legacy endpoint implementation still existed and were reviewed before production replacement. Replacement tests compare against those immutable files; they do not recreate a synthetic FastEndpoints surface.

## Findings

### Feasibility at representative CRUD scope

Minimal APIs can replace FastEndpoints for a materially larger first-party module without changing its route, wire, authorization, tenant, or consumed documentation contract. Explicit `IEndpointRouteBuilder` mapping fits the CShells feature lifecycle and lets the module state its owner, security disposition, request/response metadata, and compatibility behavior without a new Elsa endpoint framework.

Foundation authorization remains the common boundary. Minimal API and FastEndpoints routes encode permission requirements as ASP.NET Core policy metadata and reach the same dynamic provider, implication expansion, normalized-principal checks, wildcard handling, and resource evaluator.

### Compatibility behavior preserved intentionally

The immutable evidence captured legacy quirks that are externally observable: GET query operations with documented request bodies, FastEndpoints' malformed-JSON ProblemDetails wording, metadata visibility differing from runtime usability for expired and revoked secrets, and existing generic host error behavior for selected conflicts and missing lifecycle targets. The replacement preserves these behaviors locally. Correcting them belongs in a separately versioned contract change, not in an endpoint-authoring migration.

### Sensitive data requires an explicit boundary

Secrets demonstrated why framework migration cannot rely on status-code parity alone. Submitted values and configuration keys cross request and provider boundaries but must never enter response contracts, errors, OpenAPI response schemas, or audit evidence. Unique marker tests and route-authoritative tenant/name binding make this security claim bite-proof.

### OpenAPI generation exposes a remaining framework retention boundary

The harness loads the production Secrets API assembly into a collectible context, materializes its routes, executes representative JSON traffic, and invokes ASP.NET Core's real `IOpenApiDocumentProvider` over those endpoints. Materialized routing and JSON traffic release after their owner is disposed, confirming the explicit-`RequestDelegate` mitigation for request-delegate caches. Service and serializer retention stages also behave as expected.

OpenAPI is different: after the document provider and every harness-owned route, service, document, and serializer reference are disposed, the collectible API assembly remains alive through a framework-held reference. The test records that outcome instead of weakening the lifecycle or claiming collection from process-memory observations. This does not block statically loaded Elsa modules, but it does block the stronger claim that an OpenAPI-described dynamically loaded module can currently unload.

## Remaining risks

- ASP.NET OpenAPI generation currently retains the collectible Secrets API context after the document/service owner is disposed. Dynamic shells need a proven metadata boundary, document-generation isolation strategy, or an explicit non-unloadable documentation lifetime.
- Explicit compatibility metadata and result translation are more verbose than greenfield Minimal APIs. This is module-local legacy cost, not evidence for another shared endpoint abstraction.
- The in-memory deterministic canary proves API behavior and state transitions but is not a persistence-provider integration matrix.
- The mixed-host test protects coexistence during migration, but FastEndpoints still has process-global configuration concerns; it remains a transition mechanism rather than the target architecture.
- Framework constitution §2.24 remains provisional. This migration relies on accepted ADR 0068 and the existing CShells `IWebShellFeature` seam.

## Proposed follow-up

1. Use the Secrets route, authorization, disclosure, and lifecycle evidence as required inputs to the migration playbook in #1349.
2. Add an explicit dynamic-OpenAPI retention work item: capture GC-root evidence, test whether non-collectible contract/transformer metadata removes the reference, and define the supported documentation lifetime.
3. Extract only stable test infrastructure and architecture guards; do not extract module-specific legacy translations into a new endpoint framework.
4. Select the next statically loaded migration wave using real FastEndpoints capability usage, then create bounded module issues from the playbook.
5. Track deliberate corrections to legacy OpenAPI or error contracts separately from authoring-framework migrations.

## Traceability

- Program tracker: #1342
- Representative migration: #1348
- Production canary: #1347
- Endpoint and authorization spike: #1329
- ADR: `docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md`
- Specification and tasks: `specs/154-secrets-api-migration/`
