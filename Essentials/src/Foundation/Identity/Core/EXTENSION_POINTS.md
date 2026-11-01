# Extension points - Foundation Identity domain

The Foundation Identity Abstractions feature owns the provider-agnostic authentication, IAM, authorization, and ownership seams. Concrete OIDC, OpenIddict, ASP.NET Core Identity, and legacy Elsa Identity providers implement these contracts from sibling modules; none are implemented here. The contracts live in `Elsa.Foundation.Identity.Core`; the feature itself and the default implementations named below live in the domain-root `Elsa.Foundation.Identity` project.

## Overridable contracts

| Contract | Default impl | Override when |
|---|---|---|
| `IAuthenticationProviderResolver` | `DefaultAuthenticationProviderResolver` (`Elsa.Foundation.Identity`) | The host needs tenant-aware provider discovery beyond registered `IAuthenticationProviderModule` instances. |
| `IOwnershipModeProvider` | `OptionsOwnershipModeProvider` (`Elsa.Foundation.Identity`) | Ownership mode is resolved from tenant configuration or another dynamic source. |
| `IEffectiveCapabilitiesResolver` | `DefaultEffectiveCapabilitiesResolver` (`Elsa.Foundation.Identity`) | The host needs additional capability gates beyond ownership mode + provider capability support. |
| `IPermissionCatalog` | `CompositePermissionCatalog` (`Elsa.Foundation.Identity`) | The application replaces the whole catalog surface with `ReplacePermissionCatalog<T>()`. To *add* permissions, contribute an `IPermissionContributor` (below); the composite aggregates the active provider's contributions into one immutable snapshot. |
| `IPermissionEvaluator` | `ClaimsPermissionEvaluator` (`Elsa.Foundation.Identity`) | Permissions are evaluated server-side from stores/caches instead of, or in addition to, normalized claims. Replace it with `ReplacePermissionEvaluator<T>()`; the shared handler still owns single/any/all and resource precedence. |
| `IPermissionAuthorizationService` | `PermissionAuthorizationService` (`Elsa.Foundation.Identity`) | A first-party service, request context, or feature API needs one canonical asynchronous decision outside endpoint middleware. It validates the trusted normalized principal, preserves tenant/resource context, applies resource-handler precedence, and delegates to the replaceable evaluator. |
| `ICurrentSubjectPermissionAuthorizer` | `UnsupportedCurrentSubjectPermissionAuthorizer` (`Elsa.Foundation.Identity`); ASP.NET Core Identity supplies `AspNetCoreIdentitySubjectAuthorizer` | Deferred work with no incoming principal needs a fresh decision for one persisted subject. The default fails closed as `Unsupported`, and no saved grant is reused. A provider that owns current users, memberships and roles takes over the default through `UseCurrentSubjectPermissionAuthorizerProvider<T>()`. A host's own `ReplaceCurrentSubjectPermissionAuthorizer<T>()` always wins. Store read failures propagate so callers keep their work owed. |
| `IPermissionPolicyNameFormatter` | `PermissionPolicyNameFormatter` (`Elsa.Foundation.Identity`) | A compatibility host needs to parse an additional single-permission policy identity. Replace it with `ReplacePermissionPolicyNameFormatter<T>()`; new Elsa metadata always emits the canonical v1 grammar. |
| `IAuthSessionService` | `ClaimsAuthSessionService` (`Elsa.Foundation.Identity.Api`) | The host needs to enrich the provider-agnostic Studio session from server-side state beyond normalized claims. |
| `IClaimsNormalizer` | `DefaultClaimsNormalizer` (`Elsa.Foundation.Identity`) | A provider needs custom claim projection while still emitting normalized Elsa role/permission claims. |
| `IClaimMappingRuleEvaluator` | `ClaimMappingRuleEvaluator` (`Elsa.Foundation.Identity`) | Mapping rules need richer matching than exact claim-type/value comparisons. |
| `IUserStore`, `IRevisionAwareUserStore`, `IPagedUserStore` | `EfUserStore`; opt-in `EfUserStore` | Select one tenant-local user authority while preserving normalized lookup, conditional email uniqueness, aggregate writes and deletes, stable bounded enumeration, and optimistic concurrency. |
| `IRoleStore`, `IRevisionAwareRoleStore`, `IPagedRoleStore` | `EfRoleStore`; opt-in `EfRoleStore` | Select one tenant-local role authority while preserving stable bounded enumeration, reservations, and optimistic concurrency. |
| `IClaimMappingStore`, `IRevisionAwareClaimMappingStore`, `IPagedClaimMappingStore` | `EfClaimMappingStore`; opt-in `EfClaimMappingStore` | Select one claim-mapping authority while preserving provider-local stable ordering and revision behavior. |
| `IExternalIdentityStore`, `IRevisionAwareExternalIdentityStore`, `IPagedExternalIdentityStore` | `EfExternalIdentityStore`; opt-in `EfExternalIdentityStore` | Select one external-login authority while preserving subject uniqueness, ownership/rebind rules, bounded ordering, and atomic owner updates. |
| `ITenantMembershipStore`, `IRevisionAwareTenantMembershipStore` | `EfTenantMembershipStore`; opt-in `EfTenantMembershipStore` | Select one membership authority while preserving tenant isolation, lossless sets, and atomic user ownership. |
| `IApplicationStore` | `EfApplicationStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`); opt-in `EfApplicationStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`) | A host selects a different tenant-local application backend while preserving lossless records, normalized lookup, and unconditional upsert semantics. The EF IAM feature selects this together with the complete IAM authority. |
| `IRevisionAwareApplicationStore` | The same scoped EF application store selected for `IApplicationStore` | A replacement must preserve create-only saves, opaque revisions, atomic compare-and-swap, and conflict/not-found distinctions. |
| `ICredentialStore` | `EfCredentialStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`); opt-in `EfCredentialStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`) | A host selects a different tenant-local credential backend while preserving hashed-secret-only persistence, lossless records, normalized lookup, and unconditional upsert semantics. The EF IAM feature selects this together with the complete IAM authority. |
| `IRevisionAwareCredentialStore` | The same scoped EF credential store selected for `ICredentialStore` | A replacement must preserve create-only saves, opaque revisions, atomic compare-and-swap, and conflict/not-found distinctions. |
| `IProviderConfigurationStore` | `EfProviderConfigurationStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`); opt-in `EfProviderConfigurationStore` (`Elsa.Foundation.Identity.Persistence.EntityFrameworkCore`) | A host selects a different durable provider-configuration backend while preserving tenant/global access, effective fallback, and unconditional upsert semantics. The EF feature replaces only this contract and its revision-aware companion. |
| `IRevisionAwareProviderConfigurationStore` | The same scoped EF provider-configuration store selected for `IProviderConfigurationStore` | A replacement must preserve create-only saves, opaque revisions, atomic compare-and-swap, and conflict/not-found distinctions. |

