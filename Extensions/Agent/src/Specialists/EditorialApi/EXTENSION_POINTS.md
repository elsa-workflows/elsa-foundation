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
other unmapped member is refused with 400. A missing or foreign review is 404. Refusals of a well-formed
decision are typed receipt statuses (`Conflict`, `AlreadyResolved`, `Forbidden`, `Closed`, `Unavailable`),
so a retry with the same request ID replays the native receipt. `EditorialReviewPermissionContributor`
contributes both permissions.
