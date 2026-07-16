# Research: Deterministic and Bounded Workflow Dispatch

## Decision 1: Represent child targets as first-class executable dependencies

**Decision**: Add a canonical `WorkflowExecutableDependency` collection to `WorkflowExecutable`. One dependency exists per distinct child artifact and carries the child artifact ID/hash plus the sorted dispatch node IDs that bind to it.

**Rationale**: Node metadata is currently enriched before hashing but is not included by `WorkflowExecutableHasher`. A metadata-only pin therefore does not change parent identity and can be corrupted by content-addressed deduplication. A first-class dependency both binds behavior and gives retention a graph to traverse.

**Alternatives considered**:

- Hash all node metadata: rejected because metadata may include per-publication reference IDs and other non-behavioral facts.
- Store only a transitive flattened closure: rejected because direct child hashes already inductively represent reachable behavior, while flattened closures increase storage and introduce more canonicalization surface.
- Keep dependencies only in a side table: rejected because Runtime must execute from the artifact alone and storage providers should persist one immutable artifact shape.

## Decision 2: Hash canonical direct dependencies, not publication provenance

**Decision**: Extend the behavioral hash payload with sorted direct dependency records and the declared-input contract. Dependency ordering and dispatch node ordering are ordinal and de-duplicated.

**Rationale**: A child artifact hash already contains its own direct dependencies, so hashing direct child identities makes transitive changes propagate inductively. Per-publication reference ID, publication ID, slot, timestamps, access context, and retirement state remain reference facts under ADR 0038/0040.

**Alternatives considered**:

- Hash the full transitive closure: correct but redundant and more expensive; it also duplicates diamond nodes.
- Hash workflow definition IDs rather than artifact IDs: rejected because it would make version-skewed calls ambiguous and would not pin exact behavior.
- Include live source-reference provenance: rejected because behaviorally identical publications must remain content-addressed to the same artifact.

## Decision 3: Project a versioned runtime input contract into the executable

**Decision**: Add an optional `WorkflowExecutableInputContract` containing canonical descriptors for name, the exact shared `TypeReference`, required flag, and literal default semantics. Newly compiled artifacts always carry version 1, including an explicitly empty contract. Missing contract remains readable/executable for legacy compatibility but is ineligible as a new strict DispatchWorkflow target until recompiled and republished.

**Rationale**: Runtime cannot load Design state. A projected immutable contract lets publication and execution validate the exact child without violating the Design/Runtime split. A nullable/versioned wrapper distinguishes a closed empty contract from pre-feature artifacts.

**Alternatives considered**:

- Read `WorkflowDefinitionState.Inputs` during child start: rejected by the artifact-only runtime hard rule.
- Copy Design `InputDefinition` into Runtime: rejected because UI/storage metadata is irrelevant and would couple the domains.
- Treat a missing contract as empty: rejected because it would silently break seeded legacy artifacts and compatibility tests.

## Decision 4: Validate static bindings at publication and realized maps at runtime

**Decision**: Literal DispatchWorkflow input objects are fully validated while compiling the parent. Expression-backed maps receive structural compile checks and are validated after evaluation but before the dispatch checkpoint is staged. Runtime validates names ordinally, rejects unknown/blank/duplicate values, checks required/default semantics and JSON compatibility through the well-known type registry, materializes supported literal defaults, and never stores rejected raw values. A declared name such as `tenant` is still only a workflow input and cannot mutate typed runtime context.

**Rationale**: Publication should reject knowable errors, while dynamic values cannot be trusted until evaluation. The checkpoint boundary is the last safe point before dispatch responsibility is committed.

**Alternatives considered**:

- Validate only at runtime: rejected because stale literal bindings would publish successfully.
- Require all input maps to be literal: rejected because expressions are a core activity-input capability.
- Accept undeclared names: rejected because it makes the executable input contract advisory rather than enforceable.

## Decision 5: Use a named Sequential contribution event

**Decision**: Publishing owns a named compile-contribution event with one aggregating handler over typed sources. DispatchWorkflow implements the source and returns node metadata/dependency contributions. The event is published Sequentially and failures abort compilation.

**Rationale**: This is cross-feature fan-in whose result the compiler reads back. The framework constitution explicitly requires a named event, one aggregating handler, and source interfaces for returned data.

**Alternatives considered**:

- Continue injecting `IEnumerable<IExecutableNodeMetadataContributor>` at the compiler: rejected as an unsanctioned fan-in topology.
- One event handler per activity feature: rejected because contribution handlers must not depend on peer ordering and do not scale.
- Anonymous mediator request: rejected because compiler correctness depends on the contribution result.

## Decision 6: Authorize retained child starts through the parent dependency edge

**Decision**: Add typed retained-dependency provenance containing parent artifact ID/hash and dispatch node ID. `WorkflowStartDispatcher` loads the parent artifact and authorizes the exact child ID/hash only when its dependency graph binds that node to that child artifact. The immutable provenance is copied into child start/state inspection; ordinary/root starts continue to use live source provenance.

