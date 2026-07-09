# Contract: HTTP Endpoint Surface (089)

The externally observable HTTP contract of workflow endpoints, per sub-unit.

## Request matching

- Base path: `HttpEndpointOptions.BasePath` (per shell). Requests outside it pass through the pipeline untouched (segment-bounded; empty/root base path disables the middleware).
- B (live): the endpoint-relative path is resolved against the per-shell route-table templates (ASP.NET route templates; first deterministic match wins for overlaps); the stimulus identity is (matched template, request method), so the method must be in the endpoint's supported set (unauthored default: GET).

## Responses

| Condition | Status | Body |
|---|---|---|
| Matched, async mode (and A baseline) | 202 Accepted | `{ "started": [executionIds] }` (resumes reported analogously from D) |
| No matching trigger/bookmark | 404 Not Found | — |
| Ambiguous match (>1 distinct workflow per (template, method)) [B — live] | 409 Conflict | `{ "error": "ambiguous-endpoint", ... }`; no instance started |
| Authorize failed [C] | 401 Unauthorized | — |
| Body exceeds RequestSizeLimit [C] | 413 Content Too Large | — |
| Workflow timeout in sync processing [C/E] | 408 Request Timeout | via fault handler |
| Bad-request classified fault [C] | 400 Bad Request | via fault handler |
| Other fault surfaced to the exchange [C/E] | 500 Internal Server Error | via fault handler |
| Sync mode, workflow wrote response [E] | workflow-authored | workflow-authored status/headers/body |
| Sync mode, suspended before response / non-local execution [E] | 202 Accepted | `{ "started": [...] }` degrade |

## HttpEndpoint activity surface (authoring contract)

| Member | Kind | Unit | Notes |
|---|---|---|---|
| Path | input (literal, required) | exists | route template from B (`orders/{id}`) |
| SupportedMethods | input | exists | routing-significant from B; N bindings for N methods |
| Authorize / Policy | inputs | C | non-identity options |
| RequestTimeout / RequestSizeLimit | inputs | C | non-identity options |
| ResponseMode | input (Sync/Async) | E | default Async (preserves baseline) |
| Result: HttpRequestModel | output | A | live request from stimulus input |
| RouteData | output | B | extracted template parameters |
| ParsedContent | output | C | content-type-parsed body |

All new members must reconcile into the activity catalog (§E2.8) via the CLR reconciliation source.
