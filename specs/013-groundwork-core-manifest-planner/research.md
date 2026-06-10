# Research: Groundwork Core Manifest And Planner Kernel

## Decision: G1 uses three generic source projects

**Rationale**: `Groundwork.Core` owns shared manifest, capability, validation, and materialization concepts. `Groundwork.Relational` and `Groundwork.Documents` are separate so relational and document planning can evolve without forcing consumers to reference both planning models.

**Alternatives considered**:

- Put all concepts in `Groundwork.Core`: rejected because relational/document planning concerns would crowd the kernel and make dependency selection less precise.
- Create provider packages immediately: rejected because G1 must prove provider-neutral planning before provider-specific rendering.

## Decision: G1 has no concrete provider dependencies

**Rationale**: Provider dependencies would make the manifest vocabulary drift toward one database family. G1 should prove that one manifest can produce relational and document plans without binding to SQLite, SQL Server, PostgreSQL, or MongoDB.

**Alternatives considered**:

- Start with SQLite DDL proof: deferred to G2 so G1 remains a kernel slice.
- Start with MongoDB collections: deferred to G5 after the portable document contract is stable.

## Decision: Manifest validation returns structured results

**Rationale**: Hosts and provider packages need to report clear errors and warnings. Exceptions alone are not enough for preview/materialization diagnostics, and boolean validation loses the details needed for operator feedback.

**Alternatives considered**:

- Throw on first error: rejected because manifest authors need all actionable diagnostics.
- Return only strings: rejected because later diagnostics need stable codes/severity.

## Decision: Workload classification is explicit

**Rationale**: G0 identified over-unification as a core risk. The manifest must declare workload family and candidate category so operational streams and runtime continuation state do not silently become ordinary documents.

**Alternatives considered**:

- Infer workload from storage unit type: rejected because it hides architecture decisions in naming.
- Leave workload classification to providers: rejected because invalid defaults would reach planning too late.

## Decision: Capability validation is separate from planning

**Rationale**: A host should be able to preview whether a provider can support a manifest before generating or applying materialization operations. Keeping compatibility separate also makes unsupported capability tests simpler and provider-independent.

**Alternatives considered**:

- Let planners perform capability validation implicitly: rejected because partial plans could hide blockers.
- Skip capability reports until concrete providers: rejected because provider packages need a contract to implement.

## Decision: Tests use sample manifests rather than Elsa stores

**Rationale**: Sample manifests prove the generic kernel without importing Elsa concepts. Elsa stores become validation inputs in G3 after the provider-neutral kernel and SQLite document store are available.

**Alternatives considered**:

- Use Secrets as the first sample: rejected for G1 because it introduces Elsa-specific vocabulary too early.
- Use workflow runtime state as the first sample: rejected because runtime state remains benchmark-gated.
