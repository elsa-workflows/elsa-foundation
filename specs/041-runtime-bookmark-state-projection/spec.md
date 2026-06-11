# Feature Specification: Runtime Bookmark State Projection

**Feature Branch**: `codex/runtime-bookmark-state-projection`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after activity execution state projection. Checkpoint commits already carry bookmark state changes; the default in-memory writer should project those changes into a bookmark state store without implementing the full bookmark lookup index or resume dispatcher.

## Scenarios & Tests

1. Given a checkpoint commit with a bookmark state upsert, when the in-memory checkpoint writer accepts the commit, then the bookmark state is saved in the bookmark state store.
2. Given a later checkpoint commit for the same bookmark with a delete operation, when it is accepted, then the bookmark is removed from the store.
3. Given the same commit ID is replayed with conflicting bookmark state, then projection remains idempotent and does not overwrite the first accepted state.
4. Given an unsupported bookmark state operation is supplied for projection, then the writer rejects the commit before recording it.

## Requirements

- **FR-001**: Runtime.Core MUST define `IBookmarkStateStore` as the minimal continuation-state boundary for bookmark state.
- **FR-002**: Runtime.Core MUST provide an in-memory bookmark state store for the current default runtime composition.
- **FR-003**: The default in-memory checkpoint writer MUST project bookmark state upserts and deletes into `IBookmarkStateStore`.
- **FR-004**: Bookmark state projection MUST remain commit-ID idempotent.
- **FR-005**: Bookmark state projection MUST validate bookmark state operation, `StateId`, and checkpoint workflow execution ID before recording a write.
- **FR-006**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Full durable bookmark lookup/index implementation.
- Bookmark stimulus matching or resume dispatch behavior.
- Wait registration creation from activity execution.
- Durable database/provider implementation.
- Full checkpoint replay/recovery scanner.

## Acceptance Criteria

- `InMemoryRuntimeCheckpointWriter` can project bookmark state upserts and deletes into `IBookmarkStateStore`.
- Duplicate commit IDs do not reapply conflicting bookmark state.
- Unsupported or mismatched bookmark state changes are rejected before write recording.
- The API feature registers the in-memory bookmark store before resolving the checkpoint writer.
- Focused runtime and architecture tests pass.
