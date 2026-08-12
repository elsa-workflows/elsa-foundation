# Extension points - Elsa API FastEndpoints domain

`Elsa.Api.FastEndpoints` owns the Elsa-side FastEndpoints integration: the secure-by-construction endpoint base classes, the per-shell API security model, and the `IFastEndpointsConfigurator` contributions that the CShells `FastEndpoints` feature applies when it maps a shell's endpoints. Endpoint security is a property of the shell composition, not a process-global switch: a shell is secured unless it explicitly composes the `ApiSecurity` feature with `AllowAnonymous = true`.

## Feature-inheritance point

### `FastEndpointsFeatureBase`

- **Kind:** Feature inheritance point (abstract base implementing CShells `IFastEndpointsShellFeature`).
- **Extend when:** A domain exposes FastEndpoints endpoints in its assembly and wants them discovered and mapped for any shell that enables the feature.
- **Behavior:** `ConfigureServices` idempotently (`TryAddEnumerable`) registers the domain-agnostic `IFastEndpointsConfigurator` set — `SerializationFastEndpointConfigurator` and `ApiSecurityFastEndpointsConfigurator` — for the shell, and, when `EndpointFilters` are set, an `EndpointFilterFastEndpointConfigurator`. Overriding `ConfigureServices` must call `base.ConfigureServices(services)` so the shell keeps its API security configurator.
- **Known implementations:** every Elsa `*Api` feature (e.g. `ActivitiesDesignApiFeature`, `WorkflowsPublishingApiFeature`, `ModularityApiFeature`, `SecretsApiFeature`) *(cross-domain)*.

## Secure-by-construction endpoint base classes

The endpoint base classes always apply `Permissions(...)` through `ConfigurePermissions()`, so an endpoint is secured the moment it derives from one of them. An individual endpoint can still opt into `AllowAnonymous()` for a genuinely public route; whether a shell honours security at all is decided by the `ApiSecurity` composition below.

| Base class | Shape |
|---|---|
| `ElsaEndpoint<TRequest>` | Request, no response |
| `ElsaEndpoint<TRequest, TResponse>` | Request + response |
| `ElsaEndpoint<TRequest, TResponse, TMapper>` | Request + response + mapper |
| `ElsaEndpointWithoutRequest` | No request, no response |
| `ElsaEndpointWithoutRequest<TResponse>` | No request, response |
| `ElsaEndpointWithMapper<TRequest, TResponse, TMapper>` | Mapper-based |
| `ElsaRequestHandlerEndpoint<TRequest, TResponse>` / `ElsaCommandHandlerEndpoint<TRequest>` | Mediator request/command bridge |

The Mediator-bridge base classes (`ElsaRequestHandlerEndpoint`, both `ElsaCommandHandlerEndpoint` variants) also own the **not-found error contract** (MS-14, issue #393): they catch `EntityNotFoundException` (`Elsa.Primitives.Exceptions`) and map it to `404`, `ArgumentException` to `400`, rethrow `OperationCanceledException`, and fall back to `500` for anything else. A store/lookup that throws `EntityNotFoundException` therefore surfaces as `404` for every endpoint built on these bases — new endpoints inherit the mapping automatically. Combined with the global ProblemDetails configurator below, the wire shape is RFC 7807.

## Implementable contributor interfaces

### `IFastEndpointsConfigurator` (CShells contract)

- **Kind:** Contributor (fan-in; every registered configurator is applied when the shell maps its FastEndpoints).
- **Register:** `services.AddScoped<IFastEndpointsConfigurator, MyConfigurator>()` (usually via `FastEndpointsFeatureBase` or `TryAddEnumerable`).
- **Consumed by:** the CShells `FastEndpoints` feature during `MapEndpoints`, which applies every `IFastEndpointsConfigurator` resolved from the shell provider against the process-static FastEndpoints `Config`.
- **Known implementations (intra-domain):** `ProblemDetailsFastEndpointConfigurator` (registered first; calls `config.Errors.UseProblemDetails()` so every Elsa endpoint returns RFC 7807 ProblemDetails — MS-14); `ApiSecurityFastEndpointsConfigurator` (assigns `Config.Endpoints.Configurator` on every map — a relax action that logs one prominent warning naming the shell when `AllowAnonymous = true`, or `null` when secured, so a relaxed configurator cannot leak across shells through the static `Config`); `SerializationFastEndpointConfigurator` (request/response serialization); `EndpointFilterFastEndpointConfigurator` (applies `IFastEndpointFilter` exclusions).

### `IFastEndpointFilter`

- **Kind:** Contributor (fan-in; excludes endpoints from a shell's mapped set).
- **Register:** expose instances through `FastEndpointsFeatureBase.EndpointFilters`, which wires an `EndpointFilterFastEndpointConfigurator`.
- **Consumed by:** `EndpointFilterFastEndpointConfigurator`, which sets `Config.Endpoints.Filter` to exclude any endpoint a filter reports.
- **Known implementations:** none default; feature modules contribute exclusions *(cross-domain)*.

### `ISseStreamFormatter<TItem>`

- **Kind:** Strategy contract (one formatter per live-feed item type; frames items and heartbeats for Server-Sent Events).
- **Signature:** `string Format(TItem item)`, `string Heartbeat()`.
- **Consumed by:** `SseStreamWriter<TItem>` (this project), which owns the streaming loop: formatted item frames, idle heartbeats, and bounded cleanup of a pending `MoveNextAsync` on disconnect.
- **Known implementations:** `OpenTelemetrySseFormatter` (typed `event:` frames, no resume id) and `StructuredLogSseFormatter` (`id:`-sequenced entry frames) *(cross-domain)*.

## Per-shell security feature

### `ApiSecurityFeature` / `ApiSecurityOptions`

- **Kind:** Shell feature with bindable options (`AllowAnonymous`, default `false`; `ShellName` is populated from the shell settings).
- **Purpose:** The single, explicit way to opt a shell out of endpoint security. When absent (the default), `ApiSecurityFastEndpointsConfigurator` secures the shell. When present with `AllowAnonymous = true`, the configurator relaxes the shell's endpoints and logs one prominent warning naming the shell.
- **Consumed by:** `ApiSecurityFastEndpointsConfigurator` via `IOptions<ApiSecurityOptions>`.

---

## Constitutional basis

- Framework §2.6.1 — contributor interfaces and single aggregation points.
- Framework §2.22.1 — per-domain extension-point catalog.
- Framework §2.23 — registration and implementation unit-test obligations.
