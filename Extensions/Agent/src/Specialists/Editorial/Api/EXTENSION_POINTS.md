# Specialist editorial review API extension points

Owner project: `Elsa.Agent.Specialists.Editorial.Api`
Domain: `Elsa.Agent`

Two NativeEndpoints endpoints over `IEditorialReviews`, with capability `elsa.api.editorial-reviews` v1:

| Route | Permission |
|---|---|
| `GET agent/editorial-reviews/{workflowExecutionId}/{activityExecutionId}` | `editorial-reviews:read` |
| `POST agent/editorial-reviews/{workflowExecutionId}/{activityExecutionId}/decision` | `editorial-reviews:decide` |

The caller's tenant and subject come only from the trusted normalized principal. The decision body has a
closed schema (`round`, `articleHash`, `decision`, `comment`, `requestId`), so an `actorId`, `requestedBy` or
other unmapped member is refused with 400. On GET, a missing or foreign review is 404. The decision route
answers a well-formed request with a typed receipt status: `Accepted`, `Pending`, `AlreadyResolved`,
`Conflict`, `Forbidden`, `NotFound`, `Closed`, `Unavailable` or `Refused`. A retry with the same request ID
replays the native receipt. Receipts never carry review content, because deciding does not imply reading. `EditorialReviewPermissionContributor`
contributes both permissions.
