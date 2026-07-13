# Research: Domain-Owned Management APIs

## Split the facade into existing domain APIs

**Decision**: Remove `ElsaWorkflowManagementApi` and distribute its behavior across Workflow Design, Activity Design, Expressions, Publishing, Runtime, and API Capabilities. Do not create a global management implementation module.

**Rationale**: The facade contains 28 routes from unrelated domains, hard-codes the `default` shell through `IShellRegistry`, bypasses the domain FastEndpoints permission convention, and duplicates existing endpoints.

**Alternatives considered**: Move the whole class into Workflow Design; create `Elsa.Management.Api`; retain a compatibility proxy. All preserve the wrong dependency envelope or a second public surface.

## Use one explicit shell-scoped capability document

**Decision**: Add `GET /capabilities`. Active API features declare static client promises explicitly; conditional contributions use the framework contributor/event pattern. The response is authenticated, permission-neutral, shell-scoped, and contains capability ID, major version, and canonical link relations.

**Rationale**: One request avoids domain probing. Feature names are internal composition identity, not supported client contracts. A previous global `Elsa.Features.Abstractions` approach was reverted and conflicts with current domain/package direction.

**Alternatives considered**: One endpoint per domain; infer feature names; include permissions or arbitrary state. These increase requests, couple clients to internals, or duplicate authorization.

## Correct retention before the public migration

**Decision**: Retained executable IDs are the union of live source-reference artifact IDs and distinct artifact IDs pinned by retained `WorkflowExecutionState` records. Add an efficient retained-root query and a safe GC grace/concurrency boundary.

**Rationale**: Runtime resume loads the pinned executable directly. Current GC consults only source references and can delete an artifact required by any execution status or race the artifact-before-reference publish sequence.

**Alternatives considered**: Duplicate execution source references; reconstruct from Design; load every execution during GC. These duplicate truth, violate artifact-only Runtime, or fail bounded-query requirements.

## Make publication slot authority explicit

**Decision**: Publishing owns a slot aggregate keyed by `(definitionId, slotName)`. `default` replacement is ordinary publish behavior; named slots enable intentional coexistence. Activation uses revision compare-and-swap and a transaction/outbox boundary so failure leaves old authority intact.

**Rationale**: Current publishing appends a source reference and indexes by artifact. Failures can leave references, bindings, schedules, and in-memory routes divergent.

**Alternatives considered**: Preserve append-only publishing; delete the old artifact; best-effort unpublish after indexing. These cause accidental coexistence, break executions, or expose partial success.

## Identify trigger authority by publication

**Decision**: Trigger bindings and recurring schedules carry publication identity. Providers declare Exclusive or FanOut cardinality. HTTP is Exclusive. Preflight checks other authoritative publications while excluding the one replaced in the same slot.

**Rationale**: Artifact identity cannot distinguish publication intent for content-addressed artifacts. Existing HTTP validation allows the same definition across artifacts, while generic routing fans out to every match.

**Alternatives considered**: Keep artifact-owned bindings; make every trigger exclusive; use definition identity. Each loses a supported side-by-side or fan-out case.

## Keep mutation and inspection in their owning domains

**Decision**: Publishing mutates publications and source references. Runtime lists, gets, and runs executables and exposes provenance read-only. Physical artifact deletion is GC or exceptional administration.

**Rationale**: The existing inspector is read-only over Runtime stores but registered in Publishing. Facade delete/restore operations conflate publication intent with artifact lifetime.

**Alternatives considered**: Move all executable operations to Runtime; retain executable soft-delete semantics. Both give Runtime publication policy or undermine retention.

## Enrich rather than duplicate Design contracts

**Decision**: Extend Design with initial authored state, first-class draft lifecycle, metadata patching, soft-delete/restore/permanent-delete, aggregate list projections, scoped-variable analysis, and input options. Persisted version lookup rejects synthetic `draft:` IDs.

**Rationale**: Existing domain endpoints conflate draft replacement and definition update, expose arbitrary version creation, and lack Studio projections. The facade adds them with N+1 queries and synthetic versions.

**Alternatives considered**: Copy facade routes; keep `rootKind`; keep synthetic versions. These preserve duplication, concrete activity coupling, or incorrect reconstruction.

## Collapse activity bootstrap into one catalog

**Decision**: Activity Design exposes one availability-filtered normalized authoring catalog sourced only from persisted definitions/versions, including descriptors, UI, ports, structure, and authoring templates.

**Rationale**: Studio combines raw activities and descriptors from two calls. The facade loads and joins the same catalog twice.

**Alternatives considered**: Keep two endpoints; enumerate live providers. These duplicate projection work or violate catalog source of truth.

## Add a focused Expressions API

**Decision**: Add `Elsa.Expressions.Api` for expression and variable-type descriptors, projecting registered contracts rather than hard-coded defaults.

**Rationale**: Expressions are not Workflow Design-owned, and custom hosts need the contract without Elsa.Server.

**Alternatives considered**: Put descriptors in Workflow Design; keep hard-coded defaults. Both couple expression support to a consumer or advertise unavailable engines.

## Coordinate Studio as a required downstream migration

**Decision**: Split Studio's central workflow client by domain, cache global capabilities, remove fallbacks, replace executable deletion with publication actions, add preflight/slot confirmation, compose initial state from the catalog, and render instances from pinned Runtime executables.

**Rationale**: Legacy use is centralized in the Workflows client and one Weaver call, but semantic changes touch creation, publishing, lifecycle, and instance rendering.

**Alternatives considered**: Release Foundation first; retain the facade; rewrite URLs only. The agreed delivery rejects a compatibility interval.

## Assign runtime diagnostics to Runtime API

**Decision**: Move runtime diagnostics settings from the legacy prefix into Runtime API.

**Rationale**: The endpoints already belong to Runtime API; only their route constant leaks the facade.

**Alternatives considered**: A global management module or one-off old route. Both retain host-owned surface.

## Validate Foundation and Studio as one release unit

**Decision**: Run focused domain tests during implementation, then Foundation solution/architecture gates and Studio package plus root typecheck/test/build gates against a custom host without Elsa.Server management code.

**Rationale**: Foundation tests cannot alone prove the downstream client or no-broken-release requirement.

**Alternatives considered**: Treat green Foundation tests as sufficient.
