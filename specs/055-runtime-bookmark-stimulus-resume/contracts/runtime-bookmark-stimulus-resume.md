# Contract: Runtime Bookmark Stimulus Resume Dispatch

## `IBookmarkStimulusLookup`

```csharp
ValueTask<BookmarkStimulusLookupResult> FindAsync(
    BookmarkStimulusLookupRequest request,
    CancellationToken cancellationToken = default);
```

Finds a non-expired bookmark for one workflow execution and stimulus type/hash. No match and ambiguous matches are explicit statuses.

## `IBookmarkResumeDispatcher`

```csharp
ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
    BookmarkResumeDispatchRequest request,
    CancellationToken cancellationToken = default);
```

Loads workflow execution state, loads the pinned executable artifact, resolves the bookmark through `BookmarkResumeResolver`, and enqueues a `ResumeBookmark` command through `IWorkflowExecutionAgentProvider`.

## Resume Command Payload

The payload carries:

- `BookmarkId`
- `ActivityExecutionId`
- `ExecutableNodeId`
- `ResumeTargetId`
- `StimulusType`
- `StimulusHash`
- optional input

It does not carry C# callback method names.