## Implementable contributor interfaces

### `IAuthenticationProviderModule`

- **Kind:** Contributor (registered provider module; one implementation per configured provider family/profile).
- **Register:** `services.AddScoped<IAuthenticationProviderModule, MyProviderModule>()`.
- **Consumed by:** `DefaultAuthenticationProviderResolver`, which composes enabled provider descriptors for sign-in and capability discovery.
- **Known implementations:** `OidcAuthenticationProviderModule` (`Elsa.Foundation.Identity.Oidc`) and `OpenIddictAuthenticationProviderModule` (`Elsa.Foundation.Identity.OpenIddict`) *(cross-domain provider modules)*.

### `IPermissionContributor`

- **Kind:** Contributor (feature-owned permission contribution to the shared catalog).
- **Register:** `services.AddPermissionContributor<MyContributor>()` (or `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, MyContributor>())`).
- **Consumed by:** `CompositePermissionCatalog`, which canonicalizes keys for lookup while retaining declared spelling and provenance. The default identity permissions are contributed by `DefaultIdentityPermissionCatalog`; keys that are not one concrete `{resource}:{verb}` permission, canonical duplicates, padded keys, and wildcard definitions fail during catalog construction with both ownership sources in the diagnostic. The catalog carries no implications; see [Elsa 3.9 to Elsa 4 permission mapping](../../../../../docs/reference/elsa3-permission-mapping.md) for the matching rules.
- **Known implementations:** `DefaultIdentityPermissionCatalog` (identity permissions), `ModuleManagementPermissionContributor` (`Elsa.Modularity.Api`), `ExtensionBuilderPermissionContributor` (`Elsa.ExtensionBuilder.Api`, optional) — the two host-control features that own the `module-management` / `extension-builder` permissions per ADR 0037 *(cross-domain)*; `EditorialReviewPermissionContributor` (`Elsa.Agent.Specialists.Editorial.Api`) for `editorial-reviews:read` / `editorial-reviews:decide` *(cross-domain)*.

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
The evaluator, authorization service, current-subject authorizer, formatter, and catalog are single replacement contracts: use their `Replace*`
methods before or after Foundation registration. Direct competing registrations are rejected rather
than selected by registration order. `IPermissionContributor` and `IPermissionResourceHandler`
remain additive fan-in seams.

