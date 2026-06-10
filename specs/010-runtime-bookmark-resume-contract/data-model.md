# Data Model: Runtime Bookmark Resume Contract

## BookmarkState

Durable runtime resume handle owned by one activity execution.

Fields:

- `BookmarkId`
- `WorkflowExecutionId`
- `ActivityExecutionId`
- `ExecutableNodeId`
- `ResumeTargetId`
- `StimulusType`
- `StimulusHash`
- `Payload`
- `Metadata`
- `CreatedAt`
- `ExpiresAt`

`ResumeTargetId` is the durable contract. C# callback method names and delegates are not persisted.

## Activity Resume Target Declaration

Activity authors can mark runtime handlers with a stable resume target ID. The declaration is not a bookmark and does not persist a method name.

## BookmarkResumeRequest

Pure resolver input containing the workflow execution state, pinned executable artifact, bookmark state, and optional resume input payload.

## BookmarkResumeResolution

Pure resolver output containing the bookmark, executable node, executable resume target, and optional resume input payload.

## RuntimeCheckpointStateChangeSet.Bookmarks

Typed bookmark state changes included in checkpoint commit envelopes. The full bookmark store/index remains a later slice.
