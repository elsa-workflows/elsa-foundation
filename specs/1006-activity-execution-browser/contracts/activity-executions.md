# Activity execution collection

`GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions`, advertised as `activity-executions`.

Query: `executableNodeId`, `parentActivityExecutionId`, `ancestorActivityExecutionId`, `limit` (default 50, existing maximum 500), `cursor`, `anchorActivityExecutionId`. Anchor selects the page containing an exact matching execution; with no cursor/anchor the latest page is returned for node history. Parent/ancestor predicates are exact runtime relationships, independent of activity type.

Response: `items` (summary, sequence, occurrence position, previous/next execution IDs, attempt and distinct iteration position), `totalCount`, `previousCursor`, `nextCursor`. Existing exact inspection endpoint supplies payload evidence. Pages are live reads and counts are response-local; no snapshot guarantee.

Authorize structure before store access; preserve tenant/permission indistinguishability, sensitive redaction and existing problem responses. Cursor bound to access and every predicate; altered/malformed continuation is rejected. Never silently substitute a latest execution for a missing explicit anchor.
