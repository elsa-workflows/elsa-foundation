# Data Model: Runtime Pipeline Slots

## RuntimePipelineSlotDefinition

Named stable slot with a deterministic sort order. Slot names are stable extension contracts; sort order is an implementation detail for plan resolution.

## RuntimePipelineMiddlewareRegistration

Provider/module registration describing:

- Pipeline kind.
- Middleware type.
- Display name.
- Slot name.
- Order within the slot.
- Registration index.
- Built-in/custom status.

## RuntimePipelinePlan

Inspectable resolved plan for one pipeline kind. Contains the ordered steps after slot/order/index sorting.

## RuntimePipelinePlanStep

Resolved step with slot definition, middleware type, order, registration index, and built-in/custom status.

## WorkflowRuntimePipelineContext

Workflow pipeline context. Carries workflow execution state and optional scheduler state.

## ActivityRuntimePipelineContext

Activity pipeline context. Carries workflow execution state, activity execution state, and optional scheduler state.
