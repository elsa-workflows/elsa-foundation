# Data Model: Groundwork Persistence Foundation

This model defines G0 planning concepts. It is intentionally provider-neutral and does not define C# API shapes.

## Groundwork Product

The reusable persistence framework. It owns generic vocabulary for storage intent, planning, materialization, schema history, provider capabilities, portable document storage, and diagnostics.

Validation rules:

- Must not depend on Elsa packages.
- Must not name Elsa domain concepts.
- Must be extractable from the repository without renaming generic concepts.

## Groundwork Kernel

The generic layer that validates manifests and coordinates provider-independent contracts.

Responsibilities:

- Manifest model.
- Storage unit model.
- Workload classification.
- Capability validation.
- Materialization plan contracts.
- Schema/materialization history contracts.
- Diagnostics contracts.

Non-responsibilities:

- Elsa module discovery.
- Elsa feature registration.
- Provider-specific DDL or native index creation.
- Domain repositories for Elsa modules.

## Storage Manifest

A provider-neutral declaration of storage intent for one module, product area, or application integration.

Required conceptual fields:

- Manifest identity.
- Owning application/module identity.
- Manifest version.
- Storage units.
- Required capabilities.
- Compatibility and migration notes.

Validation rules:

- Must contain at least one storage unit.
- Must use generic workload and storage vocabulary.
- Must declare versioning and materialization expectations.

## Storage Unit

A named durable unit within a manifest.

Required conceptual fields:

- Unit identity.
- Workload classification.
- Lifecycle policy.
- Identity policy.
- Tenancy/partition policy when applicable.
- Concurrency policy.
- Serialization policy.
- Declared indexes.
- Portable query contract.
- Physicalization policy.

Validation rules:

- Operational workloads cannot silently default to ordinary document storage.
- Unindexed portable queries must be rejected by contract.
- Provider-specific physicalization preferences must remain optional preferences, not required provider shape.

## Workload Classification

The declared workload family and behavioral profile for a storage unit.

Initial families:

- Metadata/configuration.
- Catalog/authored data.
- Runtime-defined business data.
- Runtime continuation state.
- Operational stream.
- Projection.
- Audit trail.

Classification outputs:

- Groundwork default candidate.
- Groundwork candidate with physicalization.
- Benchmark-gated candidate.
- Specialized-provider candidate.

## Provider Capability Report

A provider-owned report that states whether a manifest can be planned and materialized.

Required conceptual fields:

- Provider identity and version.
- Supported workload families.
- Supported index types.
- Supported concurrency modes.
- Supported materialization operations.
- Unsupported requirements.
- Warnings and fallbacks.

Validation rules:

- Unsupported manifest requirements produce clear failures unless an explicit fallback is accepted.
- Provider differences remain isolated to provider packages.

## Materialization Plan

A provider-specific plan generated from provider-neutral manifests.

Required conceptual fields:

- Plan identity.
- Target provider.
- Manifest version.
- Planned operations.
- Required lock/materialization mode.
- Preview diagnostics.
- History entry shape.

Validation rules:

- Plans must be previewable before apply.
- Applied versions must be recorded in schema/materialization history.
- Plans must not require Elsa-specific context.

## Elsa Validation Bridge

The Elsa-side integration that validates Groundwork through real Elsa stores.

Responsibilities:

- Discover Elsa-owned manifests.
- Register Groundwork providers inside Elsa shell composition.
- Run startup materialization tasks.
- Expose Elsa diagnostics/status.
- Implement Elsa repositories over Groundwork stores.

Non-responsibilities:

- Defining generic manifest vocabulary.
- Owning provider-specific Groundwork implementation logic.
- Moving runtime hot paths before benchmark evidence exists.
