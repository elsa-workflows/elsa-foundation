# Studio Preferences API

This package exposes the authenticated Studio Preferences REST surface. It is the first production canary in the [First-party REST API Consolidation program](../../../../../docs/program-goals/first-party-rest-api-consolidation.md).

## Composition

Enable `StudioPreferences` for the core store, namespaces, service, and permission catalog. Enable `StudioPreferencesApi` for the HTTP surface; it depends on the core feature.

`StudioPreferencesApiFeature` implements CShells `IWebShellFeature`. Its `MapEndpoints(IEndpointRouteBuilder, IHostEnvironment?)` method delegates to the public `StudioPreferencesApi.MapStudioPreferencesApi` and `StudioHomeLayoutApi.MapStudioHomeLayoutApi` entry points. Hosts outside CShells can call those mappers directly after registering both features' services.

## HTTP surface

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/_elsa/studio/preferences/{namespace}` | `studio/preferences:read` or `*` | Read one scoped preference document. |
| PUT | `/_elsa/studio/preferences/{namespace}` | `studio/preferences:write` or `*` | Create or conditionally update one scoped preference document. |
| GET | `/_elsa/studio/home-layout` | `studio/preferences:read` or `*` | Read the current user's Home layout. Falls back to the admin default, then the built-in layout. |
| PUT | `/_elsa/studio/home-layout` | `studio/preferences:write` or `*` | Create or update the current user's Home layout. |
| DELETE | `/_elsa/studio/home-layout` | `studio/preferences:write` or `*` | Reset: delete the user's layout so the admin default (or built-in) applies. |
| GET | `/_elsa/studio/home-layout/default` | `studio/preferences/defaults:manage` or `*` | Read the admin default Home layout. |
| PUT | `/_elsa/studio/home-layout/default` | `studio/preferences/defaults:manage` or `*` | Save the admin default Home layout. |

Both routes use the Foundation Identity policy provider and evaluator. `studio/preferences:write` implies `studio/preferences:read`. `studio/preferences/defaults:manage` is a separate Studio-preferences permission (wildcard still grants it); it is not `module-management`. The authenticated subject and tenant plus `X-Elsa-Studio-Host-Id` form the scope; the reserved subject `__admin_default__` is rejected on the per-user route. PUT requires either `If-None-Match: *` for creation or one quoted `If-Match` revision for update.

GET `/home-layout` returns the fallback document's `ETag` when the source is `default`. That ETag identifies the admin default, not a user revision: the first user PUT must still send `If-None-Match: *`. A builtin fallback has no ETag. PUT rejects malformed values, duplicate ids, more than 50 widgets, and widget ids that do not match `^[a-z0-9][a-z0-9._-]{0,63}(:[a-z0-9._-]{1,64})?$` with 422. Extension `registerHomeWidget` ids that match the pattern are stored; the client skips widgets that are not registered (for example when the module is disabled) and keeps them in the saved layout.

## Registered services and contributors

The core feature registers the built-in dashboard, attention, and home-layout namespaces, namespace registry, in-memory store, time provider, scoped preference service, and `StudioPreferencesPermissionContributor`. The API feature adds the scoped `StudioPreferenceScopeResolver` and the Home layout routes.

Anonymous or demo Studio sessions (sign-in off) never reach this surface. The Studio client falls back to `localStorage` or the built-in default; see the Home module README.

There are no module-owned event handlers, background tasks, or scheduled jobs. Persistence hosts can replace `IStudioPreferenceStore`; feature modules can contribute additional `IStudioPreferenceNamespace` implementations.

## Transition scope

The package contains no production FastEndpoints dependency or endpoint discovery types. No first-party Elsa module exposes FastEndpoints routes any more, but a third-party module still can in the same host: both authoring models publish standard ASP.NET Core endpoints and use the same Foundation authorization evaluator. The canary evidence and remaining risks are recorded in `docs/reports/studio-preferences-minimal-api-canary-2026-08.md`.
