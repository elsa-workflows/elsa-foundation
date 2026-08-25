---
status: accepted
date: 2026-08-15
decision_context: Endpoint framework spike #1329, report PR #1338, and program slice #1343 approved by Sipke
---

# First-party REST APIs use ASP.NET Core Minimal APIs

> **Shared-layer bound narrowed by [ADR 0071](0071-first-party-rest-apis-use-endpoint-classes.md)**
> (2026-08-25). Read after 0071, the prohibition below is on process-global discovery, static
> registries, and a parallel endpoint DSL — not on request and handler bases as such. This ADR's
> scope, security disposition, authorization ownership, HTTP and OpenAPI compatibility
> requirements, and FastEndpoints retirement criteria all remain in force.

## Context

Elsa Foundation currently exposes first-party HTTP APIs through several authoring and publication
models. The August 2026 endpoint-framework spike inventoried 178 static FastEndpoints route
registrations, existing root Minimal API routes, and an unbounded set of workflow-authored dynamic
HTTP routes. The root CShells management surface already uses Minimal APIs successfully, while Elsa
module APIs predominantly use `Elsa.Api.FastEndpoints` and process-global endpoint discovery.

That split also exposes two permission paths. Existing Elsa FastEndpoints bases perform direct
permission-claim matching, while Foundation Identity provides ASP.NET Core policies, permission
catalog contributions, implication expansion, replaceable evaluators, normalized claims, and
resource handlers. Endpoint authoring should declare access requirements without owning or
bypassing those semantics.

The spike proved that Minimal API and FastEndpoints routes can coexist in one host and can use the
same Foundation Identity evaluator. It also observed that all three collectible assembly contexts
mapped through FastEndpoints remained rooted after host disposal. The spike did not prove parity
for every current endpoint, so migration needs contract evidence and independently deployable
waves rather than a big-bang replacement.

This decision governs endpoint authoring and authorization composition. It does not change the
HTTP/JSON protocol, public route contracts, domain behavior, or the server-side host-management
credential boundary.

## Decision

### Scope and target

ASP.NET Core Minimal APIs are the normative authoring model for all first-party REST APIs owned by
Elsa modules, CShells/Nuplane management components, and application roots.

Existing FastEndpoints routes may coexist during bounded migration waves. Coexistence keeps hosts
deployable while modules move; it is not a permanent choice between two equal first-party
authoring models.

This target does not reopen REST versus JSON-RPC or gRPC. Existing public HTTP/JSON contracts remain
in force unless a separate, explicitly approved contract change replaces them.

### Explicit module mapping

Each first-party module that owns static REST endpoints exposes an explicit mapping entry point that
accepts `IEndpointRouteBuilder`, conventionally named `Map<Module>Api`:

```csharp
public static void Map<Module>Api(IEndpointRouteBuilder endpoints);
```

The contract is the explicit composition call and the standard ASP.NET Core route builder, not
extension-method syntax or the exact method return type. A mapper may be a module-owned static method
and may return a standard ASP.NET Core convention builder when useful, but it must not require an
Elsa-specific endpoint builder. Route groups, handlers, filters, binding, results, and metadata
remain ordinary ASP.NET Core constructs.

Process-global assembly discovery is not part of the target module contract. Module activation or
an existing feature-composition hook calls each mapper explicitly, making endpoint ownership and
lifecycle visible at the composition boundary.

Every mapped endpoint carries immutable
[route ownership metadata](../glossary/elsa.md). Its stable conceptual fields are:

- `OwnerKind`: `Host`, `Module`, or `DynamicShell`;
- `OwnerId`: the stable owning module or feature identifier; and
- `ShellId` and `Generation`: required for `DynamicShell`, absent otherwise.

The CLR implementation may split these fields across typed metadata records, but all fields must be
available through the standard endpoint `Metadata` collection before publication. Display names and
route paths are diagnostics, not ownership identities.

### Security disposition and authorization ownership

Every first-party endpoint has one explicit primary
[endpoint security disposition](../glossary/elsa.md):

1. **Permission protected.** Attach standard ASP.NET Core authorization metadata for a Foundation
   Identity policy. The endpoint-owning module contributes its permission definitions; Foundation
   Identity owns policy resolution, implication, wildcard compatibility, normalized claims,
   replaceable evaluation, and resource handlers.
