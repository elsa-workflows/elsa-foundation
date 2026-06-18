# Extension points - Foundation Identity domain

The Foundation Identity Abstractions feature owns the provider-agnostic authentication, IAM, authorization, ownership, and security-default seams. Concrete OIDC, OpenIddict, ASP.NET Core Identity, and legacy Elsa Identity providers implement these contracts from sibling modules; none are implemented here.

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| `IAuthenticationProviderManager` | `DefaultAuthenticationProviderManager` (`Elsa.Foundation.Identity.Abstractions`) | The host needs tenant-aware provider discovery beyond registered `IAuthenticationProviderModule` instances. |
| `IOwnershipModeProvider` | `OptionsOwnershipModeProvider` (`Elsa.Foundation.Identity.Abstractions`) | Ownership mode is resolved from tenant configuration or another dynamic source. |
| `IEffectiveCapabilitiesResolver` | `DefaultEffectiveCapabilitiesResolver` (`Elsa.Foundation.Identity.Abstractions`) | The host needs additional capability gates beyond ownership mode + provider capability support. |
| `IPermissionCatalog` | `DefaultIdentityPermissionCatalog` (`Elsa.Foundation.Identity.Abstractions`) | The application supplies a broader permission catalog while preserving the namespaced identity keys. |
| `IPermissionEvaluator` | `ClaimsPermissionEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | Permissions are evaluated server-side from stores/caches instead of, or in addition to, normalized claims. |
| `IClaimsNormalizer` | `DefaultClaimsNormalizer` (`Elsa.Foundation.Identity.Abstractions`) | A provider needs custom claim projection while still emitting normalized Elsa role/permission claims. |
| `IClaimMappingRuleEvaluator` | `ClaimMappingRuleEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | Mapping rules need richer matching than exact claim-type/value comparisons. |
| `ISecurityDefaultGuardEvaluator` | `SecurityDefaultGuardEvaluator` (`Elsa.Foundation.Identity.Abstractions`) | A host needs custom aggregation/reporting of security-default guard results. |

## Implementable contributor interfaces

### `IAuthenticationProviderModule`

- **Kind:** Contributor (registered provider module; one implementation per configured provider family/profile).
- **Register:** `services.AddScoped<IAuthenticationProviderModule, MyProviderModule>()`.
- **Consumed by:** `DefaultAuthenticationProviderManager`, which composes enabled provider descriptors for sign-in and capability discovery.
- **Known implementations:** none yet in this PR. Future OIDC/OpenIddict/legacy modules implement this contract *(cross-domain provider modules)*.

### `IPermissionResourceHandler`

- **Kind:** Contributor (resource-specific authorization decision hook).
- **Register:** `services.AddScoped<IPermissionResourceHandler, MyResourceHandler>()`.
- **Consumed by:** `PermissionAuthorizationHandler` before falling back to `IPermissionEvaluator`.
- **Known implementations:** none yet in this PR. Feature modules can contribute resource-specific checks while preserving backend source-of-truth enforcement *(cross-domain)*.

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
