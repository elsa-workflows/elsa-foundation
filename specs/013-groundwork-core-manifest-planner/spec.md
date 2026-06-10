# Feature Specification: Groundwork Core Manifest And Planner Kernel

**Feature Branch**: `codex/groundwork-core-manifest-planner`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Define the G1 Groundwork core manifest and planner kernel: generic storage manifests, storage units, workload classifications, index declarations, provider capabilities, materialization plans, schema history contracts, relational planning, document planning, and validation tests with no Elsa package references."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Declare Provider-Neutral Storage Intent (Priority: P1)

A framework or application author can define a storage manifest that describes storage intent in generic Groundwork terms, including storage units, workload classification, lifecycle, identity, tenancy, concurrency, serialization, indexes, query contract, and schema version.

**Why this priority**: Provider and application integration work cannot proceed safely until storage intent has a stable, provider-neutral vocabulary.

**Independent Test**: Create a sample manifest for a metadata/configuration document and verify it can be validated without choosing SQLite, SQL Server, PostgreSQL, MongoDB, EF Core, or Elsa-specific concepts.

**Acceptance Scenarios**:

1. **Given** a valid manifest with one document storage unit and declared indexes, **When** the manifest is validated, **Then** validation succeeds and exposes normalized storage intent for planners.
2. **Given** a manifest that uses provider-specific physical names as required contract fields, **When** the manifest is validated, **Then** validation fails with a clear provider-neutrality error.
3. **Given** a manifest that omits workload classification or schema version, **When** validation runs, **Then** validation fails with the missing requirement.

---

### User Story 2 - Produce Relational And Document Plans From The Same Manifest (Priority: P2)

A provider author can consume the same validated manifest through relational and document planning contracts and produce provider-neutral plan descriptions for each storage model.

**Why this priority**: The central promise of Groundwork is that modules declare intent once while provider families translate that intent into their own physical strategy.

**Independent Test**: Use the same sample manifest to produce both a relational plan and a document plan, then verify both plans preserve workload, index, concurrency, and schema-history requirements.

**Acceptance Scenarios**:

1. **Given** a validated manifest with declared indexes, **When** the relational planner runs, **Then** it produces a table/index-oriented plan without binding to a concrete SQL dialect.
2. **Given** the same validated manifest, **When** the document planner runs, **Then** it produces an envelope/index-oriented plan without binding to a concrete document provider.
3. **Given** a manifest with a workload classification unsupported by a planner, **When** planning runs, **Then** the planner reports an unsupported capability instead of creating a partial plan silently.

---

### User Story 3 - Validate Provider Capabilities Before Materialization (Priority: P3)

A host or provider integrator can compare a manifest with a provider capability report before applying storage changes, and can see required operations, warnings, unsupported requirements, and schema-history expectations.

**Why this priority**: Groundwork must fail clearly when a provider cannot satisfy a manifest, otherwise portability becomes misleading and runtime failures move too late.

**Independent Test**: Compare manifests against compatible and incompatible provider capability reports and verify that the result clearly identifies support, warnings, and blocking gaps.

**Acceptance Scenarios**:

1. **Given** a provider capability report that supports the manifest's workload, indexes, concurrency, and schema history requirements, **When** capability validation runs, **Then** the manifest is marked plannable.
2. **Given** a provider capability report that does not support a required index or concurrency mode, **When** capability validation runs, **Then** the result blocks materialization and names the unsupported requirement.
3. **Given** a provider capability report that supports a fallback with warnings, **When** capability validation runs, **Then** warnings are surfaced without changing required manifest intent.

### Edge Cases