2. **Intentionally public.** Attach anonymous-access metadata plus typed public-disposition metadata
   that records a reason or category. `AllowAnonymous` alone is not sufficient evidence for the
   endpoint inventory.
3. **Host credential protected.** Attach typed host-credential disposition metadata and use the
   host-control authentication mechanism. A host-management key is not a user permission.
4. **Host policy protected.** For workflow-authored or integration routes whose established access
   model is ASP.NET Core authorization rather than a Foundation permission, attach standard
   authorization metadata plus typed host-policy disposition metadata naming its owner. The
   disposition carries policy names when the endpoint selects named policies. An empty policy set
   faithfully records the compatibility case where the endpoint requires an authenticated principal
   through the host's default policy; implementations must not invent a policy name for that case.
   Ordinary Elsa module permissions must not use this classification to bypass Foundation Identity.

The stable disposition metadata exposes `Kind`, `OwnerId`, and the policy, permission, or credential
reference used for enforcement. A host-policy reference is optional only for the authenticated-
principal/default-policy compatibility case. Public dispositions instead require `Category` and a
non-empty `Reason`. Typed specializations may represent the four cases, but they must remain
inspectable as one closed conceptual contract.

The minimum metadata by surface is:

| Surface | Required metadata |
|---|---|
| Ordinary module REST | Standard route/method metadata, module owner, and Foundation permission disposition |
| Intentionally public | Owner, public category/reason, and standard anonymous-access metadata |
| Host control | Host owner and credential-kind disposition |
| Streaming | Owner and security disposition plus standard response content-type/OpenAPI metadata; framing and lifecycle remain contract-test obligations |
| Workflow-authored dynamic HTTP | Dynamic-shell owner with shell/generation and an explicit public, permission, host-credential, or host-policy disposition |

Authentication establishes a normalized principal before authorization. Provider-specific claim
mapping stays outside endpoint mappings. Endpoint mappings declare policy requirements; they do not
perform permission claim matching or hide permission rules in path-specific middleware.

