# Workflow inspection authorization extension points

## Overridable contracts

| Contract | Kind / layer | Default / owning project | Signature and use |
|---|---|---|---|
| `IActivityInspectionContextAsync` | Replacement, `.Core`; exactly one request-scoped implementation | `HttpContextActivityExecutionInspectionAuthorizationContext`, `Elsa.Workflows.Runtime.Api` | Trusted tenant/audit subject/correlation; `GetAuthorizationProfileAsync`, `CanInspectStructureAsync`, `CanInspectSensitiveValuesAsync`, `CanResolveSensitiveValuePayloadsAsync`. API readers and authorized Dashboard source reads consume the same record-specific decisions. |

The Runtime API composes the default through `TryAddScoped` and `EnsureReplacementContract`, rejecting duplicate registrations. A custom host replaces this contract to supply its genuine current request authority, preserving separate structure/value/payload decisions and cancellation. An empty/untrusted subject is not an allow-all identity. The explicit development helper remains in the Runtime API package and is not registered as a production fallback.

## Implementable contributor interfaces

None. This package defines a single-implementation authorization seam, not additive authority.

## Events

None. The existing Runtime API policy implementation remains the semantic owner; extracting its contract changes no permission or runtime-execution rule.
