# Contract: Runtime Activity Bookmark Request

## Activity Context

`IActivityExecutionContext.CreateBookmark(ActivityBookmarkRequest request)` records a durable bookmark request for the currently executing activity.

`ActivityBookmarkRequest` carries:

- bookmark ID
- resume target ID
- stimulus type/hash
- optional JSON payload
- optional expiry
- metadata

## Invoke Handler

After successful activity execution:

- If no bookmark requests exist, existing completion behavior runs.
- If one or more bookmark requests exist, the handler enqueues one `CreateBookmark` scheduler work item per request and returns without completing the activity.

The resulting `RuntimeCreateBookmarkCommandPayload` uses the current invocation's pinned executable identity, executable node ID, and activity execution ID.
