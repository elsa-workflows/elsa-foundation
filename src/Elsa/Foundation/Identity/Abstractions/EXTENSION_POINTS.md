# Extension points - Foundation Identity domain

The Foundation Identity Abstractions feature owns the provider-agnostic authentication, IAM, authorization, ownership, and security-default seams. Concrete OIDC, OpenIddict, ASP.NET Core Identity, and legacy Elsa Identity providers implement these contracts from sibling modules; none are implemented here.

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| `IAuthenticationProviderResolver` | `DefaultAuthenticationProviderResolver` (`Elsa.Foundation.Identity.Abstractions`) | The host needs tenant-aware provider discovery beyond registered `IAuthenticationProviderModule` instances. |
| `IOwnershipModeProvider` | `OptionsOwnershipModeProvider` (`Elsa.Foundation.Identity.Abstractions`) | Ownership mode is resolved from tenant configuration or another dynamic source. |
| `IEffectiveCapabilitiesResolver` | `DefaultEffectiveCapabilitiesResolver` (`Elsa.Foundation.Identity.Abstractions`) | The host needs additional capability gates beyond ownership mode + provider capability support. |
| `IPermissionCatalog` | `CompositePermissionCatalog` (`Elsa.Foundation.Identity.Abstractions`) | The application replaces the whole catalog surface with `ReplacePermissionCatalog<T>()`. To *add* permissions, contribute an `IPermissionContributor` (below); the composite aggregates the active provider's contributions into one immutable snapshot. |
| `IPermissionEvaluator` | `ClaimsPermissionEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | Permissions are evaluated server-side from stores/caches instead of, or in addition to, normalized claims. Replace it with `ReplacePermissionEvaluator<T>()`; the shared handler still owns single/any/all and resource precedence. |
| `IPermissionAuthorizationService` | `PermissionAuthorizationService` (`Elsa.Foundation.Identity.Abstractions`) | A first-party service, request context, or feature API needs one canonical asynchronous decision outside endpoint middleware. It validates the trusted normalized principal, preserves tenant/resource context, applies resource-handler precedence, and delegates to the replaceable evaluator. |
| `IPermissionPolicyNameFormatter` | `PermissionPolicyNameFormatter` (`Elsa.Foundation.Identity.Abstractions`) | A compatibility host needs to parse an additional single-permission policy identity. Replace it with `ReplacePermissionPolicyNameFormatter<T>()`; new Elsa metadata always emits the canonical v1 grammar. |
| `IAuthSessionService` | `ClaimsAuthSessionService` (`Elsa.Foundation.Identity.Api`) | The host needs to enrich the provider-agnostic Studio session from server-side state beyond normalized claims. |
| `IClaimsNormalizer` | `DefaultClaimsNormalizer` (`Elsa.Foundation.Identity.Abstractions`) | A provider needs custom claim projection while still emitting normalized Elsa role/permission claims. |
| `IClaimMappingRuleEvaluator` | `ClaimMappingRuleEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | Mapping rules need richer matching than exact claim-type/value comparisons. |
| `ISecurityDefaultGuardEvaluator` | `SecurityDefaultGuardEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | A host needs custom aggregation/reporting of security-default guard results. |

## Implementable contributor interfaces

### `IAuthenticationProviderModule`

- **Kind:** Contributor (registered provider module; one implementation per configured provider family/profile).
- **Register:** `services.AddScoped<IAuthenticationProviderModule, MyProviderModule>()`.
- **Consumed by:** `DefaultAuthenticationProviderResolver`, which composes enabled provider descriptors for sign-in and capability discovery.
- **Known implementations:** `OidcAuthenticationProviderModule` (`Elsa.Foundation.Identity.Oidc`) and `OpenIddictAuthenticationProviderModule` (`Elsa.Foundation.Identity.OpenIddict`) *(cross-domain provider modules)*.

### `IPermissionContributor`

- **Kind:** Contributor (feature-owned permission contribution to the shared catalog).
- **Register:** `services.AddPermissionContributor<MyContributor>()` (or `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, MyContributor>())`).
- **Consumed by:** `CompositePermissionCatalog`, which canonicalizes keys for lookup while retaining declared spelling and provenance. The default identity permissions are contributed by `DefaultIdentityPermissionCatalog`; canonical duplicates, padded keys, wildcard definitions, and wildcard implication targets fail during catalog construction with both ownership sources in the diagnostic.
- **Known implementations:** `DefaultIdentityPermissionCatalog` (identity permissions), `ModuleManagementPermissionContributor` (`Elsa.Modularity.Api`), `ExtensionBuilderPermissionContributor` (`Elsa.Modularity.ExtensionBuilder`) — the two host-control features that own `module-management.*` / `extension-builder.*` permissions per ADR 0037 *(cross-domain)*.

### `IPermissionResourceHandler`

- **Kind:** Contributor (resource-specific authorization decision hook).
- **Register:** `services.AddScoped<IPermissionResourceHandler, MyResourceHandler>()`.
- **Consumed by:** the shared single/any/all permission handler before falling back to `IPermissionEvaluator`. For one member, every resource source runs in registration order: any denial vetoes grants; otherwise any grant succeeds; the evaluator runs only after unanimous abstention. Exceptions, timeouts, and request cancellation propagate and stop later sources. The protected resource is preserved, while the active HTTP request's `RequestAborted` token is supplied through both evaluator APIs.
- **Known implementations:** none yet in this PR. Feature modules can contribute resource-specific checks while preserving backend source-of-truth enforcement *(cross-domain)*.

## Endpoint permission metadata and trusted principals

Minimal APIs and other standard ASP.NET Core endpoint builders use `RequirePermission(...)`,
`RequireAnyPermission(...)`, or `RequireAllPermissions(...)`. Each extension emits one canonical
`Elsa.Permission:v1:...` authorization policy. Legacy `Elsa.Permission:<permission>` names remain
parse-only aliases for the compatibility window; malformed reserved v1 names fail closed.

Permission policies accept exactly one authenticated identity whose runtime `AuthenticationType`
is registered with `AddNormalizedAuthenticationType(...)` and which carries exactly one
`elsa.identity.normalized = v1` marker. Provider packages register their observed post-authentication
types only after guaranteeing strip-map-mark projection. Untrusted, unmarked, or ambiguous
authenticated callers are challenged; a trusted normalized caller with no grant is forbidden.

Foundation wraps one pre-existing `IAuthorizationMiddlewareResultHandler` (or the ASP.NET Core
default) and delegates every unrelated outcome. Register a host result handler before Foundation;
multiple prior handlers fail immediately and a handler added afterward fails startup validation.
The evaluator, authorization service, formatter, and catalog are single replacement contracts: use their `Replace*`
methods before or after Foundation registration. Direct competing registrations are rejected rather
than selected by registration order. `IPermissionContributor` and `IPermissionResourceHandler`
remain additive fan-in seams.

### Async authorization context replacement window

Feature APIs that need authorization during request handling should depend on their asynchronous
context sibling (`IActivityAuthoringContextAsync`, `IActivityDependencyContextAsync`,
or `IActivityInspectionContextAsync`) and call the async methods with the
request cancellation token. These contexts delegate decisions to `IPermissionAuthorizationService`;
they do not inspect permission claims themselves. Provider-specific or resource-specific rules receive
a stable resource object and remain in `IPermissionResourceHandler` implementations.

The original synchronous context interfaces remain source-compatible during the advisory replacement
window for external hosts, but the built-in HTTP adapters mark their permission members obsolete and
fail closed rather than blocking on asynchronous work. First-party production callers are migrated to
the async siblings. The synchronous members are candidates for removal in the next major release;
hosts should migrate replacements before then.

### `ISecurityDefaultGuard`

- **Kind:** Validator (action-named contributor that returns startup/configuration violations).
- **Register:** `services.AddScoped<ISecurityDefaultGuard, MySecurityGuard>()`.
- **Consumed by:** `SecurityDefaultGuardEvaluator`, which returns all violations rather than swallowing failures.
- **Known implementations:** `SigningKeySecurityDefaultGuard`, `HttpsMetadataSecurityDefaultGuard`, `SecretHashSecurityDefaultGuard` *(intra-domain - default)*.

## Events

### `AuthEvent`

- **Kind:** Audit event abstraction (record emitted through `IAuthEventSink`; no concrete dispatcher is selected in this PR).
- **Purpose:** Authentication, authorization, identity-resource, and credential lifecycle changes publish structured, non-sensitive audit details.
- **Known implementations:** none yet in this PR. Provider/management modules emit events through `IAuthEventSink`; audit infrastructure supplies the sink *(cross-domain)*.

---

## Constitutional basis

- Framework §2.6.1 — contributor interfaces and single aggregation points.
- Framework §2.6.2 — replacement contracts for single active implementations.
- Framework §2.22.1 — per-domain extension-point catalog.
- Framework §2.23 — registration and implementation unit-test obligations.
