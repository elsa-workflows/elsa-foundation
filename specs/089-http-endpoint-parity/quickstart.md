# Quickstart: Verifying HTTP Endpoint Parity (089)

End-to-end verification walkthrough, extended per sub-unit as each lands.

## Prerequisites

- `dotnet build` at repo root succeeds.
- Server host: `src/apps/Elsa.Server` with the `ActivitiesHttp` (and from B, `WorkflowsRuntimeHttp`) features enabled in `shells.json`.

## A — live request reaches the workflow

1. Run the server. Publish a workflow whose start trigger is `HttpEndpoint` with `Path = "orders/webhook"`.
2. Select `ResponseMode = Async` so this input-delivery check explicitly exercises background acceptance. The example uses the default `/workflows/http` mount; replace it with the active server's configured mount when it differs.
3. `curl -s -X POST http://localhost:<port>/workflows/http/orders/webhook -H 'content-type: application/json' -d '{"id":7}'`
4. Expect `202 Accepted` with `{ "started": ["<executionId>"], "resumed": [], "response": { "mode": "async", "reason": "background-accepted", "message": "The endpoint accepts work in the background and returns 202 before the workflow responds." } }` — the middleware is mounted via the shell feature, no host-code edits.
5. Inspect the execution's state (management API): the trigger activity's Result contains method `POST`, the `content-type` header, and body `{"id":7}` — not the authored-route placeholder.
6. `curl http://localhost:<port>/workflows/http/nope` → `404`. `curl http://localhost:<port>/` → untouched health response (pass-through).

## B — templates and methods

1. Publish an endpoint `Path = "orders/{id}"`, `SupportedMethods = [GET, DELETE]`.
2. `GET /workflows/http/orders/42` → 202; RouteData output contains `id=42`.
3. `POST /workflows/http/orders/42` → 404. Publish a second workflow on the same (template, GET) → request now yields the ambiguity error, nothing starts.

## C — parsing, auth, limits

1. JSON request → ParsedContent output holds the deserialized structure.
2. Authorize-enabled endpoint, anonymous call → 401. Authenticated call → 202.
3. Body larger than RequestSizeLimit → 413-class rejection; faulting workflow → mapped 4xx/5xx.

## D — mid-flow resume

1. Run a workflow that reaches a mid-flow `HttpEndpoint("approvals/{token}", POST)` and suspends.
2. `POST /workflows/http/approvals/abc` → the suspended instance resumes with the live request; second identical request → no waiting bookmark → behaves per StartAndResume for remaining matches.

## E — synchronous response

1. Publish a sync-mode endpoint whose workflow runs `WriteHttpResponse(200, "text/plain", "pong")` — `curl` receives `200 pong` in the same exchange; the durable HttpResponse artifact is also recorded.
2. Sync-mode workflow that suspends first → `202` degrade with the execution identifiers and nested `response.mode = "sync"`, `response.reason = "workflow-response-not-produced"`.
3. The server's configurable `SyncRequestTimeout` defaults to 30 seconds. A positive endpoint `RequestTimeout` may shorten that deadline. It covers authorization, body read, inline dispatch, and response delivery after the route settings are selected; cooperative timeout cancellation returns `408` before response headers start, or aborts the connection after response start. A workflow component that ignores cancellation cannot be forcibly stopped.
4. Async-mode endpoint → `202` acceptance when dispatch completes within its authored `RequestTimeout`. That timeout may still return `408`; it is not replaced by the server-wide Sync deadline.

## Test-suite verification (every sub-unit)

```bash
dotnet build
dotnet test   # full test projects (QA-gate rule: no subsets)
# architecture guard runs within the test suite (local repro requirement)
```
