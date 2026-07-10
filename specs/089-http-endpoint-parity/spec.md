# Feature Specification: HTTP Endpoint Full Parity

**Feature Branch**: `089-http-endpoint-parity`

**Created**: 2026-07-08

**Status**: Draft

**Input**: User description: "Bring the foundation's HttpEndpoint capability to full behavioral parity with elsa-core's Elsa.Http module: start-input delivery, per-method routing, route templates, content parsing, authorization/fault handling/limits, mid-workflow HttpEndpoint resume, and synchronous request/response via the spec-069 request-affine execution seam. Decomposed into five sequenced sub-units (A–E), each landable as its own branch/PR. Approved design plan with verified code facts: ~/.claude/plans/agile-swimming-matsumoto.md."

## Context

The W16 activity-library unit (PR #465) shipped the async/202 baseline: `HttpEndpoint` start trigger indexed through the `IActivityTriggerStimulusProvider` seam, `HttpEndpointMiddleware` dispatching through `IStimulusRouter` in StartOnly mode, and `WriteHttpResponse` recording a durable response artifact. It deliberately deferred start-input delivery, per-method routing, and synchronous response correlation as named follow-ups. Spec 069 (request-affine execution) later landed the runtime seam that makes synchronous responses possible without abandoning actor/mailbox semantics — but its non-goals explicitly excluded `WriteHttpResponse` and HTTP endpoint wiring. This unit is those follow-ups, delivered as five sequenced sub-units.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Webhook receives the real request (Priority: P1) — sub-units A

A workflow author publishes a workflow starting with an HTTP Endpoint trigger. An external system posts JSON to the endpoint path. The started workflow reads the actual request body, headers, and query parameters — not a placeholder — and the endpoint is reachable in a running server without hand-editing host code.

**Why this priority**: Without the live request payload, an HTTP-triggered workflow cannot act on its input; and today the middleware is not mounted in any host, so the feature is unreachable end-to-end. This is the minimum useful webhook.

**Independent Test**: Start the server host, publish a workflow with an HTTP Endpoint trigger, POST a JSON body, and assert the workflow's recorded state contains the posted body/headers/query.

**Acceptance Scenarios**:

1. **Given** a published workflow triggered at path `orders/webhook`, **When** a client POSTs a JSON body to that path under the endpoints base path, **Then** the response is `202 Accepted` with the started execution id and the workflow observes the live body, headers, and query values.
2. **Given** the same request, **When** the workflow's trigger activity produces its result, **Then** the result reflects the live request (not the authored route projection).
3. **Given** a request to a path with no published trigger, **When** it arrives under the base path, **Then** the response is `404 Not Found`.

---

### User Story 2 - Method-aware, templated routes (Priority: P2) — sub-unit B

An author declares an endpoint at `orders/{id}` accepting only `GET` and `DELETE`. A `GET orders/42` starts the workflow with route parameter `id=42` available as an output; a `POST orders/42` does not match. Two workflows publishing the same (path, method) pair are rejected as ambiguous at request time.

**Why this priority**: Method discrimination and route parameters are core REST semantics; without them the endpoint activity cannot model real APIs.

**Independent Test**: Publish a workflow with a templated path and restricted methods; verify matching/non-matching requests and the extracted route data.

**Acceptance Scenarios**:

1. **Given** a published endpoint `orders/{id}` with methods `GET,DELETE`, **When** `GET orders/42` arrives, **Then** the workflow starts and its RouteData output contains `id=42`.
2. **Given** the same endpoint, **When** `POST orders/42` arrives, **Then** no workflow starts and the response is `404 Not Found`.
3. **Given** two published workflows on the same (template, method), **When** a matching request arrives, **Then** the request is rejected with an ambiguity error and neither workflow starts.
4. **Given** a republished workflow whose endpoint path changed, **When** requests arrive, **Then** only the new path matches (the old route is gone from the route table).

---

### User Story 3 - Parsed content, authorization, faults, limits (Priority: P3) — sub-unit C

An author marks an endpoint as requiring authorization and sets a request size limit. Unauthenticated calls get `401`. Oversized bodies are rejected. A JSON body arrives parsed as structured content on the activity's ParsedContent output. Workflow faults map to meaningful HTTP status codes (timeout → 408, bad request → 400, fault → 500).

**Why this priority**: Production endpoints need authn/z and defensive limits; parsed content removes boilerplate from every consuming workflow.

**Independent Test**: Exercise an authorized endpoint anonymously (401), with an oversized body (413-class rejection), with valid JSON (ParsedContent populated), and with a faulting workflow (mapped status).

**Acceptance Scenarios**:

1. **Given** an endpoint with Authorize set, **When** an unauthenticated request arrives, **Then** the response is `401` and no workflow starts.
2. **Given** a JSON request to a matching endpoint, **When** the workflow runs, **Then** ParsedContent holds the deserialized structure chosen by content type.
3. **Given** an endpoint with a request size limit, **When** a larger body arrives, **Then** the request is rejected and no workflow starts.
4. **Given** a workflow that faults during synchronous execution, **When** the caller waits, **Then** the mapped status code is returned (timeout → 408, bad request → 400, other fault → 500).

---

### User Story 4 - Workflow waits mid-flow for a request (Priority: P4) — sub-unit D

A workflow starts (by any trigger), performs work, then suspends at a mid-flow HTTP Endpoint activity. A later HTTP request to that endpoint's (path, method) resumes the suspended instance and delivers the request to it.

**Why this priority**: Callback-style integrations (approval links, payment confirmations) require suspend-on-endpoint; valuable but independent of the start-trigger path.

**Independent Test**: Run a workflow that suspends on a mid-flow endpoint; send the matching request; assert the instance resumes with the request payload.

**Acceptance Scenarios**:

1. **Given** an instance suspended at a mid-flow HTTP Endpoint, **When** a matching request arrives, **Then** the instance resumes and observes the live request payload.
2. **Given** both a published start trigger and a suspended instance matching the same stimulus, **When** a request arrives, **Then** routing follows StartAndResume semantics (waiting instances resume; new instances start) without the started instance self-resuming on the same stimulus.
3. **Given** a suspended mid-flow endpoint whose bookmark has expired, **When** a matching request arrives, **Then** the instance does not resume.

---

### User Story 5 - Synchronous response in the same request (Priority: P5) — sub-unit E

An author sets an endpoint's response mode to synchronous. A client calls the endpoint and receives, in the same HTTP exchange, the status/headers/body produced by the workflow's Write HTTP Response activity. If the workflow suspends at a durable boundary before responding, the caller receives `202 Accepted` instead. Endpoints in async mode keep today's immediate-202 behavior.

**Why this priority**: This is the headline elsa-core parity item, but it depends on A (input), and benefits from B/C (routing identity, faults/timeouts); it lands last by design.

**Independent Test**: Sync-mode endpoint whose workflow writes a response → caller receives it in-request; workflow that suspends first → caller receives 202; async-mode endpoint → 202 always.

**Acceptance Scenarios**:

1. **Given** a sync-mode endpoint whose workflow executes Write HTTP Response, **When** a client calls it, **Then** the same HTTP exchange returns the workflow-authored status, headers, and body, and the durable response artifact is also recorded.
2. **Given** a sync-mode endpoint whose workflow suspends before responding, **When** a client calls it, **Then** the client receives `202 Accepted` with the execution id (degrade path).
3. **Given** a sync-mode endpoint with a request timeout, **When** the workflow exceeds it, **Then** the caller receives `408` and the instance continues per normal runtime semantics.
4. **Given** an async-mode endpoint, **When** called, **Then** behavior is identical to today's 202 baseline.
5. **Given** a suspended mid-flow endpoint in sync mode, **When** the resuming request arrives, **Then** the resuming caller can receive the workflow's subsequent Write HTTP Response in that same exchange (fresh ambient context on resume).

### Edge Cases

- Request arrives while the route table is still warming after shell start: the request must not be silently dropped; it either matches (table ready) or 404s deterministically — no partial-table ambiguity errors.
- A stimulus matches a trigger whose artifact was since unpublished: no start; 404.
- Sync mode on a node that does not host the execution (distributed actor provider): degrade to 202; ambient request services never cross process boundaries.
- Write HTTP Response executes with no live HTTP context (async mode, resumed later, or non-HTTP start): the durable artifact is still recorded; no fault is raised for the missing context (decided: softer than elsa-core's fault — see Assumptions).
- Two concurrent requests resume the same single suspended bookmark: exactly one resume wins (existing bookmark-consumption semantics); the loser gets the no-match outcome.
- Body read must respect the size limit during streaming, not only Content-Length declarations.

## Requirements *(mandatory)*

### Functional Requirements

**Sub-unit A — host wiring + start-input delivery**

- **FR-001**: The stimulus routing start path MUST deliver the dispatched stimulus input to started workflow instances through the existing seed-input channel, under a well-known input key; the resume path's existing input delivery is unchanged.
- **FR-002**: The HTTP Endpoint trigger activity MUST surface the live request model (path, method, headers, query, body) from the delivered stimulus input as its Result, replacing the authored-route projection.
- **FR-003**: The HTTP activities feature MUST mount its request middleware into the shell's request pipeline through the platform's middleware contribution seam (no manual host-code edits), ordered after authentication/authorization middleware so the request identity is available.
- **FR-004**: Requests outside the configured endpoints base path MUST pass through untouched (current behavior preserved).

**Sub-unit B — routing upgrade**

- **FR-005**: Trigger stimulus identity MUST key on the pair (normalized route template, lowercased HTTP method); publishing an endpoint with N supported methods produces N trigger bindings.
- **FR-006**: Trigger bindings MUST carry the route template, method, and non-identity endpoint options (authorize, policy, request timeout, request size limit, response mode) as binding metadata; these options MUST NOT affect stimulus identity.
- **FR-007**: A per-shell route table MUST be rebuilt from the trigger binding store at shell start and updated when bindings change (publish/republish/unpublish), with no separate durable store.
- **FR-008**: The middleware MUST resolve a concrete request path to a route template (extracting route parameters) before computing stimulus identity; unmatched paths yield `404`.
- **FR-009**: When more than one distinct workflow matches the same (template, method), the request MUST be rejected with an ambiguity error response and no instance started.
- **FR-010**: The HTTP Endpoint activity MUST expose extracted route parameters as a RouteData output.

**Sub-unit C — parsing, authorization, faults, limits**

- **FR-011**: The middleware MUST parse the request body by content type through the prioritized content-parser set and deliver the parsed value to the activity's ParsedContent output; unrecognized content types fall back to the raw body string.
- **FR-012**: Endpoints marked Authorize MUST be enforced through the endpoint authorization handler seam before any dispatch; failures return `401`. Policy names, when present, are evaluated by the handler.
- **FR-013**: Endpoint faults MUST map to HTTP status codes through the endpoint fault handler seam: execution timeout → `408`, bad-request classification → `400`, other faults → `500`.
- **FR-014**: Per-endpoint request timeout MUST bound synchronous processing; per-endpoint request size limit MUST be enforced during body reading (not only via declared Content-Length).

**Sub-unit D — mid-workflow endpoint**

- **FR-015**: A non-start HTTP Endpoint activity MUST create a durable bookmark keyed by the same stimulus identity as the start trigger, following the established activity bookmark + resume-target pattern.
- **FR-016**: The middleware MUST dispatch in StartAndResume mode so a matching request resumes waiting instances and starts newly-triggered ones, preserving the router's existing self-resume protection.
- **FR-017**: The resumed activity MUST observe the resuming request's live model through its declared outputs (Result, RouteData, ParsedContent), consistent with the start path.

**Sub-unit E — synchronous response**

- **FR-018**: Endpoints MUST support a per-endpoint response mode (Sync | Async). Async preserves the current immediate-202 contract exactly.
- **FR-019**: In sync mode, the middleware MUST dispatch with request-affine ambient services (per spec 069) so the run drains inline on the caller's flow; the caller receives the workflow-authored response produced by Write HTTP Response within the same HTTP exchange.
- **FR-020**: Write HTTP Response MUST write the live response (status, headers, body via the content-factory set) when a live HTTP context is exposed by ambient request services, and MUST always record the durable response artifact regardless.
- **FR-021**: Ambient request services MUST never enter durable command envelopes or any persisted state (spec 069 FR-001 invariant re-asserted here).
- **FR-022**: Sync mode MUST degrade to `202 Accepted` when the run reaches a durable boundary before a response is written, and when execution is not local to the request (distributed provider).

### Key Entities

- **HTTP request model**: the serialized request (path, method, headers, query, body) carried as stimulus input; the single wire shape shared by start and resume.
- **Trigger binding metadata**: per-binding string map carrying route template, method, and non-identity endpoint options; source of truth for the route table.
- **Route table (per shell)**: in-memory template catalog rebuilt from trigger bindings; maps concrete paths to templates + route parameters.
- **Response instruction artifact**: the durable record of the workflow's intended HTTP response; unchanged shape, now also driving the live response in sync mode.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can go from `git clone` to a working HTTP-triggered workflow (publish → curl → observe live payload in workflow state) using only shell configuration — zero host-code edits.
- **SC-002**: An endpoint declared with a route template and restricted methods matches exactly its (template, method) set: 100% of non-matching methods/paths return 404 in the acceptance suite.
- **SC-003**: A sync-mode endpoint returns the workflow-authored response in the same HTTP exchange for workflows that respond without suspending; degrade cases (suspend-first, distributed) return 202 — no request ever hangs past its configured timeout.
- **SC-004**: All five sub-units land as independent PRs, each passing the full test-project QA gate and architecture guard without changes to the others' scope.
- **SC-005**: Behavior parity checklist against elsa-core's module (trigger start, method/template routing, parsed content, authorize/policy, fault mapping, timeout/size limits, mid-flow resume, sync response, 202 mode) is fully green, with the two documented exceptions (multipart upload validation descoped; missing-HTTP-context is artifact-only instead of a fault).

## Assumptions

- Pre-release posture: stimulus-hash contents may change without migration shims (per repo policy); existing test hash expectations are regenerated in sub-unit B.
- Multipart/form-data file upload validation (file size/extension/MIME whitelists) is explicitly out of scope; noted for a future unit.
- Write HTTP Response without a live HTTP context records the artifact and does not fault (deliberately softer than elsa-core's NoHttpContext fault; revisit if callers need strictness).
- Quiescence gating (503 + Retry-After during shutdown/drain) is out of scope; noted as a follow-up.
- Correlation-id / workflow-instance-id selectors (header/query) from elsa-core are out of scope for this unit; the stimulus router's correlation scope is unchanged.
- The spec-069 inline drain runs to quiescence within the dispatch on the in-process actor; verified during planning (speckit-plan) before sub-unit E is scheduled.
- Distributed (multi-node) synchronous responses are a future concern; the design leaves the degrade path (202) and does not build distributed transport.
- Multi-node route-table freshness is a future concern; the per-shell route table is refreshed by a process-local observer (`RouteTableTriggerIndexObserver`) on each publish, so on a multi-node host other nodes serve their last-refreshed routes until they next publish or restart. No cross-node invalidation signal is built here — like distributed sync responses, this waits on a production-ready distributed provider (a durable route store / index-change subscription) rather than a bespoke broadcast. Single-node hosts are always fresh; a new node rebuilds its full route table from the durable trigger index at startup (`UpdateRouteTableStartupTask`).

## Sub-unit sequencing (each its own branch/PR)

| Sub-unit | Contents | Depends on |
|---|---|---|
| A | Host middleware wiring + start-input delivery | — |
| B | Method + template routing, binding metadata, route table | A |
| C | ParsedContent, authorization, fault mapping, timeout/size limits | B |
| D | Mid-workflow endpoint bookmark + StartAndResume | B (C recommended) |
| E | Sync response via request-affine dispatch + Write HTTP Response live write | A–D |
