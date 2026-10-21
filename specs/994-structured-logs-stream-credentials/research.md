# Research: Authenticated Structured Logs Streaming

## Decision: opt in to the browser-managed session on the existing stream

The current client constructs `new EventSource(createStreamUrl(...))` without an init option. The existing normal-host reproduction shows Recent and Sources succeed while that product stream receives 401; a browser request using `new EventSource(url, { withCredentials: true })` in the same authorized context receives 200 with `text/event-stream`. The bounded client correction is to pass `{ withCredentials: true }` at the existing construction site.

The WHATWG [Server-sent events specification](https://html.spec.whatwg.org/multipage/server-sent-events.html#the-eventsource-interface) defines the `withCredentials` EventSource init option. With it, the browser includes credentials under its normal same-origin/cross-origin rules. A cross-origin response still depends on the host's existing credentialed CORS configuration; this plan does not change that policy.

## Preserve existing behavior and security boundaries

- Leave `createStreamUrl` and its filter query parameters unchanged. Existing same-origin and separate-origin URL semantics remain covered.
- Leave the current effect lifecycle intact: filter changes dispose the previous source and create its replacement; unmount closes the active source. The browser owns automatic EventSource retry for a transient interruption.
- Keep the existing backend permission checks on Recent, Sources, and Stream authoritative. `StructuredLogsApi.MapStructuredLogsApi` applies the existing wildcard-or-module permission policy to all three routes; denied requests must not start an SSE response or disclose events.
- Credentials remain browser-managed. Do not put a token or secret in the URL, a new setting, request summaries, or saved evidence.
- The recorded credentialed browser probe supports the cookie-session repro only. EventSource does not provide a general arbitrary-header option, so this work makes no bearer-only authentication claim.

## Alternatives considered

- **Custom fetch/readable-stream client or shared streaming abstraction**: rejected; a browser-native option addresses the demonstrated defect without replacing retry, framing, or cleanup behavior.
- **Bearer token in the query or a new client token setting**: rejected; outside the approved cookie-session scope and would create credential exposure and storage concerns.
- **Backend permission, CORS, endpoint, or authentication changes**: rejected for this issue; the existing credentialed probe succeeds and existing endpoint policy is the boundary to preserve.
- **Constructor-only assertion as completion evidence**: insufficient under FR-006; it cannot prove the actual product request succeeds in the normal host. Keep it as regression coverage, then qualify the rebuilt product stream in the real separate-origin composition.

## Evidence still required during implementation qualification

The retained probe is prior reproduction evidence, not final acceptance. After implementation and the reviewed RED/GREEN test checkpoint, the rebuilt actual product page must reach Live with a 200 `text/event-stream` request under the same disposable standard private-cookie session, including the empty-inventory case. Denial cases, filters, pause/buffer, transient reconnect, and cleanup must retain their specified outcomes.
