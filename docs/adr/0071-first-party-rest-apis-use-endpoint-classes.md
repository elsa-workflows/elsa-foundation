---
status: accepted
date: 2026-08-25
decision_context: Endpoint-class rollout on branch claude/shared-endpoint-conventions (PR #1417) and the endpoint-library spin-off design session, ratified by Sipke Schoorstra; supersedes the shared-layer bound in ADR 0068
---

# First-party REST APIs use endpoint classes over Minimal APIs

## Context

[ADR 0068](0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) settled that first-party REST
APIs use ASP.NET Core Minimal APIs, and bounded what the shared layer may contain. Two of its
sentences bear directly on what has been built since:

> The shared layer must not recreate FastEndpoints behind Elsa-owned request bases, handler bases,
> discovery, or a parallel endpoint DSL. Prefer a small amount of module-local repetition over a
> shared abstraction that merely renames an ASP.NET Core primitive.

and, among the rejected options:

> **Create an Elsa endpoint framework over Minimal APIs.** Rejected because it would recreate the
> abstraction being removed and obscure standard ASP.NET Core behavior.

The rollout that followed produced request bases and module-local assembly scanning. Workflows
Design, Workflows Publishing, and Activities Design are now fully migrated: every routed operation
is one class under `Endpoints/<Resource>/<Operation>/Endpoint.cs`, carrying its own route,
metadata, permission, and handling, composed by a single call, with the Api-assembly mediator
wrappers retired behind operation interfaces. The rollout merged as PR #1417 (`3f14435c3`).
Seventeen further modules still inline API Explorer metadata per endpoint.

The shape is within the letter of what 0068 excluded. 0068 anticipated this and named the route out:

> If the shared endpoint layer grows into a new framework or composition pattern, that proposal must
> return through the §2.24 ratification process rather than expanding this ADR implicitly.

This ADR is that return. It resolves the question rather than leaving the merged rollout to carry a
standing objection — the review on PR #1417 raised exactly this, and this ratification answers that
thread — and it does so on the two grounds 0068 itself used: whether the result is unloadable, and
whether it obscures standard ASP.NET Core behavior.

Both grounds are now answerable with measurement rather than argument. The FastEndpoints rejection
rested on the endpoint-framework spike's finding that **0 of 3 collectible assembly contexts were
collected** after host disposal, with registrations accumulating across disposed hosts (issue
#1199). The endpoint-class implementation is subject to a metadata lifetime validator, a
byte-stable endpoint manifest baseline, and repeated collectible load-and-unload cycles across four
waves of real modules.

This decision governs endpoint authoring. It does not change the HTTP/JSON protocol, public route
contracts, operation identifiers, or domain behavior.

## Decision

First-party REST endpoints are authored as **endpoint classes**: one class per operation, declaring
its route by attribute, refining its metadata by an overridable `Configure`, and owning its handling
in `HandleAsync`. Endpoint classes are mapped **module-locally, inside the owning module's own
explicit composition call**, from a shared library that carries zero Elsa domain dependencies.

### The constraints that bound this

The library is permitted request and handler bases only because it is bound by all of the following.
These are the operative rules; a change to any of them returns here.

1. **No process-global discovery and no static registry.** A module hands its own assembly to its
   own mapping call. Nothing found is stored anywhere that outlives the endpoint generation. Once
   the planned source generator lands, registration is generated per assembly and there is no scan
   at all in the default path.
2. **No framework static may hold a reference to a consumer type.** Caches keyed by contract type
   must be weak-keyed or generation-scoped. This rule exists because it was already violated once:
   a `static ConcurrentDictionary<Type, ConstructorInfo>` in the request binder rooted every
   contract type it had bound for host lifetime, which is structurally the same defect as
   FastEndpoints' `internal static readonly` registration state.
3. **Handlers publish as bare `RequestDelegate`.** A typed lambda makes `RequestDelegateFactory`
   publish the handler's own `MethodInfo` and `AsyncStateMachineAttribute` into endpoint metadata,
   which API Explorer retains for the host service-provider lifetime.
4. **Every mapping call returns `IEndpointConventionBuilder`.** Authorization, filters, CORS, rate
   limiting, output caching, and results remain ASP.NET Core's. There is no parallel result type, no
   parallel validation stack, and no parallel binding model beyond what constraint 3 makes
   unavoidable.
5. **Metadata lifetime validation runs as the final convention, fail-closed.** An unconfigured host
   gets the guard. Disabling it requires an explicit suppression call.

### What ADR 0068's bound becomes

0068's shared-layer sentence is narrowed. Read after this ADR, the prohibition is on **process-global
discovery, static registries, and a parallel endpoint DSL** — not on request and handler bases as
such. Bases that satisfy constraints 1 through 5 do not recreate the abstraction 0068 removed,
because what made the previous framework unloadable was its process-global state, not its base
classes.

The second half of 0068's rejection, that an endpoint framework would "obscure standard ASP.NET Core
behavior," is answered by constraint 4 and evidenced by the manifest baseline: the metadata the
endpoint classes generate is ordinary ASP.NET Core metadata, asserted byte-for-byte against a
committed file.

### Unload safety is the binding constraint

Where developer experience and unloadability conflict, unloadability wins. This is why the request
binder is hand-written rather than delegated to `RequestDelegateFactory`, why handlers are published
untyped, and why constraint 2 exists at all. The cost is a deliberately narrow binder covering the
shapes first-party endpoints use, with an explicit throw on anything else rather than silent
misbinding.

### Externalization

The `Elsa.Api.AspNetCore` and `Elsa.Api.Endpoints` assemblies are the pre-extraction staging of a
library intended to ship as an external MIT-licensed project that Elsa consumes as a package. They
already carry zero Elsa domain dependencies and zero NuGet package references, holding only
`FrameworkReference Microsoft.AspNetCore.App`.

When the criteria below are met, Elsa replaces those project references with a `PackageReference`.
**That swap is a packaging change, not an architectural one, and does not require a further ADR.**
The architectural commitment is made here, in the constraints above, and those constraints bind the
external library exactly as they bind the in-tree assemblies.

### What stays with Elsa

The library does not absorb Elsa's vocabulary. These remain Elsa-owned and are attached through the
library's convention extension points:

- **Authorization.** `RequirePermissionAttribute` implements the library's convention-attribute
  interface and applies the full requirement, so the attribute and the imperative form cannot drift.
  Foundation Identity never enters the library's dependency graph, preserving 0068's security
  disposition and permission-ownership model unchanged.
- **Endpoint ownership.** `EndpointOwnershipMetadata`, its host/module/dynamic-shell kinds, and
  `EndpointSecurityDispositionMetadata` stay Elsa types. The library knows only a group name. Elsa's
  inventory guards and manifest tooling read Elsa's vocabulary through a metadata extractor.
- **Mediator dispatch.** The bridge from endpoint classes to `IRequestSender`/`ICommandSender` is an
  extension over the library's public mapping seam and stays in Elsa, since the library is
  dispatch-agnostic and must not depend on a mediator.
- **Operation identifiers.** Elsa's `{Owner}Endpoints{Operation}` scheme is wire-visible through
  OpenAPI and is configured explicitly rather than taking any library default.

## Considered options

**Keep 0068 as written and stop the rollout.** Rejected. Seventeen modules would continue inlining
API Explorer metadata per endpoint, which is the drift 0068's own compatibility requirements exist
to prevent, and the FastEndpoints retirement criteria would stall with the transition-exception
registry already empty.

**Adopt an existing third-party endpoint library.** Rejected. FastEndpoints fails the unloadability
requirement by measurement, and 0068 already forbids it in assemblies promised to be dynamically
unloadable. No other .NET endpoint library makes an unloadability guarantee or exposes the metadata
lifetime seam this requires.

**Amend ADR 0068 in place without a new decision record.** Rejected. 0068 explicitly requires that a
shared layer growing into a framework return through governance rather than expand the ADR
implicitly. Amending the sentence that forbids the thing being built, in order to permit it, is the
implicit expansion 0068 names.

**Ratify a new pattern under framework §2.24.3 instead.** Considered and not treated as required.
0068's own constitutional alignment section records that "§2.24's closed pattern catalog remains
draft and is therefore not treated as a ratified gate here," and consuming an external library is
the Adapter/Bridge shape §2.24.2 already carries — the same shape by which FastEndpoints was
consumed. If the architects want the endpoint-class pattern catalogued regardless, the worked
example already exists and that ratification can run in parallel rather than blocking this decision.

## Consequences

One authoring model replaces per-endpoint metadata repetition across every first-party module. The
API Explorer description-method requirement, the documented-versus-runtime status split, and
owner-scoped problem translation are handled once rather than per endpoint, which removes an entire
class of silently-vacuous OpenAPI defects.

Unloadability becomes measurable rather than assumed, and the constraints above are enforced by
tests rather than by review discipline.

Costs and consequences to accept:

- **A dependency Elsa does not control**, once externalized. Mitigated by the constraints being
  restated here as Elsa's own requirements, and by Elsa being the library's primary consumer.
- **A deliberately narrow request binder.** Contract shapes outside the supported set throw
  explicitly at map or request time rather than binding silently. Widening it is a deliberate act.
- **Two registration paths** while the source generator rolls out: generated by default, reflective
  scan as fallback.
- **Two wire-visible changes** to decide explicitly at extraction, not by default. Endpoints
  documenting 401 and 403 unconditionally will document them only where authorization metadata is
  actually present, which changes the published document for anonymous endpoints. And the
  operation-identifier convention must be pinned rather than inherited.
- **Collectibility remains proven by test fixtures, not exercised in production.** As
  [ADR 0070](0070-rest-api-contracts-ship-in-one-assembly-per-domain.md) records, no production code
  creates a collectible `AssemblyLoadContext` today. Every collectible context in the repository is
  built by a test fixture. This decision improves what is measurable; it does not by itself make
  dynamic module replacement available. Third-party contract publication remains open (#1414).

## Externalization criteria

Elsa replaces the in-tree project references with a package reference when all of the following hold:

1. The library is published under MIT with no Elsa-specific vocabulary in its public surface, and no
   NuGet package dependencies beyond the ASP.NET Core shared framework.
2. Constraints 1 through 5 above are enforced by tests in the library's own repository, including a
   collectible load-and-unload proof that routes through the library's binding path rather than
   around it.
3. The representative host endpoint manifest is byte-identical after the swap, or every difference
   is recorded as a reviewed approved difference.
4. The two wire-visible changes named under Consequences are decided and recorded.

Meeting these criteria does not require a further ADR.

## Constitutional alignment

Framework §2.17 places a shared helper in a separate library only "when the helper itself is a
feature," above a three-consumer bar. An endpoint authoring model is a feature by that test, and its
consumer count is every first-party REST module — presently more than twenty.

Framework §2.24's closed pattern catalog remains draft, and 0068 already records that it is not
treated as a ratified gate for this area. This decision uses the Adapter/Bridge shape §2.24.2
carries: Elsa composes an external library through standard ASP.NET Core conventions and its own
convention attributes. Should the architects choose to catalogue "self-describing endpoint class
with module-local mapping" as a distinct structural pattern, this ADR's decision section supplies
the criteria and the Workflows Design Definitions group supplies the worked example.

## Linked decisions and evidence

- [ADR 0068](0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) — superseded in its
  shared-layer bound only. Its scope, security disposition, authorization ownership, HTTP and
  OpenAPI compatibility, and FastEndpoints retirement criteria all remain in force.
- [ADR 0070](0070-rest-api-contracts-ship-in-one-assembly-per-domain.md) — contract assembly
  structure and the note that no production code creates a collectible load context.
- Issue #1199 — FastEndpoints composition-level retention: 0 of 3 collectible contexts collected,
  registrations accumulating across disposed hosts.
- Endpoint-framework spike #1329, report PR #1338, program slice #1343 — the evidence base 0068 rests
  on.
- PR #1417 (merged as `3f14435c3`) — the endpoint-class rollout this decision governs, including
  the Workflows Design, Publishing, and Activities Design migrations and their byte-frozen
  compatibility corpora.
- PR #1429 — the binder's per-parameter route→body→query→default precedence made presence-aware,
  restoring the FastEndpoints-era behavior for partial bodies.
- PR #1428 and the capabilities fix inside #1417 — two anonymous-type fingerprint serializations
  replaced by named records over owner contexts: constraint 2 enforced in practice, twice, without
  changing wire bytes.
- Issue #1414 — third-party unloadable contract publication, still open.
- `DomainManagementApiCompositionTests.Representative_host_manifest_is_stable_reviewed_and_permission_owned`
  — the manifest built ten times per run, asserted deterministic, diffed against a committed
  baseline, and permission-ownership validated.
- `OpenApiLifetimeBoundaryTests` — 24 facts covering nested, private, generic-argument,
  delegate-target, multicast, and enumerable-yielded collectible metadata graphs, plus fail-closed
  behavior on unknown metadata shapes.
- `OpenApiLifetimeCollectibilityTests` — 4 facts against real OpenAPI document generation.
- `Wave1MinimalApiCollectibilityTests` and the Wave 3, 4, and 5 suites — three collectible
  load-map-serve-dispose-unload cycles per owner with weak-reference evidence and 32 forced
  compacting collections.
