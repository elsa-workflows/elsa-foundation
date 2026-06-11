# Contract: Runtime Bookmark State Projection

`InMemoryRuntimeCheckpointWriter` can project `RuntimeCheckpointCommit.StateChanges.Bookmarks` into `IBookmarkStateStore`.

## Store Boundary

`IBookmarkStateStore` stores bookmark continuation state by workflow execution ID and bookmark ID.

The minimal operations are:

- save a `BookmarkState`;
- delete a bookmark by workflow execution ID and bookmark ID;
- find a bookmark by workflow execution ID and bookmark ID;
- list bookmark states for a workflow execution.

Stimulus lookup and resume dispatch indexes are out of scope for this slice.

## Projection Rule

When the writer is constructed with an `IBookmarkStateStore`, a newly accepted checkpoint commit projects each bookmark state change into the store before the write record is added.

Supported operations:

- `RuntimeStateChangeOperation.Upsert`: save or replace the bookmark state.
- `RuntimeStateChangeOperation.Delete`: remove the bookmark state by workflow execution ID and bookmark ID.

## Invariants

- Duplicate commit IDs return without validation or projection.
- Projection is serialized by the writer gate.
- `StateId` must equal `BookmarkState.BookmarkId`.
- `BookmarkState.WorkflowExecutionId` must equal the checkpoint workflow execution ID.
- If validation or projection fails, the write is not recorded.
