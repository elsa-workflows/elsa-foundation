# Research: Groundwork Persistence Foundation

## Decision: Groundwork is a generic product validated by Elsa

**Rationale**: The original Persistence vNext roadmap contains reusable concerns: manifests, provider capabilities, materialization plans, schema history, document envelopes, declared indexes, and diagnostics. Those concerns are not inherently Elsa concepts. Elsa should validate them through real stores, but not define the generic vocabulary.

**Alternatives considered**:

- Keep everything under `Elsa.Persistence.VNext`: rejected because it would make later extraction and reuse harder.
- Build a standalone repository immediately: rejected because the abstractions still need validation against real Elsa workloads.

## Decision: Generic packages use the `Groundwork` root

**Rationale**: `Groundwork` communicates foundational infrastructure and reads well as a package/root namespace prefix. A quick public search did not show an obvious dominant .NET package family using `Groundwork.*`, but a later naming/trademark check remains required before public release.

**Alternatives considered**:

- `Groundwork.Persistence.*`: rejected because persistence is the product domain and the extra segment adds noise.
- Elsa-prefixed generic packages: rejected because they weaken extraction readiness.

## Decision: Workload classification comes before provider implementation

**Rationale**: Metadata documents, catalogs, runtime-defined business data, runtime continuation state, and operational streams have different mutation, query, volume, latency, and consistency profiles. The portable document store should be the default only for the workloads that fit it.

**Alternatives considered**:

- Treat all stores as documents first: rejected because runtime queues, logs, outbox records, and locks would be misclassified.
- Start from relational DDL first: rejected because provider-neutral storage intent would be shaped by one provider family too early.

## Decision: Manifest vocabulary is provider-neutral storage intent

**Rationale**: Modules and applications should describe what they need: workload kind, lifecycle, identity, tenancy, schema version, concurrency, serialization, indexes, query contract, and physicalization preference. Providers decide how to materialize those requirements.

**Alternatives considered**:

- Let modules declare provider-specific DDL or collections: rejected because it duplicates provider knowledge and prevents portable planning.
- Keep manifests minimal until providers exist: rejected because provider work would otherwise invent inconsistent contract fragments.

## Decision: Elsa integration is a bridge, not part of Groundwork

**Rationale**: Elsa needs feature registration, module discovery, startup materialization, diagnostics, and store repositories. Those are application integration concerns. They should consume Groundwork contracts and map Elsa concepts onto generic storage units.

**Alternatives considered**:

- Put Elsa module discovery inside Groundwork.Hosting: rejected because it would leak Elsa's modularity model into the generic framework.
- Let Elsa stores call provider packages directly: rejected because it bypasses the generic validation and planning layer.

## Decision: Runtime continuation state is benchmark-gated

**Rationale**: Runtime checkpoint commits have atomicity, concurrency, and post-commit intent semantics that are different from ordinary document save/load/query flows. Groundwork may be a candidate only after provider contracts and benchmark evidence prove it can meet the runtime contract.

**Alternatives considered**:

- Include workflow runtime stores in the first migration wave: rejected because it would create a hot-path risk before provider behavior is proven.
- Exclude runtime forever: rejected because some runtime metadata/state categories may fit Groundwork after physicalization.
