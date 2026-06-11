# Feature Specification: Groundwork Elsa Bridge

**Feature Branch**: `codex/groundwork-elsa-bridge`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Groundwork Elsa bridge and first opt-in module validation. Add an Elsa.Persistence.Groundwork integration package that can register Groundwork manifests and providers, materialize startup plans through Elsa startup tasks, expose diagnostics for registered manifests/providers/materialization status, and validate a low-risk Secrets-like module manifest through tests without adding Elsa concepts to Groundwork packages."

## User Scenarios & Testing

### User Story 1 - Register Groundwork In Elsa (Priority: P1)

An Elsa integrator can opt into Groundwork persistence by enabling an Elsa bridge package and registering one or more Groundwork manifests.

**Why this priority**: Without an Elsa bridge, Groundwork remains a standalone library and cannot validate the integration boundary promised by G3.

**Independent Test**: Configure the bridge in an `IServiceCollection`, register a manifest, build the provider, and verify bridge options plus diagnostics expose the manifest without adding Elsa references to Groundwork packages.

**Acceptance Scenarios**:

1. **Given** a service collection, **When** the bridge registration is configured with a manifest, **Then** the manifest is available to bridge services through options.
2. **Given** generic Groundwork packages, **When** bridge services are added, **Then** Groundwork packages still do not reference Elsa projects.

---

### User Story 2 - Materialize Groundwork At Elsa Startup (Priority: P2)

An Elsa application can register provider adapters and let an Elsa startup task materialize configured Groundwork manifests.

**Why this priority**: Startup materialization proves Groundwork can participate in Elsa hosting without EF provider-specific migrations for the Groundwork-backed path.

**Independent Test**: Register a SQLite provider adapter and a Secrets-like manifest, run the startup task, and verify the provider schema history exists.

**Acceptance Scenarios**:

1. **Given** bridge options with materialization enabled, **When** the startup task runs, **Then** each registered provider materializes each registered manifest.
2. **Given** materialization is disabled, **When** the startup task runs, **Then** no provider materialization is attempted and diagnostics report a skipped state.

---

### User Story 3 - Inspect Groundwork Diagnostics (Priority: P3)

An Elsa operator or test can inspect what manifests, providers, and materialization attempts are currently known to the bridge.

**Why this priority**: Diagnostics make the opt-in bridge explainable before any production module migration depends on it.

**Independent Test**: Resolve the diagnostics service before and after startup materialization and verify it reports registered manifests, providers, and status for each attempted manifest/provider pair.

**Acceptance Scenarios**:

1. **Given** registered manifests and providers, **When** a diagnostics snapshot is requested, **Then** the snapshot lists their identities.
2. **Given** startup materialization succeeds, **When** a diagnostics snapshot is requested, **Then** the snapshot contains a succeeded materialization record.
3. **Given** startup materialization is disabled, **When** diagnostics are inspected after startup, **Then** the snapshot contains skipped materialization records.

### Edge Cases

- No manifests registered: startup task should complete without provider calls and diagnostics should show no materialization attempts.
- No providers registered: startup task should not fail; diagnostics should make the missing provider condition visible.
- Provider failure: startup task should record the failed materialization before rethrowing.
- Duplicate bridge registration: service registration should remain deterministic and should not duplicate singleton diagnostics services.

## Requirements

### Functional Requirements

- **FR-001**: Add an `Elsa.Persistence.Groundwork` project under the Elsa persistence tree.
- **FR-002**: The bridge MUST register Groundwork manifests through Elsa service configuration.
- **FR-003**: The bridge MUST register provider adapters without referencing provider-specific packages from the bridge project.
- **FR-004**: The bridge MUST expose an Elsa startup task that materializes configured manifests when enabled.
- **FR-005**: The startup task MUST skip materialization when disabled by bridge options.
- **FR-006**: The startup task MUST record succeeded, skipped, unavailable-provider, and failed materialization statuses.
- **FR-007**: The bridge MUST expose a diagnostics service with registered manifests, registered providers, and materialization records.
- **FR-008**: Add a Secrets-like Elsa manifest factory in the bridge or tests as the first low-risk validation target.
- **FR-009**: Tests MUST prove the Secrets-like manifest can be materialized by SQLite through the bridge.
- **FR-010**: Tests MUST prove bridge registration does not add Elsa references to Groundwork packages.
- **FR-011**: The bridge MUST be opt-in and must not replace existing EF persistence paths.

### Key Entities

- **Groundwork Bridge Options**: Elsa-side configuration containing manifests and materialization behavior.
- **Groundwork Provider Adapter**: Elsa-side adapter that names a Groundwork provider and can materialize a manifest.
- **Groundwork Startup Task**: Elsa startup task that coordinates configured manifests and provider adapters.
- **Groundwork Diagnostics Snapshot**: Read model listing manifests, providers, and materialization outcomes.
- **Secrets-like Manifest**: Low-risk Elsa validation manifest stored as Groundwork documents and indexes.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A test resolves bridge options and diagnostics after service registration with at least one manifest.
- **SC-002**: A test runs the startup task with SQLite and verifies schema history for the Secrets-like manifest.
- **SC-003**: A test verifies disabled materialization produces skipped diagnostics without touching SQLite schema.
- **SC-004**: Architecture tests verify Groundwork projects remain Elsa-free after the bridge is added.

## Assumptions

- The bridge package may reference Elsa hosting/task abstractions and Groundwork contracts.
- Provider-specific adapter implementations can live outside the bridge project; G3 tests can provide the SQLite adapter.
- There is no existing Secrets module in this repo, so G3 validates a Secrets-like manifest instead of migrating production Secrets code.
- HTTP diagnostics endpoints are out of scope for G3; diagnostics are exposed as injectable services.
