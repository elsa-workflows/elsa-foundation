# Feature Specification: Runtime Value Binding Contract

**Feature Branch**: `codex/runtime-value-binding-contract`
**Created**: 2026-06-10
**Input**: Slice 6 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Runtime Nodes Carry Typed Input Bindings (Priority: P1)

Runtime can represent compiled input bindings for executable nodes without loading authored data links or evaluated input state.

**Independent Test**: Create an executable node with literal, expression, activity-output, durable-value, and reference bindings using only Runtime.Core contracts.

### User Story 2 - Activity Outputs Are Active-Scope Values (Priority: P1)

Runtime can publish and consume an activity output by `ActivityExecutionId` while the output is in the active execution scope.

**Independent Test**: Record an output for one activity execution, resolve a downstream input binding that names that activity execution, and assert the value comes from the active output register.

### User Story 3 - Durable Capture Is Explicit (Priority: P1)

Runtime can represent output capture into declared durable values and reject output bindings that try to cross suspension or ambiguous execution scopes without explicit semantics.

**Independent Test**: Validate an activity-output binding marked as crossing a suspension boundary and assert a diagnostic requiring durable value capture; validate an ambiguous loop/parallel binding and assert a diagnostic instead of picking an arbitrary output.

## Requirements

- **FR-001**: Runtime.Core MUST define a typed `RuntimeInputBinding` model with literal, expression, activity-output, durable-value, and reference sources.
- **FR-002**: Runtime.Core MUST define output capture declarations that target declared durable values rather than creating a separate durable activity-output store.
- **FR-003**: `ExecutableNode` MUST use typed input binding and output capture contracts instead of opaque JSON binding placeholders.
- **FR-004**: Runtime.Core MUST define an active activity output register keyed by `WorkflowExecutionId`, `ActivityExecutionId`, and output name.
- **FR-005**: Runtime.Core MUST define a pure input binding resolver for literal values, references, durable values, and active activity outputs.
- **FR-006**: Activity-output binding resolution MUST require a concrete producer `ActivityExecutionId`; ambiguous output references MUST fail with a runtime binding diagnostic or exception.
- **FR-007**: Binding validation MUST report that output references crossing suspension require explicit durable value capture.
- **FR-008**: Runtime input binding contracts MUST NOT introduce Design-owned workflow document dependencies or history/audit output reads.

## Out of Scope

- Full expression engine execution.
- Full scheduler data-dependency behavior.
- Full compile/publish pipeline from authored data links.
- Durable value storage providers beyond the existing state contract.
- History/audit projection or audit-safe output recording.

## Success Criteria

- **SC-001**: Tests prove executable nodes carry typed runtime bindings and captures.
- **SC-002**: Tests prove same-scope output-to-input resolution uses `ActivityExecutionId`.
- **SC-003**: Tests prove ambiguous or cross-suspension output references are rejected unless represented as durable value bindings.
- **SC-004**: Runtime and architecture tests continue to pass with no Design dependency.
