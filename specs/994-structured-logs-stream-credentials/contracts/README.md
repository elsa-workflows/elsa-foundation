# UI and Authentication Boundary

This change adds no public API, endpoint, wire format, package contract, or authentication feature. It changes one internal UI request option at the existing Structured Logs stream creation point.

The page continues to request the same Stream URL with the same filter query. The browser's EventSource credential option sends its managed session under the browser's origin rules. The host's existing cross-origin policy and the backend's existing `diagnostics/structured-logs:read` or wildcard authorization remain authoritative. Recent and Sources are unchanged.

A successful empty stream remains distinct from a denied or failed connection. Authentication material is not encoded in a URL, stored in a new setting, or included in request summaries or persisted evidence. This boundary does not claim support for bearer-only auth or arbitrary EventSource request headers.