### Async authorization contexts

Feature APIs that need authorization during request handling should depend on their asynchronous
authorization context (`IActivityAuthoringContextAsync`, `IActivityDependencyContextAsync`,
or `IActivityInspectionContextAsync`) and call the async methods with the
request cancellation token. These contexts delegate decisions to `IPermissionAuthorizationService`;
they do not inspect permission claims themselves. Provider-specific or resource-specific rules receive
a stable resource object and remain in `IPermissionResourceHandler` implementations.

The architecture suite's `PermissionAuthorizationBoundaryTests` checks this in the compiled IL of every
packable runtime library. A method fails it when it passes a permission value to a claims lookup
(`FindFirst`, `FindFirstValue`, `FindAll`, `HasClaim`, including static helpers of those names that take
a principal or identity), hands a principal's claims (its `Claims` or `Identities`, a `FindAll` result,
a list, array, lookup or grouping of the claims themselves or a helper result built from them, or their
values projected with `Select(claim => claim.Value)`) to a call together with a permission value, passes
a permission value to a helper that does either with it, or calls a FastEndpoints
`Permissions`/`PermissionsAll` helper. A permission value is the permission claim type, a key a
permission contributor adds to the catalog (the test constructs every `IPermissionContributor` and reads
what it contributes, and fails if one cannot be constructed), a namespaced constant whose own name
contains `Permission`, a string, string-collection or delegate field so named, a static
string-collection or delegate field of a permission-named type, or a string, string collection or
delegate assigned from one or returned by a call that takes one. A claims lambda counts when it loads a
permission value or compares a claim's own type, value or issuer with one, directly or through a scanned
helper's parameter; using the result of an `IPermissionAuthorizationService` decision does not. Other
constants of a permission-named type, such as an owner id, and the bare `*` are not permission values.
Only the methods on the reviewed list in that test may read permission claims: the claims evaluator, the
claims normalizer (which strips incoming internal claims), token issuance, the two session projections
and the identity projection. `PermissionBypassScanner` lists what the scan does not follow (array,
collection and tuple elements, claims enumerated by a loop, lookups or groupings built with an element
selector, a claims collection handed to a helper, values passed through a `StringBuilder` or an async
method's `Task`, `ref`/`out` write-backs, and calls through interfaces, virtual methods or delegates);
review still covers those.

The original synchronous context interfaces and their legacy adapters were removed by #2566 (ADR 0080
D4); the async contexts are the only request authorization contexts.

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

## Opt-in external bearer normalization

`Elsa.Foundation.Identity.Oidc` consumes `IClaimMappingStore` and `IClaimsNormalizer` for its default-false fixed-host bearer bridge. The store is explicitly supplied through the existing replacement-contract registration; opt-in activation requires exactly one normalizer descriptor. It filters internal input claims before matching and validates the single output identity, without changing shared normalizer semantics. It does not invoke `IPrincipalFactory` or mutate user/link stores. See the [owning OIDC README](../Oidc/README.md) and [canonical bearer contract](../../../../../specs/190-worker-oidc-normalization/contracts/bearer-normalization.md) for supported callbacks, ordinary persistence-scope agreement and trust enrollment.

## ASP.NET Core Identity login presentation

The optional `LoginPageOptions.Renderer` callback is a single presentation replacement owned by
`Elsa.Foundation.Identity.AspNetCoreIdentity` (Layer 3), not a provider-neutral authentication contract.
Its default is `LoginPage.Render`, configured through `Identity:Login`. Register once with
`PostConfigure<LoginPageOptions>`; it receives a `LoginPageContext` after return URL sanitization and
antiforgery issuance. Keeping `FormHtml` retains the standard POST fields. The login endpoint consumes
the callback; credential/CSRF/return-url validation remains endpoint-owned. No event or contributor is
registered. See the [branding guide](../../../../../Studio/docs/branding.md#replace-login-presentation).
