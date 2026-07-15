# Research: Reusable Activity Definitions

This document records the Phase 0 decisions that turn [PRD #671](https://github.com/elsa-workflows/elsa-foundation/issues/671) into an implementation-ready backend plan. All previously open design questions were resolved during the maintainer grilling session; no `NEEDS CLARIFICATION` items remain.

## R1. Reuse is an activity boundary, not nested workflow execution

**Decision**: Retire the Foundation workflow-as-activity concept. A reusable visual implementation is an `ActivityGraph`; Runtime executes it through a Runtime-owned `GraphActivity` as an ordinary composite within the current workflow execution. The outer activity remains visible and descendants are ordinary activity executions.

**Rationale**: This matches the intended Elsa Core behavior, preserves one workflow execution identity, and lets the existing scheduler, pipeline, checkpoint, bookmark, incident, cancellation, retry, and inspection seams do their normal jobs.

**Alternatives considered**:

- Start a child workflow instance. Rejected because it changes identity, scope, durability, inspection, and cancellation semantics and recreates the wrong abstraction.
- Inline the template with no outer boundary. Rejected because callers lose contract enforcement, lifecycle ownership, retry scope, and clickable inspection.
- Keep `WorkflowDefinitionActivity` and implement its deferred body. Rejected because workflow identity remains the wrong reusable-activity identity and upgrade/versioning remains awkward.

## R2. Extend the Activity Catalog with drafts and immutable versions

**Decision**: Keep `ActivityDefinition` as the stable catalog identity. Add multiple `ActivityDefinitionDraft` aggregates and enrich immutable `ActivityDefinitionVersion` records. Each draft has an optimistic revision and optional immutable `SourceVersionId`; each definition lineage has one `ContentAuthority`.

**Rationale**: The existing catalog already owns picker identity and version discovery. A draft aggregate adds authoring concurrency and lineage without turning the immutable version row into mutable state or reusing workflow state for a different concept.

**Alternatives considered**:

- Add mutable fields to `ActivityDefinitionVersion`. Rejected because versions and published dependencies must remain immutable.
- Reuse `WorkflowDefinitionDraft` and `WorkflowDefinitionState`. Rejected because an activity contract/provider manifest has a distinct lifecycle and would collapse domain boundaries.
- Maintain one shared draft per definition. Rejected because concurrent work would overwrite or serialize unrelated authoring attempts.

## R3. Separate public contract, Design provider manifest, and Runtime consumer descriptor

**Decision**: An activity version has three deliberately separate shapes:

1. `ActivityContract`: provider-neutral inputs, outputs, outcomes, defaults, durability, and presentation.
2. `ActivityProviderManifest`: opaque authored source identified by stable `ProviderKey` and `SchemaVersion`.
3. `RuntimeActivityDescriptor`: executable payload identified independently by stable `ConsumerKey` and `SchemaVersion`.

Durable keys are namespaced strings controlled by providers, not CLR `FullName` values. Providers may infer contract proposals, but the draft contract is authoritative and publication verifies fidelity.

**Rationale**: Framework §2.6.4 requires design-time and runtime contracts to split by consumer. Stable keys allow providers to evolve CLR packaging without rewriting durable content and keep Runtime independent of Design types.

**Alternatives considered**:

- Continue using `DescriptorType` as a CLR full name for both Design and Runtime. Rejected because type identity is a persistence coupling and conflates two consumers.
- Put the public contract inside the provider payload. Rejected because version comparison, binding, catalog presentation, and compatibility policy would require provider-specific decoding.
- Let provider reconciliation overwrite the contract. Rejected because discovery would silently change a public API.

## R4. Publish deterministic, closed executable templates

**Decision**: Publishing an activity version compiles its provider manifest into an immutable `ExecutableActivityTemplate`. The template is content-addressed from canonical behavior only, records exact direct dependencies and a closed dependency set, and carries provider/compiler fingerprints plus Runtime consumer requirements. Provenance and layout remain on Source References and do not affect the behavior hash.

**Rationale**: This extends ADR 0038 (behavioral content identity), ADR 0039 (layout on Source Reference), ADR 0040 (one artifact store), and the Artifact-Only Runtime rule. A consuming workflow places already-compiled templates; it never recompiles activity source with a newer provider.

**Alternatives considered**:

- Compile graph source during every consuming workflow publish. Rejected because provider drift could change behavior without a new activity version.
- Embed provenance or layout in the template hash. Rejected because behaviorally identical templates would receive different identities.
- Store only direct source references and reload activity Design state at runtime. Rejected by artifact-only execution.

## R5. Publication is one atomic, expected-head transition

**Decision**: Under a per-definition lock, publication validates the expected draft revision and expected definition head, validates contract and provider source, resolves exact dependencies, detects cycles, compiles deterministically, evaluates the version diff and requested SemVer, persists the template and hierarchical layout Source Reference, persists the immutable version and direct dependency edges, and advances the head in one atomic visibility boundary.

**Rationale**: No consumer can observe a version without all execution material and dependencies, and competing publishers receive a stale-head result without losing their drafts.

**Alternatives considered**:

- Publish phases independently with cleanup on failure. Rejected because cleanup cannot prevent readers from observing partial state.
- Last-writer-wins publication. Rejected because it loses the compatibility baseline and can mislabel concurrent changes.
- Mutate the new head after version creation. Rejected because head visibility must be part of the same transition.

## R6. Version diff is platform-owned and provider-extensible

**Decision**: The platform computes a structured `ActivityVersionDiff` across public contract, defaults, outcomes, durability, provider identity, implementation hash, and dependencies. It enforces the PRD SemVer baseline; providers may append stricter diagnostics or raise the minimum bump but cannot lower it.

**Rationale**: Compatibility must remain comparable across provider shapes and power both publication validation and frontend upgrade UX.

**Alternatives considered**:

- Compare opaque manifests only. Rejected because identical provider payload changes can have different public compatibility impact.
- Let every provider define all SemVer rules. Rejected because users would face inconsistent meanings for the same public-contract change.
- Return only a required bump string. Rejected because authors and tools need stable per-change explanations.

## R7. Defaults are caller-side compiled bindings

**Decision**: An input default contains syntax plus value/expression source as a caller-side binding template. Workflow publication applies a default only when the caller input is absent, compiles it through the existing binding compiler, and embeds the resulting binding in the consuming artifact. Runtime evaluates and durably captures every effective input once at graph entry.

**Rationale**: Later default changes cannot affect an already published workflow, explicit null remains distinguishable from absence, and suspension/retry share one deterministic snapshot.

**Alternatives considered**:

- Evaluate defaults inside graph descendants. Rejected because retries or resumes could observe different values.
- Store only literal defaults initially. Rejected because expression defaults are a first-class requirement and fit the existing binding compiler.
- Treat null as absence. Rejected because requiredness and author intent become ambiguous.

## R8. Activity execution scope is ordinary durable Runtime state

**Decision**: Do not introduce a separate “custom activity invocation” entity. The outer `ActivityExecutionId` owns the graph boundary's durable inputs, graph-local variables, output captures, attempt lineage, and state. `GraphActivity` breaks the caller's user-variable chain but retains ambient runtime identity, tenant, services, time, tracing, and cancellation.

**Rationale**: `ActivityExecutionContext` remains the live execution façade; durable state remains in existing Runtime records and durable values. This avoids a competing identity/state model.

**Alternatives considered**:

- Add a monolithic graph-invocation blob. Rejected because it duplicates activity execution lifecycle and makes partial durable updates difficult.
- Reuse caller variables through lexical chaining. Rejected because reusable behavior would gain hidden inputs and outputs.
- Create a special “custom activity” scope. Rejected because Runtime has no behavioral distinction between first- and third-party activities.

## R9. Entry, exit, cancellation, and retry use existing checkpoint semantics

**Decision**: Entry capture plus first-child scheduling is one checkpoint; output capture plus boundary outcome, terminalization, and parent continuation is one checkpoint. Descendant bookmarks stay native. Cancellation wins only after subtree cleanup commits. Retry creates a fresh outer execution and descendants, retaining the pinned template and effective captured input snapshot while linking first and previous attempts.

**Rationale**: These are the existing scheduler-boundary durability rules applied to a composite boundary. They prevent stranded outers, duplicate output propagation, proxy bookmark drift, and reuse of failed local state.

**Alternatives considered**:

- Proxy descendant bookmarks on the outer activity. Rejected because it duplicates native ownership and complicates resumption.
- Reuse the same execution identity for retry. Rejected because failed local state and evidence would be conflated with the new attempt.
- Promise exactly-once external effects. Rejected because checkpointed orchestration cannot retroactively undo arbitrary external side effects.

## R10. Deterministic invocation origins namespace placement

**Decision**: Each template placement receives a canonical length-framed invocation-origin path. Full SHA-256 of that origin participates in executable-node and resume-target namespacing. Human-readable provenance is stored separately. Compilation and dependency traversal are iterative; Foundation sets no arbitrary graph-size ceilings, but exposes measurement and pluggable admission policy.

**Rationale**: Deep nesting and repeated placement remain deterministic and collision-resistant without making readable paths unbounded identifiers or relying on the process call stack.

**Alternatives considered**:

- Concatenate readable ancestor names into durable identifiers. Rejected because identifiers grow without bound and introduce escaping ambiguity.
- Truncate hashes. Rejected because collision handling would become an avoidable runtime concern.
- Apply a universal maximum depth. Rejected because a value safe for one host can still be unsafe for another and needlessly restrictive elsewhere.

## R11. Dependencies, lifecycle, and upgrades remain separate concerns

**Decision**: Immutable direct dependency edges are authoritative. Reverse/transitive pages are rebuildable projections with an explicit watermark. Retirement prevents new direct selection but does not invalidate closed parent templates; revocation is stronger. Upgrade planning produces a bottom-up snapshot pinned to all draft revisions and definition heads and applies selected changes atomically.

**Rationale**: Execution never depends on a mutable reverse index or live catalog lifecycle, while authoring still has enough evidence to plan changes safely.

**Alternatives considered**:

- Cascade retirement through all parents. Rejected because closed templates already contain their executable dependencies.
- Make reverse edges execution truth. Rejected because projection lag or rebuild would threaten execution.
- Rewrite published consumers in place. Rejected because it violates immutable exact pinning.

## R12. Inspection extends the existing activity-execution projection

**Decision**: Extend the current activity-execution detail with optional activity-definition/template boundary facts and attempt lineage. Add a cursor-paginated hierarchy page rooted at an outer activity execution and a separate pinned-layout read. Pages carry stable parent relations, relative depth, committed sequence, aggregate summary, and opaque replay cursor. Structure and sensitive-value permissions are evaluated independently on every request.

**Rationale**: Existing spec 079 already owns lifecycle evidence, bookmark/incident/value summaries, provenance, and checkpoint consistency. An additive hierarchy surface avoids a second inspection model and lets Studio click through without eager unbounded hydration.

**Alternatives considered**:

- Return the entire nested graph on workflow-instance detail. Rejected because loops/retries can make the response unbounded.
- Inspect from current activity Design source. Rejected because old runs would change and Runtime-only deployments would fail.
- Persist an aggregate status on every outer. Rejected because the outer lifecycle and subtree aggregate answer different questions; the aggregate is derivable.

## R13. Errors use RFC 7807 plus stable diagnostics

**Decision**: Keep the shared RFC 7807 envelope and add `errorCode`, `traceId`, and an ordered `diagnostics` extension. Each diagnostic carries stable code, severity, subject, structured location, message, optional remediation, and safe string metadata. Publication validation uses `422`; optimistic/content-authority conflicts use `409`; tenant authorization uses `403`; absence uses `404`; malformed requests use `400`.

**Rationale**: The repository already globally configures Problem Details. A stable diagnostic array supports editors, CI, import tools, and provider-specific locations without exposing opaque provider payloads.

**Alternatives considered**:

- Invent a separate error envelope. Rejected because it would fragment the API.
- Return a dictionary keyed by field. Rejected because dependency paths, graph nodes, provider source, and cross-artifact conflicts are not all fields.
- Put arbitrary JSON in diagnostics. Rejected because it creates an unreviewable disclosure and compatibility surface.

## R14. Groundwork is the first-party persistence target

**Decision**: Core models and store contracts remain provider-neutral. New durable persistence lands in Groundwork and in-memory conformance stores. No EF migration or new EF schema is added; the temporary EF lane is not expanded, consistent with ADR 0042 and the Zero-EF program goal.

**Rationale**: Elsa Foundation has accepted Groundwork as its sole eventual concrete persistence family. Adding a second durable implementation now would immediately create migration work and contradict the active transition.

**Alternatives considered**:

- Implement EF and Groundwork in parallel. Rejected as new work on a retiring provider family.
- Put Groundwork types in Core contracts. Rejected because persistence invariants remain provider-neutral.
- Use only in-memory storage for the first slice. Rejected because full restart/resume is the release gate.

## R15. Clean break and Elsa 3 import are separate compatibility policies

**Decision**: Remove `WorkflowDefinitionActivity`, `WorkflowIdentity`-backed workflow catalog reconciliation, and `UsableAsActivity`. Retain the explicit separate-workflow execution activity. Provide Elsa 3 collection-aware plan/apply conversion that produces an activity definition plus a wrapper workflow, with deterministic identities, exact rewrites, atomic closures, and explicit cycle diagnostics.

**Rationale**: Foundation is pre-release and should not carry two reusable-activity models. The constitution limits backward compatibility to one-way Elsa 3 import, where collection context is required to rewrite references correctly.

**Alternatives considered**:

- Maintain a Foundation compatibility adapter. Rejected because it preserves the conceptual and persistence ambiguity this feature removes.
- Convert each Elsa 3 workflow independently. Rejected because references and cycles are collection-level facts.
- Break cycles by substituting separate-workflow execution. Rejected because it silently changes semantics.

## R16. Smallest safe vertical slice and release gate

**Decision**: The first safe slice includes authoring drafts, the graph provider, atomic template publication, exact workflow placement, deterministic expansion, input isolation, one output mapping, natural `Done`, one native descendant bookmark, complete restart/resume, hierarchical inspection, legacy-surface removal, and architecture guards. The mandatory black-box gate tears the host down over durable storage between suspension and resume.

**Rationale**: Anything smaller can demonstrate compilation while leaving durability, inspection, or the old competing model unresolved. The slice is narrow in behavior but crosses every load-bearing seam once.

**Alternatives considered**:

- Stop at compilation. Rejected because the current code already has a construct-only dead end.
- Execute only non-suspending graphs. Rejected because durable nested execution is the central risk.
- Defer inspection. Rejected because the outer boundary must be operable and the frontend contract must stabilize before UX work.
