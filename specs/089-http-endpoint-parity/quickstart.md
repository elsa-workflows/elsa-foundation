# Quickstart: Verifying HTTP Endpoint Parity (089)

End-to-end verification walkthrough, extended per sub-unit as each lands.

## Prerequisites

- `dotnet build` at repo root succeeds.
- Server host: `src/apps/Elsa.Server` with the `ActivitiesHttp` (and from B, `WorkflowsRuntimeHttp`) features enabled in `shells.json`.

## A — live request reaches the workflow

1. Run the server. Publish a workflow whose start trigger is `HttpEndpoint` with `Path = "orders/webhook"`.
2. `curl -s -X POST http://localhost:<port>/workflows/http/orders/webhook -H 'content-type: application/json' -d '{"id":7}'`
3. Expect `202 Accepted` with `{ "started": ["<executionId>"] }` — the middleware is mounted via the shell feature, no host-code edits.
4. Inspect the execution's state (management API): the trigger activity's Result contains method `POST`, the `content-type` header, and body `{"id":7}` — not the authored-route placeholder.
5. `curl http://localhost:<port>/workflows/http/nope` → `404`. `curl http://localhost:<port>/` → untouched health response (pass-through).

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
2. Sync-mode workflow that suspends first → `202` degrade. Timeout exceeded → `408`. Async-mode endpoint → always `202` (baseline preserved).

## Test-suite verification (every sub-unit)

```bash
dotnet build
dotnet test   # full test projects (QA-gate rule: no subsets)
# architecture guard runs within the test suite (local repro requirement)
```
