# Contract: HTTP Endpoint Surface (089)

The externally observable HTTP contract of workflow endpoints, per sub-unit.

## Request matching

- Base path: `HttpEndpointOptions.BasePath` (per shell). Requests outside it pass through the pipeline untouched (segment-bounded; empty/root base path disables the middleware).
- B (live): the endpoint-relative path is resolved against the per-shell route-table templates (ASP.NET route templates; first deterministic match wins for overlaps); the stimulus identity is (matched template, request method), so the method must be in the endpoint's supported set (unauthored default: GET).

## Responses

| Condition | Status | Body |
|---|---|---|
| Matched, async mode (and A baseline) | 202 Accepted | `{ "started": [executionIds], "resumed": [executionIds] }` [resumed live from D — dispatched resumes only; started/resumed coexist per StartAndResume] |
| No matching trigger/bookmark (no start AND no dispatched resume) [D — live] | 404 Not Found | — |
| Ambiguous match (>1 distinct workflow per (template, method)) [B — live] | 409 Conflict | `{ "error": "ambiguous-endpoint", ... }`; no instance started |
| Authorize failed (or handler absent — fail closed) [C — live] | 401 Unauthorized | — (evaluated before the body is read) |
| Body exceeds the per-endpoint RequestSizeLimit (or global MaxRequestBodyBytes) [C — live] | 413 Content Too Large | — |
| Dispatch exceeded the per-endpoint RequestTimeout [C — live; E extends to sync runs] | 408 Request Timeout | via fault handler (inline fallback when absent) |
| Bad-request classified fault (HttpBadRequestException) [C — live] | 400 Bad Request | via fault handler (inline fallback) |
| Other dispatch fault [C — live; E extends to sync runs] | 500 Internal Server Error | via fault handler (inline fallback) |
| Sync mode, workflow wrote response [E] | workflow-authored | workflow-authored status/headers/body |
| Sync mode, suspended before response / non-local execution [E] | 202 Accepted | `{ "started": [...] }` degrade |

## HttpEndpoint activity surface (authoring contract)

| Member | Kind | Unit | Notes |
|---|---|---|---|
| Path | input (literal, required) | exists | route template from B (`orders/{id}`) |
| SupportedMethods | input | exists | routing-significant from B; N bindings for N methods |
| CanStartWorkflow | input (literal, bool) | D — live | default **true** (deviation from elsa-core default-false; preserves A–C); non-identity; `false` → no trigger bindings, node always suspends (mid-flow only). Governs the D-D1 start-vs-suspend decision with `TriggerNodeId` |
| Authorize / Policy | inputs | C — live | non-identity binding metadata; literal-at-publish; fail-closed enforcement |
| RequestTimeout / RequestSizeLimit | inputs | C — live | non-identity binding metadata; timeout bounds dispatch (408), size limit overrides the global cap (413) |
| ParsedContent | output | C — live | content-type-parsed body as wire-safe JSON; null for empty/unknown/non-HTTP starts |
| ResponseMode | input (Sync/Async) | E | default Async (preserves baseline) |
| Result: HttpRequestModel | output | A — live | live request from stimulus input |
| RouteData | output | B — live | extracted template parameters |

All new members must reconcile into the activity catalog (§E2.8) via the CLR reconciliation source.
