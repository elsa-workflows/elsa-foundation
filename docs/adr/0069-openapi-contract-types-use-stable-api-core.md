---
status: proposed
date: 2026-08-16
decision_context: First-party REST API Consolidation blocker #1392; framework retention reproduced by spec 165
---

# OpenAPI contract types use stable API Core assemblies

## Context

Elsa modules can be loaded into collectible assembly contexts and replaced without restarting the
host. ASP.NET Core routing releases a retired endpoint implementation once the owning generation is
removed and drained, but API Explorer and the built-in OpenAPI document service retain the endpoint
metadata they use to describe operations and schemas for the host service-provider lifetime.

A framework-only reproduction demonstrates the consequence: after real document generation, a
request or response `Type` from a collectible module remains reachable even after its endpoint data
source and service provider are released. The same implementation collects when request and
response metadata name stable host/shared types. There is no supported generation eviction seam for
the installed framework's operation-context and schema-identifier caches.

Private cache mutation, timed eviction, process garbage collection, hiding operations, and replacing
specific schemas with `object` would make correctness dependent on implementation details or weaken
the public contract. A separate serialized-document contribution model could support arbitrary
third-party contract lifetimes, but would establish a new Elsa-owned OpenAPI framework. Framework
constitution section 2.24 is draft, so that broader pattern is not introduced by this decision.

## Decision

First-party dynamically replaceable REST APIs use three lifetime layers:

1. Public request and response types consumed by API Explorer live in an owner-scoped, stable
   `*.Api.Core` assembly, or in an existing stable Core assembly when that model genuinely belongs
   there.
2. Endpoint mappers, handlers, binders, provider adapters, and source-generated runtime serializer
   contexts remain in the replaceable API implementation assembly.
3. A shared final endpoint convention validates the completed API Explorer-facing metadata and
   rejects references to collectible types, metadata instances, members, delegates, transformers,
   or serializer artifacts before publication. Live serializer contexts, type-info objects, and
   options are never endpoint metadata; runtime serialization consumes them only inside the
   replaceable implementation.

Native ASP.NET Core API Explorer and OpenAPI generation remain authoritative. The validation
convention adds no binding, routing, serialization, authorization, result, or document behavior.
It requires standard Elsa endpoint ownership metadata so a rejection identifies the owner and,
where present, shell and generation.

Hosts that publish a changing `EndpointDataSource` register
`AddDynamicEndpointApiExplorerRefresh()`. This adapts the endpoint source's standard change token to
API Explorer's standard `IActionDescriptorChangeProvider` invalidation seam. Without that bridge,
API Explorer keeps its first endpoint-description collection even after routing publishes another
generation. The bridge adds no Elsa document cache or document format; document requests still use
the built-in provider and observe a complete description collection before or after each atomic
endpoint-source replacement.

Existing public CLR namespaces and JSON contracts remain unchanged when a type moves to an API Core
assembly. The former implementation assembly supplies type forwarders where binary compatibility
requires them. Changing a shared contract assembly requires its normal SemVer treatment and a host
restart; only the implementation assembly is hot-replaceable.

Workflow design and runtime API projections remain in their API subdomains. The `Api.Core` suffix
describes stable public wire-contract ownership; it does not move projections into authored workflow
state or runtime-artifact domains.

For independently authored plugins that cannot share a stable contract lifetime with the host, a
canonical serialized OpenAPI snapshot remains the candidate boundary. It is deferred to a separate
ratified design that must define its schema, validation, merge, security, generation, and caching
contracts.

## Consequences

- First-party modules retain exact native OpenAPI schemas while their implementation generations
  remain collectible.
- Dynamic hosts must register the API Explorer refresh bridge once at the host composition root.
- Unsafe endpoint candidates fail before visibility; CShells transactional publication preserves
  the previous accepted generation.
- REST migration waves must split API-visible DTOs into stable API Core packages before claiming
  combined OpenAPI unloadability.
- Module-owned operation transformers and custom metadata are allowed only when their runtime types
  and captured object graphs have stable lifetime.
- Shared API contracts are deliberately not hot-reloadable. Contract changes use package versioning
  and restart semantics rather than pretending every assembly in a module has one lifetime.
- Third-party unloadable contract publication remains unresolved and cannot be inferred from this
  first-party decision.

## Evidence

- Spec and lifecycle matrix: [`specs/165-unload-safe-openapi`](../../specs/165-unload-safe-openapi/)
- Decision report: [`docs/reports/unload-safe-openapi-boundary-2026-08.md`](../reports/unload-safe-openapi-boundary-2026-08.md)
- Parent authoring decision: [ADR 0068](0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