- A manifest with zero storage units is invalid.
- Storage unit identities must be stable and unique within a manifest.
- Index identities must be stable and unique within a storage unit.
- Unindexed portable queries must be represented as unsupported, not as implicit scans.
- Operational workloads cannot be planned by default document/relational planners unless explicitly allowed by capability and policy.
- Provider capability warnings must not downgrade required manifest constraints.
- Schema/materialization history must be represented for every materializable plan.
- Generic Groundwork projects must not reference Elsa projects or use Elsa domain names in public contracts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Groundwork MUST provide a generic storage manifest model that contains manifest identity, version, owner identity, storage units, required capabilities, and compatibility notes.
- **FR-002**: Groundwork MUST provide a generic storage unit model that contains unit identity, workload classification, lifecycle policy, identity policy, optional tenancy/partition policy, concurrency policy, serialization policy, declared indexes, query contract, and physicalization policy.
- **FR-003**: Groundwork MUST provide a workload classification model that can distinguish Groundwork-default, physicalization-required, benchmark-gated, and specialized-provider candidates.
- **FR-004**: Groundwork MUST validate manifests before planning and return structured validation results that include errors and warnings.
- **FR-005**: Groundwork MUST reject manifests that require provider-specific physical database shape in generic contract fields.
- **FR-006**: Groundwork MUST provide provider capability reports covering supported workload families, index shapes, query operations, concurrency modes, materialization operations, and schema-history support.
- **FR-007**: Groundwork MUST compare validated manifests with provider capability reports and produce structured compatibility results.
- **FR-008**: Groundwork MUST define provider-neutral materialization plan concepts with plan identity, target provider identity, manifest version, planned operations, diagnostics, and schema-history entry requirements.
- **FR-009**: Groundwork MUST define schema/materialization history concepts so applied manifest versions can be recorded by later provider implementations.
- **FR-010**: Groundwork MUST define a relational planning contract that produces provider-neutral relational plan operations from validated manifests.
- **FR-011**: Groundwork MUST define a document planning contract that produces provider-neutral document/envelope/index plan operations from validated manifests.
- **FR-012**: Groundwork MUST provide at least one sample manifest that can produce both relational and document plans.
- **FR-013**: Groundwork MUST include tests proving the same sample manifest can validate and produce both relational and document plan descriptions.
- **FR-014**: Groundwork MUST include tests proving unsupported capabilities and unindexed portable queries fail clearly.
- **FR-015**: Groundwork MUST include architecture or project-reference tests proving generic Groundwork projects do not reference Elsa projects.
- **FR-016**: Groundwork MUST not implement concrete SQLite, SQL Server, PostgreSQL, MongoDB, or Elsa bridge behavior in G1.

### Key Entities *(include if feature involves data)*

- **Storage Manifest**: Provider-neutral declaration of storage intent for one product/module/application area.
- **Storage Unit**: Named durable storage concern inside a manifest.
- **Workload Classification**: Behavioral classification that drives allowed default storage strategy and guardrails.
- **Index Declaration**: Stable declaration of an indexed field or field group, including uniqueness, sortability, type expectations, and portable query support.
- **Portable Query Contract**: Declared set of allowed query operations for a storage unit.
- **Provider Capability Report**: Provider-owned statement of supported features, warnings, and unsupported requirements.
- **Capability Compatibility Result**: Structured comparison result between a manifest and provider capabilities.
- **Materialization Plan**: Provider-neutral description of operations needed to prepare storage for a manifest.
- **Schema History Entry**: Provider-neutral record shape for an applied manifest/materialization version.
- **Relational Plan**: Provider-neutral table/index-oriented plan produced from a manifest.
- **Document Plan**: Provider-neutral envelope/index-oriented plan produced from a manifest.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A sample manifest validates successfully and produces both relational and document plan descriptions in automated tests.
- **SC-002**: At least three invalid manifest cases produce structured validation errors in automated tests.
- **SC-003**: At least two unsupported provider capability cases produce blocking compatibility results in automated tests.
- **SC-004**: Automated architecture checks prove generic `Groundwork.*` projects do not reference `Elsa.*` projects.
- **SC-005**: The G1 slice creates no concrete database provider and no Elsa integration package.
- **SC-006**: A reviewer can identify the next slice, G2 SQLite portable document store MVP, from the completed G1 artifacts without reopening Groundwork's product boundary.

## Assumptions

- G0 Groundwork Persistence Foundation is accepted as the product boundary.
- G1 is a buildable kernel slice, not another pure planning slice.
- The generic projects live under `src/Groundwork/` and tests under `tests/Groundwork/`.
- The first implementation targets contracts, validation, provider-neutral plans, and tests only.
- Provider-specific rendering, startup locking, live database integration, and Elsa module migration begin in later slices.
