# Studio Preferences API

This package exposes the authenticated Studio Preferences REST surface. It is the first production canary in the [First-party REST API Consolidation program](../../../../docs/program-goals/first-party-rest-api-consolidation.md).

## Composition

Enable `StudioPreferences` for the core store, namespaces, service, and permission catalog. Enable `StudioPreferencesApi` for the HTTP surface; it depends on the core feature.

`StudioPreferencesApiFeature` implements CShells `IWebShellFeature`. Its `MapEndpoints(IEndpointRouteBuilder, IHostEnvironment?)` method delegates to the public `StudioPreferencesApi.MapStudioPreferencesApi(IEndpointRouteBuilder)` entry point. Hosts outside CShells can call that mapper directly after registering both features' services.

## HTTP surface

| Method | Route | Permission | Purpose |
|---|---|---|---|
| GET | `/_elsa/studio/preferences/{namespace}` | `studio.preferences.read` or `*` | Read one scoped preference document. |
| PUT | `/_elsa/studio/preferences/{namespace}` | `studio.preferences.write` or `*` | Create or conditionally update one scoped preference document. |

Both routes use the Foundation Identity policy provider and evaluator. `studio.preferences.write` implies `studio.preferences.read`. The authenticated subject and tenant plus `X-Elsa-Studio-Host-Id` form the scope; the route namespace is authoritative. PUT requires either `If-None-Match: *` for creation or one quoted `If-Match` revision for update.

## Registered services and contributors

The core feature registers the built-in dashboard and attention namespaces, namespace registry, in-memory store, time provider, scoped preference service, and `StudioPreferencesPermissionContributor`. The API feature adds the scoped `StudioPreferenceScopeResolver`.

There are no module-owned event handlers, background tasks, or scheduled jobs. Persistence hosts can replace `IStudioPreferenceStore`; feature modules can contribute additional `IStudioPreferenceNamespace` implementations.

## Transition scope

The package contains no production FastEndpoints dependency or endpoint discovery types. Other Elsa modules may continue to expose FastEndpoints routes in the same host during staged migration; both authoring models ultimately publish standard ASP.NET Core endpoints and use the same Foundation authorization evaluator. The canary evidence and remaining risks are recorded in `docs/reports/studio-preferences-minimal-api-canary-2026-08.md`.
