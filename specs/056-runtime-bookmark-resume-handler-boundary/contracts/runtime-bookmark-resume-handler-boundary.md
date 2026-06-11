# Contract: Runtime Bookmark Resume Handler Boundary

## Scheduler Work Handler

`WorkflowResumeBookmarkSchedulerWorkHandler` handles `WorkflowExecutionCommandKind.ResumeBookmark`.

It consumes `RuntimeResumeBookmarkCommandPayload`, loads the pinned executable artifact and activity execution state, constructs the activity, invokes the matching `[ResumeTarget("<id>")]` method, and enqueues `CompleteActivity` work.

## Supported Handler Signatures

- `void Handler()`
- `Task Handler()`
- `ValueTask Handler()`
- `void Handler(IActivityExecutionContext context)`
- `Task Handler(IActivityExecutionContext context)`
- `ValueTask Handler(IActivityExecutionContext context)`
- `void Handler(JsonElement input)`
- `Task Handler(JsonElement input)`
- `ValueTask Handler(JsonElement input)`

Any other signature faults the activity execution state.
