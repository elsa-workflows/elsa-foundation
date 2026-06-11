# Contract: Runtime Bookmark Creation Checkpoint

## Scheduler Command

`WorkflowCreateBookmarkSchedulerWorkHandler` handles `WorkflowExecutionCommandKind.CreateBookmark`.

Payload:

- pinned executable identity
- bookmark ID
- activity execution ID
- executable node ID
- resume target ID
- stimulus type/hash
- optional bookmark payload
- optional expiry
- reason

## Checkpoint

The handler commits `RuntimeCheckpointNames.BookmarkCreated` with:

- one `ActivityExecutionState` `Upsert` with status `Suspended`
- one `BookmarkState` `Upsert`
- no post-commit intents

Completion propagation work is not enqueued.
