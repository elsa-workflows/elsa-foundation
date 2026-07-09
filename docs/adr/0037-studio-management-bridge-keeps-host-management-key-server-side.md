# Studio Management Bridge Keeps Host Management Keys Server-Side

Status: accepted (2026-07-09; ratified after maintainer review of PRD #584 and slice breakdown.
Free-flow design, sharpened through a grilling pass after diagnosing browser calls from
`Elsa.Studio.Web` to `Elsa.Server` host-control endpoints.)

Browser clients must not carry an [Elsa host management key](../glossary/elsa.md). Host-control
operations such as module management and Extension Builder remain management-key-protected between
servers, while `Elsa.Studio.Web` exposes a [Studio management bridge](../glossary/elsa.md) that
authorizes browser requests with user [host-control permissions](../glossary/elsa.md) and attaches
server-side credentials only when invoking a target Elsa host.

## Context

`Elsa.Server` protects module-management and Extension Builder endpoints with the configured
management key. `Elsa.Studio.Web` currently has browser-side endpoint contexts for both the Studio
host and backend host, so the SPA can call backend host-control endpoints directly. That shape either
fails when no management key is exposed to the browser, or succeeds only by publishing the key through
runtime configuration, which makes the key cease to be a server-side secret.

## Decision

Use a Studio-owned server-side bridge for browser-initiated host-control operations. The browser
calls Studio with its normal user session or bearer token. Studio checks explicit coarse permissions
such as `module-management.read`, `module-management.manage`, `extension-builder.read`, and
`extension-builder.manage`, then calls the backend host with the server-side host management key when
the target endpoint requires it.

The bridge is not a path-for-path reverse proxy. Browser-facing routes and DTOs belong to Studio and
should express Studio concepts such as host registry, host capabilities, and build operations. The
implementation may reuse backend contract types internally when they are stable shared contracts, but
frontend code should not be coupled to backend host-control endpoint paths.

Host-control permissions are first-class identity permissions, but their ownership remains with the
feature/domain that owns the host-control surface. If the current permission catalog cannot accept
feature-owned permission contributions, add the minimum composition seam instead of hard-coding
module-management or Extension Builder permissions inside the identity domain.

The first bridge slice lives in `elsa-foundation-studio`. It may call existing `elsa-foundation`
backend HTTP contracts where those contracts are already stable, but it should not introduce a new
shared bridge contract package until another consumer or a real versioning problem exists.

`studio-runtime.js` must not expose the backend host management key. Studio may keep a server-side
configuration value for bridge-to-backend calls, but the browser-visible runtime field should be
removed or left empty/deprecated once frontend callers no longer depend on it.

## Considered Options

- Expose the backend management key to the browser. Rejected because it turns a host-control secret
  into client-visible configuration and couples browser code to host-to-host authentication.
- Make backend host-control endpoints accept user bearer tokens directly. Rejected for the current
  shape because those endpoints are host-control surfaces, not ordinary user APIs; direct user bearer
  support can be added later only as an explicit backend authorization model.
- Add a narrow pass-through proxy for backend endpoint paths. Rejected because it preserves the
  frontend/backend topology coupling and hides the boundary decision behind proxy mechanics.
- Route browser requests through Studio. Accepted because it preserves the server-side credential
  boundary while still using normal user authorization for product access.

## Consequences

Studio owns the browser-facing contract for backend host-control capability, registry, and mutation
flows. Frontend code should stop constructing direct backend host-control requests.

When Studio lacks a backend host management key, backend host-control operations fail closed but the
Studio shell should degrade gracefully. Identity, ordinary backend bearer-token APIs, and local Studio
host management may continue to work; backend module-management and Extension Builder UI should
surface a clear "backend management unavailable" state instead of repeatedly issuing doomed backend
requests.

Studio should expose explicit backend management availability through the bridge instead of asking
the frontend to infer state from failed fetches. Useful states include `available`, `unconfigured`,
`unreachable`, `unauthorized`, and `degraded`.

The immediate remediation for browser `401` noise is not to publish
`Studio:BackendModuleManagementApiKey` to browser runtime configuration. The first implementation
slice should route read-only backend host-control reads through Studio:

- backend module registry
- backend Extension Builder capabilities
- backend management availability/status

Mutations such as package upload, reconcile, prune, feed edits, file edits, build, promote, and
rollback are later slices because they require sharper permission checks and UX failure handling.

Read-only backend host-control endpoints remain management-key protected between Studio and Server.
If Elsa later needs direct browser/API access with bearer-token authorization, that should be a new
backend contract rather than an exception inside the existing host-control surface.

Implementation should proceed in this order:

1. Add server-side Studio bridge endpoints for backend management status, backend module registry,
   and backend Extension Builder capabilities.
2. Move frontend reads to those Studio endpoints.
3. Stop emitting and using browser-visible backend management key configuration.
4. Add feature-owned host-control permission contributions and bridge authorization checks.
5. Defer mutating operations to later slices.

Direct backend management-key access remains supported for non-browser callers: server-to-server
bridges, CLI automation, and trusted operator scripts may call `Elsa.Server` host-control endpoints
directly with the host management key. Possession of a Studio session alone is never enough to
perform host-control work.
