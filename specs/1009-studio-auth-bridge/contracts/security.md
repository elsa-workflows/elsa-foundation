# Public integration and wire contract

The [hosting contract](../../../docs/reports/studio-hosting-contract.md) remains canonical.

Common AddElsaStudio/MapElsaStudio own registration/mapping; no new public handler/cache/client abstraction.
Studio:Auth and existing backend configuration keys retain their meanings. Configure before Add; use standard
root DI/HttpClientFactory configuration for a custom clock, handlers and HTTP behavior.

The existing Api StudioBridgeAuthorization owner gains the current coarse/read/manage policy names and two
module-management permission keys. Keep exact values; module endpoints use these/public RequireStudioBridgePermission.
Internal Web policy dependencies disappear without a production friend.

Browser status/registry/runtime fields and paths remain compatible. No management key is included in browser
JSON/JS or bearer introspection; only the privileged backend read client attaches it. Missing URL/key produces
no privileged outbound call. Authenticated session requests are a separate operation.

Cache behavior stays bounded by the configured TTL/cap and shorter definitive-negative TTL. Revocation follows
expiry or disabled caching; an immediate/distributed invalidation guarantee is not introduced. Request
cancellation is distinct from backend timeout. Query tokens are limited to explicit trusted non-root prefixes,
including segment-boundary checks; all other paths require bearer by header.
