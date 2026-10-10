# Contract: Manifest Kernel

G1 exposes generic manifest concepts under `Groundwork.Core`.

## Required Capabilities

- Create a storage manifest with one or more storage units.
- Validate manifest identity, version, storage units, policies, indexes, query contract, and workload classification.
- Return structured validation results with severity, code, message, and optional target path.
- Represent provider capability reports.
- Compare manifests to provider capability reports.
- Represent materialization plans and schema-history entries.

## Required Guardrails

1. Generic manifest concepts do not use Elsa domain names.
2. Manifest validation rejects missing workload classification.
3. Manifest validation rejects query contracts that require undeclared indexes.
4. Manifest validation rejects required provider-specific physical shape.
5. Capability validation blocks unsupported required capabilities.
6. Warnings are preserved separately from blocking errors.

## Sample Manifest Contract

G1 must include a sample metadata/configuration document manifest with:

- One storage unit.
- Optimistic concurrency.
- JSON-like serialization policy.
- At least two declared indexes.
- At least one unique index.
- At least two portable query declarations.

The sample must be generic; it must not represent an Elsa secret, activity, workflow, bookmark, or runtime entity.
