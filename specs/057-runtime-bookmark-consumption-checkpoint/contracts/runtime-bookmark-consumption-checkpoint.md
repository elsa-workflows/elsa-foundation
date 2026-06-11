# Contract: Runtime Bookmark Consumption Checkpoint

## Service

`IBookmarkConsumptionCheckpointService` commits `BookmarkConsumed` for one matched bookmark resume.

Input:

- `RuntimeSchedulerWorkItem` for `ResumeBookmark`
- `RuntimeResumeBookmarkCommandPayload`
- matched `BookmarkState`
- completed `ActivityExecutionState`

Output:

- checkpoint ID
- commit ID
- persistence decision

## State Changes

The checkpoint commit MUST include:

- one `ActivityExecutionState` `Upsert`
- one `BookmarkState` `Delete`

The checkpoint commit MUST NOT include post-commit intents in this slice.