Permission-protected Minimal API and transitional FastEndpoints routes must ultimately use the same
Foundation Identity policy provider and evaluator. The exact single/any/all permission extensions,
wildcard compatibility, and resource-denial semantics are owned by
[#1344](https://github.com/elsa-workflows/elsa-foundation/issues/1344); this ADR fixes the ownership
boundary without duplicating that contract.

### Bounded shared endpoint conventions

The shared Elsa endpoint layer may contain only conventions that must remain consistent across
endpoint owners:

- framework-neutral permission and [security-disposition](../glossary/elsa.md) metadata/extensions;
- [route ownership metadata](../glossary/elsa.md) needed for inventory, collision diagnostics, and
  dynamic lifecycle;
- thin ProblemDetails, validation, and OpenAPI conventions proven necessary by at least three
  consumers, consistent with framework constitution sections 2.8 and 2.17; and
- architecture and compatibility guards for those contracts.

ASP.NET Core remains responsible for routing, binding, serialization, filters, results, CORS, rate
limiting, OpenAPI execution, and policy execution. The shared layer must not recreate FastEndpoints
behind Elsa-owned request bases, handler bases, discovery, or a parallel endpoint DSL. Prefer a small
amount of module-local repetition over a shared abstraction that merely renames an ASP.NET Core
primitive.

### HTTP and OpenAPI compatibility

A framework migration is not a public API redesign. Before a module moves, tests pin the externally
visible behavior its consumers rely on, including:

- route templates and HTTP methods;
- route, query, header, and body binding;
- JSON names, shapes, and serialization behavior;
- success and failure status codes;
- ProblemDetails types, extensions, and not-found/conflict/validation behavior;
- response headers, content types, pagination, filtering, and streaming framing; and
- consumed OpenAPI operations and schemas.

Minimal API mappings use standard ASP.NET Core binding and System.Text.Json configuration. Domain or
application services remain the default home for business validation; endpoint filters may handle
transport-level validation. Errors use ASP.NET Core ProblemDetails facilities and typed results while
preserving the pinned public behavior. OpenAPI metadata is declared on route groups or endpoints and
verified as a consumer contract.

Streaming endpoints use ASP.NET Core response primitives and must preserve cancellation,
backpressure, framing, heartbeat/idle behavior, resume semantics, and response cleanup. A shared
streaming helper is justified only after at least three consumers demonstrate a capability gap in
the platform primitives.

### Specialized endpoint surfaces

- **Root and management APIs.** Existing Minimal API management surfaces already conform to the
  target authoring model. Their security disposition still must be explicit.
- **Host-control APIs.** Authoring may use Minimal APIs, but the access model remains the server-side
  credential boundary recorded by
  [ADR 0037](0037-studio-management-bridge-keeps-host-management-key-server-side.md). A browser-facing
  Studio bridge is user-permission protected; the backend host-control call is host-credential
  protected.
- **Workflow-authored HTTP endpoints.** These remain a distinct runtime publication model rather
  than pretending to be statically declared module mappings. At publication time they must carry
  explicit security disposition and route ownership metadata and participate in collision checks.
  The matched endpoint metadata is the binding authority for execution: a request resolves the exact
  shell generation and service provider recorded at publication, never whichever generation is
  currently active for the same shell identifier. A complete candidate manifest is validated before
  one atomic publication; rejection preserves the previous generation, while requests already bound
  to it retain that exact generation through drain. Issue
  [#1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345) owns the publication,
  resolution, drain, and collectible-context implementation and tests.
- **[Dynamically unloadable endpoint modules](../glossary/elsa.md).** New unloadable REST modules use
  explicit Minimal API mapping
  and require repeatable collectible-`AssemblyLoadContext` evidence across routing, DI,
  serialization, and disposal. FastEndpoints is forbidden in assemblies promised to be
  dynamically unloadable.
- **MVC.** No current first-party MVC endpoint surface exists, so MVC authoring and adapter parity
  are out of scope. Introducing MVC requires a separate scope and authorization decision.
- **Third-party endpoints.** Third-party authoring choices and compatibility obligations are out of
  scope. Independent host security, collision, and lifecycle rules still apply where their own
  contracts say so; this ADR does not create those contracts.

### Transitional FastEndpoints exceptions

Existing first-party FastEndpoints registrations are transitional inventory. A new or materially
expanded FastEndpoints surface requires an approved compatibility exception that records:

- the exact module, routes, and owning feature;
- executable evidence of the ASP.NET Core capability gap;
- HTTP/OpenAPI and authorization contract coverage;
- why a module-local Minimal API implementation is insufficient;
- the removal owner and follow-up issue; and
- confirmation that the assembly is not promised to be dynamically unloadable.

Convenience, familiarity, or avoiding a migration seam is not an exception. An approved exception
still uses the shared Foundation Identity policy path and standard security/ownership metadata.

The canonical, machine-readable exception registry is
`tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json`, created and enforced by
[#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346). Each entry identifies the
owning module and feature, exact registration and route/method surface, capability-gap evidence,
contract-test fixtures, linked approval issue or pull request, approving maintainer or architect,
status, removal owner and issue, target migration wave, and confirmation that the assembly is not
promised to be collectible. Only an `Approved` entry backed by the linked review is an allowlist
entry; proposals and stale entries grant no exception. Closing the removal issue or completing the
target wave expires the entry.

The architecture guard combines a deterministic first-party endpoint inventory with registration
ownership evidence. It rejects every new or expanded FastEndpoints registration that does not match
an exact approved registry entry, and rejects expired, stale, or orphaned entries. Registry changes
are reviewed architecture changes and may expand the surface only with a separately approved
compatibility gap. The retirement gate is stricter than the registry: its scanner must report zero
first-party FastEndpoints registrations, including registrations that still have an allowlist entry.

### Retirement criteria

FastEndpoints is retired from first-party REST authoring only when all of the following are true:

- the runtime inventory reports zero remaining first-party FastEndpoints registrations;
- every migrated module passes its pinned HTTP, OpenAPI, security, and coexistence gates, plus
  unloadability gates when the module is promised to be collectible;
- no first-party module depends on FastEndpoints endpoint bases or process-global discovery;
- FastEndpoints packages, startup configuration, discovery, and transitional tests are removed;
- dynamic and host-owned routes pass the approved ownership and collision policy; and
- the program completion report records any remaining non-first-party or non-REST dispositions.

Migration waves are separate reviewable work units. Each wave must leave the repository deployable
and must remove the migrated module's obsolete FastEndpoints wiring rather than carrying two live
implementations indefinitely. Each owner report records its stable contract assembly, explicit mapper,
authorization disposition, immutable before evidence, native OpenAPI/serialization lifetime proof, and
live-host result. Activities Design is the first large provider/store-rich owner to exercise those
obligations together; Publishing is the first owner to take the first-party FastEndpoints inventory
to zero and remove the now-empty Workbench shell feature.

## Considered options

- **Keep FastEndpoints as the permanent feature-API model.** Rejected because it preserves the split
  authorization path and process-global discovery, conflicts with collectible module goals, and
  leaves root and module APIs on different defaults.
- **Replace all FastEndpoints routes in one change.** Rejected because the spike did not prove parity
  for all 178 registrations and a single migration would make contract regressions and rollback
  unbounded.
- **Create an Elsa endpoint framework over Minimal APIs.** Rejected because it would recreate the
  abstraction being removed and obscure standard ASP.NET Core behavior. Only evidence-backed,
  cross-module conventions belong in the shared layer.
- **Put authorization in path-specific middleware.** Rejected because route paths are not permission
  ownership boundaries, endpoint requirements become invisible to inspection, and standard policy
  challenge/forbid behavior can be bypassed.
- **Force workflow-authored routes into the static module-mapping shape.** Rejected because their
  routes are runtime-authored and generation-owned. They share metadata and publication rules, not
  the static authoring lifecycle.
- **Change the management protocol while changing frameworks.** Rejected because HTTP/JSON versus
  JSON-RPC or gRPC is independent of the endpoint authoring and authorization decision.

## Consequences

- First-party endpoint composition becomes explicit and locally inspectable instead of depending on
  process-global discovery.
- Hosts may run Minimal API and FastEndpoints routes together during migration, but every remaining
  FastEndpoints surface has an observable exit condition.
- Modules may carry a little more explicit mapping and metadata code. That repetition preserves
  ownership and is preferred to a broad shared endpoint framework.
- Public contract snapshots become a prerequisite for migration, so framework replacement cannot
  silently change binding, serialization, errors, streaming, or OpenAPI.
- Foundation Identity becomes the single permission-semantics owner across endpoint adapters.
- Dynamic publication and unloadability remain separate proof obligations; choosing Minimal APIs
  does not make either correct automatically.
- This ADR stabilizes the target and its boundaries; it does not itself migrate a production
  endpoint. Production changes remain in the linked program slices.

## Constitutional alignment

This decision uses standard ASP.NET Core adapters and module-local mapping methods; it does not
introduce a new cross-feature structural pattern. Framework §2.24's closed pattern catalog remains
draft and is therefore not treated as a ratified gate here. If the shared endpoint layer grows into
a new framework or composition pattern, that proposal must return through the §2.24 ratification
process rather than expanding this ADR implicitly.

Elsa constitution §E2.1 currently lists `Elsa.Api.FastEndpoints` as the shipped endpoint-framework
adapter. That table describes the current domain tree; this ADR makes the adapter transitional. The
table and generated maps should change only as production migrations alter the shipped tree.

## Linked decisions and evidence

- [Endpoint framework and authorization spike](../reports/endpoint-framework-authorization-spike-2026-08.md)
- [First-party REST API Consolidation program](../program-goals/first-party-rest-api-consolidation.md)
- [Program issue #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
- [Architecture decision slice #1343](https://github.com/elsa-workflows/elsa-foundation/issues/1343)
- [Shared permission contract #1344](https://github.com/elsa-workflows/elsa-foundation/issues/1344)
- [Migration evidence and authoring gates #1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)
- [Atomic CShells endpoint publication #1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345)
- [Studio Preferences canary #1347](https://github.com/elsa-workflows/elsa-foundation/issues/1347)
- [Activities Design migration #1373](https://github.com/elsa-workflows/elsa-foundation/issues/1373)
- [Activities Design migration evidence](../reports/activities-design-api-migration-2026-08.md)
- [Publishing migration #1374](https://github.com/elsa-workflows/elsa-foundation/issues/1374)
- [Publishing migration evidence](../reports/publishing-api-migration-2026-08.md)
- [Studio management bridge decision](0037-studio-management-bridge-keeps-host-management-key-server-side.md)
