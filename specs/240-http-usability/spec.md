# Feature Specification: HTTP Workflows That Work Out of the Box

**Feature Branch**: `agent/codex/1177-http-usability`
**Created**: 2026-10-16
**Status**: Implemented
**Issue**: #1177
**Program-goal state**: none/free-flow (one customer-reported HTTP usability improvement).

## User scenarios and testing

### 1. Receive the response authored in a new workflow (P1)

An author creates an HTTP endpoint followed by Write HTTP Response. Publishing without an explicitly authored response mode returns the workflow's status and body. Output captures are optional.

Sync requests are bounded by a server-wide configurable deadline, defaulting to 30 seconds. An endpoint may choose a shorter timeout. Async endpoints keep their existing authored dispatch timeout behavior.

Acceptance: a real POST against a newly published workflow without a response-mode input returns 200 and the authored JSON. Explicit Async still returns 202. Existing stored trigger/bookmark metadata without a mode still means Async until republished. Enum numeric values remain stable.

### 2. See and understand response behavior (P1)

The endpoint offers a dropdown labelled in ordinary language: "Wait for workflow response" and "Accept and run in background". Its graph node shows the effective choice. An Async endpoint with a reachable Write HTTP Response offers a non-blocking explanation and an explicit action to switch to Sync. Editing the graph never silently changes mode.

### 3. Start with a working HTTP example and diagnose the result (P1)

The create-workflow dialog offers an HTTP request/response starter when the necessary catalog activities exist. It authors a start endpoint and JSON response with no required captures. An explicit test-request action sends a real request to the configured backend. Its compact request line groups method, mounted path and Send. The response shows status and accessible Body/Headers tabs: a read-only syntax editor for the response body and a name/value table of the headers exposed by the browser. Empty body and header states are explicit. Starting a new attempt clears the previous response, including when path validation fails. The request body uses the shared code editor, selecting JSON, XML, HTML or JavaScript syntax from the content type (case-insensitive, ignoring parameters and recognizing +json/+xml suffixes). Unknown types use plain text. Switching syntax preserves the body; GET/HEAD and an active send make it read-only. It discovers the active server's workflow HTTP mount, including any shell path prefix, from the server capability. The editable request path stays strictly under that mount. A missing or unusable capability disables the request instead of falling back to a guessed path. A 202 includes an explanation grounded in the actual response contract, including the difference between background acceptance and a synchronous exchange that produced no response.

## Requirements and boundaries

- Keep runtime HTTP knowledge in HTTP code and Studio HTTP-specific presentation in the owning Workflows module; generic property editors consume descriptor metadata.
- Preserve existing published metadata interpretation. New publication records the effective mode explicitly. Republish of an unauthored draft deliberately adopts Sync; document this transition.
- Expose `ActivitiesHttp.SyncRequestTimeout` as the server-wide synchronous-request limit, defaulting to 30 seconds. After route and endpoint settings are selected, the Sync deadline covers authorization, body reading, inline dispatch, and response delivery. A positive endpoint `RequestTimeout` may shorten this deadline but cannot extend it. Timeout cancellation is cooperative and maps to `408 Request Timeout` when the operation observes cancellation before the response starts. Async mode retains its existing authored dispatch timeout, without inheriting the server-wide Sync deadline.
- A valid background workflow remains publishable. Hints must not imply every Write HTTP Response reaches every endpoint or that background execution failed.
- Studio must read the effective workflow HTTP base from the active server's `elsa.http.workflow-endpoints` capability and its `workflow-http-base` link. Include the active shell route prefix when resolving it. If the capability is missing, incompatible, disabled, or invalid, fail closed; do not assume `/workflows/http`. The request editor allows a route beneath the discovered mount, not editing the mount itself.
- Studio's test-request dialog has a separate 35-second client timeout. Raising the server's Sync deadline above 35 seconds does not raise the dialog limit.
- No automatic request on opening a dialog, no arbitrary external request target, and no auth-token/storage inspection. Use the configured backend and canonical HTTP client.
- Use current catalog version IDs for the starter, and fail clearly when required activities are unavailable. No fixed content-addressed IDs.
- No new `PackageReference` entries, persistence migrations, remote deployment, or recording changes. Add a project reference to the API Capabilities project and update affected lockfiles; central package versions remain unchanged.

## Success criteria

A new author can create the HTTP starter, publish it and receive JSON without entering enum strings or binding outputs. A surprising 202 has an actionable explanation. Existing already-published endpoints retain their response behavior. Studio sends test requests only beneath the actual advertised workflow mount. Sync requests use the 30-second default host deadline unless the host changes it, and a shorter endpoint timeout takes precedence; Async dispatch keeps its authored timeout behavior.
