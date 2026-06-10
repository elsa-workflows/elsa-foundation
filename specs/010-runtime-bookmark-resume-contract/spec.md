# Feature Specification: Runtime Bookmark Resume Contract

**Feature Branch**: `codex/runtime-bookmark-resume-contract`
**Created**: 2026-06-10
**Status**: Draft
**Input**: Slice 5 from `docs/reports/elsa-4-runtime-execution-action-plan.md`

## User Scenarios & Testing

### User Story 1 - Persist Durable Bookmark Resume Handles (Priority: P1)

Runtime can represent a durable bookmark for a concrete activity execution without storing a C# callback method name.

**Independent Test**: Create a bookmark for a workflow execution, activity execution, executable node, stimulus type/hash, and resume target ID, then assert the bookmark contains no callback method field and can be included in a checkpoint state-change envelope.

### User Story 2 - Resolve Resume Through Pinned Artifact (Priority: P1)

Runtime can resolve a bookmark by using the workflow execution's pinned executable artifact and the bookmark's `ResumeTargetId`.

**Independent Test**: Given a workflow execution pinned to an executable artifact with a resume-target table, resolve a bookmark and assert the returned executable node and resume target come from that pinned artifact.

### User Story 3 - Report Missing Resume Targets Clearly (Priority: P1)

Runtime reports artifact/runtime compatibility failures when a bookmark refers to a missing or inconsistent resume target.

**Independent Test**: Resolve a bookmark whose `ResumeTargetId` is absent from the artifact and assert a runtime resume-target resolution exception, not a design deserialization error or callback method lookup.

## Requirements

- **FR-001**: Runtime.Core MUST define `BookmarkState` with workflow execution ID, activity execution ID, executable node ID, resume target ID, stimulus type/hash, payload, metadata, creation time, and optional expiration.
- **FR-002**: `BookmarkState` MUST store `ResumeTargetId` and MUST NOT store C# method names, delegates, or callback identifiers as durable resume data.
- **FR-003**: Runtime checkpoint state changes MUST support typed bookmark state changes.
- **FR-004**: Activities.Runtime.Core MUST expose a resume target declaration contract that activity authors can use without referencing Workflow Design models.
- **FR-005**: Runtime.Core MUST define a bookmark resume resolver that maps `BookmarkState.ResumeTargetId` through the pinned executable artifact's resume-target table.
- **FR-006**: Resume resolution MUST verify the workflow execution is pinned to the executable artifact being used for resolution.
- **FR-007**: Missing or mismatched resume targets MUST produce a clear runtime resolution exception.
- **FR-008**: Runtime.Core MUST remain free of Design-owned authored workflow model dependencies.

## Out Of Scope

- Full bookmark persistence store or lookup index.
- Full resume command execution.
- Full external stimulus matching service.
- Full activity handler invocation.
- Global unmatched-stimulus inbox.
- Elsa 3 callback-method bookmark migration.

## Success Criteria

- Tests prove bookmark state carries durable lookup fields and no callback method contract.
- Tests prove resume resolves through the pinned executable artifact's resume-target table.
- Tests prove missing resume targets produce a clear runtime exception.
