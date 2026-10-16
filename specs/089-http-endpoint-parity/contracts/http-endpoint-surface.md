# Contract: HTTP Endpoint Surface (089)

The externally observable HTTP contract of workflow endpoints, per sub-unit.

## Request matching

- Base path: `HttpEndpointOptions.BasePath` (per shell). The active shell advertises its effective workflow HTTP mount through the `elsa.http.workflow-endpoints` API capability; clients resolve its `workflow-http-base` link with the shell route prefix. Requests outside the mount pass through untouched (segment-bounded; empty/root base path disables the middleware).
- B (live): the endpoint-relative path is resolved against the per-shell route-table templates (ASP.NET route templates; first deterministic match wins for overlaps); the stimulus identity is (matched template, request method), so the method must be in the endpoint's supported set (unauthored default: GET).

## Responses

| Condition | Status | Body |
|---|---|---|
| Matched, async mode (and A baseline) | 202 Accepted | `{ "started": [executionIds], "resumed": [executionIds], "response": { "mode": "async", "reason": "background-accepted", "message": "The endpoint accepts work in the background and returns 202 before the workflow responds." } }` [resumed live from D — dispatched resumes only; started/resumed coexist per StartAndResume] |
| No matching trigger/bookmark (no start AND no dispatched resume) [D — live] | 404 Not Found | — |
| Ambiguous match (>1 distinct workflow per (template, method)) [B — live] | 409 Conflict | exactly `{ "error": "ambiguous-endpoint" }` — deliberately minimal, no other fields (anti-disclosure, #592 item 10: never echoes the method/template); no instance started |
| Authorize failed (or handler absent — fail closed) [C — live] | 401 Unauthorized | — (evaluated before the body is read) |
| Body exceeds the per-endpoint RequestSizeLimit (or global MaxRequestBodyBytes) [C — live] | 413 Content Too Large | — |
| Cooperative request work exceeded its deadline [C — authored Async dispatch timeout; round two — Sync host `SyncRequestTimeout`, optionally shortened by endpoint `RequestTimeout`] | 408 Request Timeout | via fault handler (inline fallback when absent), if the response has not started |
| Bad-request classified fault (HttpBadRequestException) [C — live] | 400 Bad Request | via fault handler (inline fallback) |
| Other dispatch fault [C — live; E: live for sync runs] | 500 Internal Server Error | via fault handler (inline fallback) |
| Sync mode, workflow wrote response [E — live] | workflow-authored | workflow-authored status/headers/body (via `WriteHttpResponse` inline write; the durable `HttpResponseInstruction` artifact is still recorded) |
| Sync mode, suspended before response / no `WriteHttpResponse` / non-local execution [E — live] | 202 Accepted | `{ "started": [executionIds], "resumed": [executionIds], "response": { "mode": "sync", "reason": "workflow-response-not-produced", "message": "The workflow may have suspended, reached no response activity, or continued on another worker. Inspect the run." } }` degrade (the same 202 writer as async mode; one path, no locality inspection) |

## HttpEndpoint activity surface (authoring contract)

| Member | Kind | Unit | Notes |
|---|---|---|---|
| Path | input (literal, required) | exists | route template from B (`orders/{id}`) |
| SupportedMethods | input | exists | routing-significant from B; N bindings for N methods |
| CanStartWorkflow | input (literal, bool) | D — live | default **false** (elsa-core parity); only explicit `true` creates trigger bindings. False/unauthored → no trigger bindings, node always suspends (mid-flow only). Non-identity. Governs the D-D1 start-vs-suspend decision with `TriggerNodeId` |
| Authorize / Policy | inputs | C — live | non-identity binding metadata; literal-at-publish; fail-closed enforcement |
| RequestTimeout / RequestSizeLimit | inputs | C — live | non-identity binding metadata; Async timeout bounds dispatch (408); Sync timeout may shorten the host-wide full-request deadline; size limit overrides the global cap (413) |
| ParsedContent | output | C — live | content-type-parsed body as wire-safe JSON; null for empty/unknown/non-HTTP starts |
| ResponseMode | input (literal, Sync/Async) | E — live | default **Sync** for new publication ([spec 240](../../240-http-usability/spec.md)); every new binding/bookmark explicitly stores `http:responseMode`, which is non-identity metadata and never enters the stimulus hash. Missing persisted metadata retains the legacy **Async** meaning. Literal-at-publish (member name or defined numeric); non-literal/undefined fails the publish |
| Result: HttpRequestModel | output | A — live | live request from stimulus input |
| RouteData | output | B — live | extracted template parameters |

All new members must reconcile into the activity catalog (§E2.8) via the CLR reconciliation source.
