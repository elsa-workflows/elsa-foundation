# Feature Specification: Runtime Elsa 3 Migration Boundary

**Feature Branch**: `codex/runtime-elsa3-migration-boundary`
**Created**: 2026-06-10
**Input**: Slice 9 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Elsa 3 Definitions Import Through A Bounded Adapter (Priority: P1)

Elsa 4 can accept known Elsa 3 authored workflow definition shapes through an explicit import boundary that produces Elsa 4 design entities or diagnostics.

**Independent Test**: Create a definition import input from an Elsa 3 workflow definition and assert the input shape is accepted for definition migration.

### User Story 2 - Elsa 3 Live Instance Resume Is Explicitly Unsupported (Priority: P1)

Elsa 4 must not accidentally treat persisted Elsa 3 runtime `WorkflowState` or workflow instance payloads as Elsa 4 runtime continuation state.

**Independent Test**: Ask the compatibility boundary to reject an Elsa 3 workflow instance state input and assert it returns an error diagnostic with cutover guidance.

### User Story 3 - Runtime Remains Free Of Elsa 3 Compatibility Dependencies (Priority: P1)

Elsa 3 compatibility lives in adapter/import modules and does not become an execution-time dependency of `Elsa.Workflows.Runtime.*`.

**Independent Test**: Architecture guard verifies runtime projects do not reference `Elsa3.*` projects.

## Requirements

- **FR-001**: `Elsa3.Models` MUST define accepted Elsa 3 migration input kinds for authored workflow definitions.
- **FR-002**: `Elsa3.Models` MUST define diagnostics and result contracts for migration/import failures and warnings.
- **FR-003**: Elsa 3 workflow instance/runtime state inputs MUST be rejected with explicit diagnostics and guidance.
- **FR-004**: `Elsa3.Mapping` MUST expose a workflow definition importer boundary that returns diagnostics instead of requiring callers to interpret arbitrary mapper exceptions.
- **FR-005**: Runtime projects MUST NOT reference `Elsa3.*` compatibility/import projects.
- **FR-006**: The slice MUST document that Elsa 3 live instance resume is out of scope.

## Out of Scope

- Full Elsa 3 JSON file reader.
- Full migration CLI/tooling.
- Elsa 3 persisted workflow instance resume.
- Elsa 3 execution log/bookmark/queue migration.
- Runtime dual-read or side-by-side Elsa 3/Elsa 4 execution.

## Success Criteria

- **SC-001**: Tests prove authored definition import input kinds are accepted and live instance inputs are rejected.
- **SC-002**: Tests prove migration results have clear success/failure diagnostic invariants.
- **SC-003**: Architecture tests prove runtime projects do not depend on `Elsa3.*`.
- **SC-004**: Existing runtime and architecture tests continue to pass.
