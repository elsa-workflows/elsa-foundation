# Data Model: Groundwork Core Manifest And Planner Kernel

This model describes G1 implementation concepts without prescribing exact C# member names.

## Storage Manifest

Provider-neutral storage intent for one product area or module.

Fields:

- Manifest identity.
- Owner identity.
- Version.
- Storage units.
- Required capabilities.
- Compatibility notes.

Validation rules:

- Identity must be non-empty and stable.
- Version must be present.
- At least one storage unit is required.
- Storage-unit identities must be unique.
- Provider-specific required physical shape is invalid.

## Storage Unit

Named durable storage concern inside a manifest.

Fields:

- Unit identity.
- Display name or description.
- Workload classification.
- Lifecycle policy.
- Identity policy.
- Optional tenancy/partition policy.
- Concurrency policy.
- Serialization policy.
- Index declarations.
- Portable query contract.
- Physicalization policy.

Validation rules:

- Workload classification is required.
- Lifecycle, identity, concurrency, and serialization policies are required.
- Index identities must be unique within the unit.
- Query contract can reference only declared indexes for portable indexed queries.
- Operational workloads require explicit specialized/benchmark-gated classification.

## Workload Classification

Declares the family and allowed Groundwork default category.

Families:

- Metadata/configuration.
- Catalog/authored data.
- Runtime-defined business data.
- Runtime continuation state.
- Operational stream.
- Projection.
- Audit trail.

Candidate categories:

- Groundwork default.
- Groundwork with physicalization.
- Benchmark-gated.
- Specialized provider.

Validation rules:

- Runtime continuation state must be benchmark-gated or explicitly physicalization-required.
- Operational streams default to specialized provider unless later policy permits otherwise.

## Index Declaration

Stable declaration of queryable fields.

Fields:

- Index identity.
- Field paths.
- Value type expectation.
- Uniqueness.
- Sortability.
- Null/missing handling.
- Supported portable operations.

Validation rules:

- Field paths must be non-empty.
- Supported operations must be compatible with the value type expectation.
- Unique indexes must declare how missing values are treated.

## Portable Query Contract

Declares which portable query operations a storage unit supports.

Fields:

- Query identity.
- Target index identity.
- Allowed operations.
- Sort and paging support.

Validation rules:

- Every indexed portable query must reference a declared index.
- Unindexed queries are invalid unless explicitly classified as non-portable/provider-specific.

## Provider Capability Report

Provider-owned statement of supported manifest features.

Fields:

- Provider identity.
- Provider version.
- Supported workload families.
- Supported candidate categories.
- Supported index capabilities.
- Supported query operations.
- Supported concurrency modes.
- Supported materialization operations.
- Schema-history support.
- Warnings.

Validation rules:

- Required manifest capabilities that are unsupported become blocking compatibility results.
- Warnings cannot change manifest intent.

## Capability Compatibility Result

Structured comparison between a manifest and provider capabilities.

Fields:

- Overall status.
- Errors.
- Warnings.
- Unsupported requirements.
- Supported fallback notes.

Validation rules:

- Any unsupported required capability makes the result blocking.
- Non-blocking warnings remain visible to callers.

## Materialization Plan

Provider-neutral description of storage preparation operations.

Fields:

- Plan identity.
- Target provider identity.
- Manifest identity and version.
- Planned operations.
- Diagnostics.
- Required schema-history entry.

Validation rules:

- A plan must be tied to one manifest version.
- Every plan must include schema-history intent.
- Plans cannot silently omit declared indexes.

## Relational Plan

Provider-neutral relational model plan.

Fields:

- Storage units.
- Table-like plan units.
- Column-like plan elements.
- Index-like plan elements.
- History operation.
- Diagnostics.

Validation rules:

- The plan remains SQL-dialect neutral.
- It preserves declared indexes and concurrency expectations.

## Document Plan

Provider-neutral document/envelope/index model plan.

Fields:

- Document storage units.
- Envelope plan.
- Generic index plan.
- Query support plan.
- History operation.
- Diagnostics.

Validation rules:

- The plan remains provider-neutral.
- It preserves declared indexes, query support, and concurrency expectations.
