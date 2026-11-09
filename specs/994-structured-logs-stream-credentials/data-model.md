# Data Model: Authenticated Structured Logs Streaming

No entities, fields, persisted settings, or state transitions are added.

| Existing data/state | Use in this change | Invariant |
|---|---|---|
| Existing page filter selection | Continues to build Recent and Stream URLs | Query names and values stay unchanged; no credential enters the URL |
| Existing `EventSource` instance and page lifecycle | Receives the browser credential option; existing effect owns replacement and close | A filter replacement closes the old instance; unmount closes the active instance; no duplicate page-owned subscription |
| Existing connection and display state | Continues to represent connecting, live, paused, and disconnected behavior | Successful empty stream can be Live; a denial/error is not presented as successful empty data |
| Browser-managed signed-in cookie session | Sent according to browser EventSource credential rules when enabled | No client token state, URL token, or persisted credential is introduced |
| Existing backend permission and stream filter binding | Remains the server-side authority | Credential transport does not grant a permission; rejected principals receive no SSE data |

The server-side endpoint, permissions, filter binder, replay cursor, and event payload model are unchanged.
