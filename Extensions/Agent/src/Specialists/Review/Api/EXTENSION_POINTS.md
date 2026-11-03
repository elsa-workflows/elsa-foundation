# Specialist human review API extension points

Owner project: `Elsa.Agent.Specialists.Review.Api`
Domain: `Elsa.Agent`

Two NativeEndpoints endpoints over `IHumanReviews`, with capability `elsa.api.agent-reviews` v1:

| Route | Permission |
|---|---|
| `GET agent/reviews/{workflowExecutionId}/{activityExecutionId}` | `agent-reviews:read` |
| `POST agent/reviews/{workflowExecutionId}/{activityExecutionId}/decision` | `agent-reviews:decide` |

The caller's tenant and subject come only from the trusted normalized principal. The decision body has a
closed schema (`contentHash`, `decision`, `comment`, `requestId`), so an `actorId`, `requestedBy` or
other unmapped member is refused with 400. On GET, a missing or foreign review is 404. The decision route
answers a well-formed request with a typed receipt status: `Accepted`, `Pending`, `AlreadyResolved`,
`Conflict`, `Forbidden`, `NotFound`, `Closed`, `Unavailable` or `Refused`. A retry with the same request ID
replays an admitted decision's native receipt. `Refused` asks for a new request ID; it is not durable, so once the
runtime forgets the request (a restart, actor passivation or idempotency-cache eviction) the same ID is evaluated
afresh against current permission and expiry. Receipts never carry review content, because deciding does not imply reading. `HumanReviewPermissionContributor`
contributes both permissions.