**Rationale**: An already-published parent must continue to use its pin after the child reference is replaced or unpublished. A narrow, verifiable authority avoids a generic provenance bypass and stays artifact-only.

**Alternatives considered**:

- Keep `RequireLiveReference` for child starts: rejected because it directly violates retained-pin execution.
- Set the legacy reference-less flag: rejected because it is a broad compatibility escape hatch and proves no parent-child relationship.
- Preserve retired child references forever: rejected because source lifecycle and artifact lifetime are intentionally separate.

## Decision 7: Traverse dependency reachability for retention and lease the closure

**Decision**: GC computes the transitive closure from all live source-reference roots and retained-execution roots. Root creation acquires renewable leases for the root plus its complete dependency closure in ordinal artifact-ID order. Missing or cyclic graphs fail closed. Final guarded deletion recomputes protected reachability before deleting.

**Rationale**: Reachability without closure-wide leasing still permits collection of a child between parent validation and durable root creation. Deterministic acquisition avoids deadlocks; conservative failure preserves artifacts.

**Alternatives considered**:

- Reference-count each dependency: rejected because updates, shared diamonds, and crash recovery make counts fragile.
- Lease only the parent: rejected because it does not fence child deletion.
- Never collect dependency artifacts: rejected because unreachable closures would leak permanently.

## Decision 8: Expose one replacement start policy

**Decision**: Add `IWorkflowExecutableStartPolicy` as a documented single-implementation replacement contract. It receives immutable artifact/start context and returns an allow or machine-classifiable deny decision. The default allows. Dispatcher evaluates it after resolving/authorizing the artifact and before actor lookup or execution-state materialization.

**Rationale**: Domain/business denial must not mutate or destroy executability. A replacement policy gives hosts one explicit gate without introducing an ordered policy chain.

**Alternatives considered**:

- Persist a deny flag on `WorkflowExecutable`: rejected because it changes immutable behavior to express environment policy.
- Reuse source-reference retirement for all denial: rejected because denial can target retained child starts as well as root publication starts.
- Multiple DI policies: rejected because additive fan-in would require another event phase and no current requirement needs it.

## Decision 9: Carry typed dispatch depth end to end

**Decision**: Add `DispatchNestingDepth` to start request, serialized start command, checkpoint command payload, workflow execution state, dispatch record, and child-start payload. Roots and missing legacy values default to 0. Dispatch computes child depth once as parent + 1; replay carries the stored value. The default positive maximum is 32.

**Rationale**: Definition/version identity cannot safely detect version-skewed recursion. A typed durable depth works across deferred delivery and replay and cannot be forged through generic metadata.

**Alternatives considered**:

- Track depth in metadata: rejected because metadata is caller-controlled and not a typed invariant.
- Increment in `ChildStartExecutor`: rejected because redelivery could inflate depth.
- Reject repeated definition IDs: rejected because newer-to-older same-definition dispatch is explicitly legal.

## Decision 10: Reject exact artifact cycles and tolerate version skew

**Decision**: Compilation loads and validates every child dependency graph using full artifact ID/hash pairs, rejects malformed repeated identities, computes the candidate identity, and defensively rejects when that full identity appears in its closure. Normal content-addressed publication forms a DAG; tests construct malformed stored graphs rather than pretending a normal direct self-cycle is authorable. Definition IDs do not participate in cycle identity.

**Rationale**: Exact content cycles are invalid; repeated workflow definitions with different artifact identities may be intentional and are bounded by runtime depth.

**Alternatives considered**:

- Reject any repeated definition ID: rejected because it forbids legal version-skewed calls.
- Rely only on runtime depth: rejected because known exact cycles should never be published.
- Ignore malformed stored dependency graphs: rejected because publication and retention would no longer be deterministic.

## Decision 11: Treat resolution-time validity as the publication pin

**Decision**: Child liveness and tenant visibility are checked when compile contribution resolves the target. Parent activation acquires leases for the exact resolved dependency closure; source replacement or unpublication between resolution and activation never retargets the parent and does not invalidate the exact pin.

**Rationale**: Deterministic dispatch requires immutability after selection. Requiring the historical source to remain live at activation would recreate the retained-pin failure #677 removes. Closure leases linearize artifact lifetime, while source liveness remains the selection gate.

**Alternatives considered**:

- Re-resolve “latest Published” at activation: rejected because it silently changes parent behavior.
- Reject when the source changes after resolution: rejected because source lifecycle is intentionally separate from artifact lifetime.

## Decision 12: Define current accessibility as publication tenant scope

**Decision**: Add optional tenant scope to the compile request and runtime source reference. Dispatch target resolution requires the candidate reference to be visible in the same tenant scope under the current publication access context. This repository exposes no author/role identity on `PublishWorkflow`; richer ACL authorization remains a separate Publishing surface.

**Rationale**: Tenant is the access dimension the current API and persistence access context can enforce honestly. Inventing an author identity inside DispatchWorkflow would be security theater.

**Alternatives considered**:

- Claim ambient access without a model: rejected as untestable.
- Add a DispatchWorkflow-only user/role system: rejected because authorization belongs to Publishing/persistence, not an activity.
