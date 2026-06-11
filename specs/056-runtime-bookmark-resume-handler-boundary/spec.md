# Feature Specification: Runtime Bookmark Resume Handler Boundary

**Feature Branch**: `codex/runtime-bookmark-resume-handler-boundary`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after bookmark stimulus resume dispatch. Dispatch now records `ResumeBookmark` scheduler work; the Activities runtime should handle that work through stable resume target IDs without persisting callback method names.

## Scenarios & Tests

1. Given `ResumeBookmark` scheduler work for a suspended activity execution, when the activity type declares a matching `[ResumeTarget]` handler, then the handler is invoked and deterministic completion work is enqueued.
2. Given the activity has no matching resume target handler, then the activity execution faults clearly and no completion work is enqueued.
3. Given the resume payload references a missing executable, node, or activity execution state, then the scheduler work faults clearly.
4. Given the activity execution is already completed, then completion work is re-enqueued idempotently without invoking the handler again.

## Requirements

- **FR-001**: Activities.Runtime MUST contribute a `ResumeBookmark` scheduler work handler before the Workflows.Runtime missing-provider fallback.
- **FR-002**: The handler MUST deserialize `RuntimeResumeBookmarkCommandPayload` and validate pinned executable identity, executable node ID, activity execution ID, and resume target ID.
- **FR-003**: The handler MUST locate an activity method declared with `ResumeTargetAttribute` matching the payload `ResumeTargetId`.
- **FR-004**: Supported handler signatures MUST be explicit and small: no parameters, `IActivityExecutionContext`, or `JsonElement`; return types `void`, `Task`, or `ValueTask`.
- **FR-005**: Successful resume MUST mark the activity execution completed and enqueue deterministic `CompleteActivity` scheduler work.
- **FR-006**: Missing or invalid resume handlers MUST fault the activity execution state instead of falling through to noop.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Bookmark deletion/consumption.
- Durable suspension lifecycle changes.
- Handler descriptor compilation during publishing.
- Method-name persistence or Elsa 3 callback-method migration.
- Complex multi-argument resume handler binding.

## Acceptance Criteria

- Tests prove `[ResumeTarget]` invocation completes activity execution and queues completion work.
- Tests prove no matching handler faults state clearly.
- Tests prove invalid handler signatures fault state clearly.
- Tests prove already completed activity execution requeues completion without invoking handler.
- Focused activity/runtime and architecture tests pass.
