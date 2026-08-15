# Research: REST API Migration Compatibility and Authoring Gates

## Runtime inventory versus source inventory

**Decision**: Treat the endpoints published through ASP.NET Core `EndpointDataSource` as the canonical enabled surface. Supplement it with a Roslyn source scan only for FastEndpoints registrations that must be reconciled with the transition registry.

**Rationale**: Runtime endpoints are the framework-neutral truth for Minimal APIs, MVC, and FastEndpoints and include applied conventions. Source analysis can identify legacy authoring even when runtime metadata does not preserve its origin, but it cannot reliably determine the enabled host surface on its own.

**Alternatives rejected**: A source-only inventory misses runtime composition and conventions. A hand-maintained route catalog drifts. A FastEndpoints-specific discovery API would make the gate depend on the framework being removed.

## Metadata seam

**Decision**: Add a small `Elsa.Api.AspNetCore` package containing typed ownership and non-permission security-disposition metadata plus standard endpoint convention extensions. Foundation Identity permission metadata remains authoritative for permission-protected endpoints.

**Rationale**: ADR 0068 requires endpoint ownership and one of four primary security dispositions. Standard metadata survives authoring-framework changes and is readable from the normal routing surface. Keeping permission semantics in Foundation Identity prevents a second authorization model.

**Alternatives rejected**: Route-prefix inference is incomplete and brittle. Custom middleware hides endpoint requirements. A custom endpoint DSL would replace one authoring framework with another abstraction.

## Compatibility evidence

**Decision**: Capture canonical HTTP observations and a consumed OpenAPI projection, then compare named facets with exact approved differences.

**Rationale**: The migration is authoring-only, so observable route, binding, JSON, status, ProblemDetails, paging/filtering, streaming, and documentation behavior must stay stable. Facet-level comparison produces actionable failures and permits a narrowly reviewed intentional change.

**Alternatives rejected**: Whole-file golden snapshots are hard to diagnose and often invite broad re-approval. Full OpenAPI generation inside the helper would add authoring-specific dependencies. String ignores can mask unrelated drift.

## Transition exception registry

**Decision**: Make `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` the single exact registry for remaining first-party FastEndpoints surfaces. Each record names the owned surface, removal owner, follow-up issue, and dynamic-unload prohibition.

**Rationale**: The current authoring surface is transitional but large. Freezing it through exact records prevents expansion while allowing ordered migrations.

**Alternatives rejected**: A count-only gate permits routes to be replaced by different legacy routes. Assembly-wide exceptions are too broad. Comments in source cannot be reconciled deterministically.

## Permission ownership

**Decision**: Validate enabled permission metadata against active `IPermissionContributor` output. A consumed permission must exist and have exactly one catalog owner; consumption by another endpoint owner is allowed when ownership is unique and the endpoint's declared disposition is consistent. Never catalog or consume `*` as an endpoint permission.

**Rationale**: Catalog ownership defines who declares the permission, while endpoint ownership defines who exposes the route; those responsibilities may intentionally differ. The administrative wildcard is a grant semantic, not a permission declaration.

**Alternatives rejected**: Requiring the route owner to declare every consumed permission prevents legitimate shared permissions. Accepting duplicate catalog owners makes provenance order-dependent.

## Collectible-context evidence

**Decision**: Compile a small fixture assembly with Roslyn into a collectible `AssemblyLoadContext`, exercise route, DI, and serializer publication stages independently, then return only weak references from a non-inlined helper before bounded forced collection.

**Rationale**: Weak-reference evidence is the only reliable assertion of collection. Staged deliberate retention fixtures distinguish the subsystem retaining the assembly. In-memory compilation keeps the fixture self-contained and repeatable.

**Alternatives rejected**: Process-memory comparisons are noisy. Loading a production feature brings unrelated retention. One combined lifecycle cannot identify which framework-held reference caused failure.

## Baseline governance

**Decision**: Baseline builders are deterministic but never auto-accept changes. Updates are explicit diffs reviewed with the owning issue and follow-up.

**Rationale**: A gate that can silently rewrite its expected state cannot prove compatibility or prevent legacy expansion.

**Alternatives rejected**: Environment-variable snapshot updates are convenient but too easy to invoke accidentally in CI or broad local runs.
