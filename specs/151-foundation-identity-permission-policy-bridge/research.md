# Phase 0 Research — Foundation Identity permission policy bridge

Baseline: `origin/main` at `092440091d595e31ff4557ccfc0d4bf271de8ec0`. Codebase-memory reconnaissance found 175 production `ConfigurePermissions(...)` uses across 121 files, funneled through six Elsa endpoint bases.

## Decision 1 — Identity Abstractions owns the contract; FastEndpoints is one-way adapter

- **Decision**: Put the canonical codec, structured requirement model, and `IEndpointConventionBuilder` extensions in `Elsa.Foundation.Identity.Abstractions`. Add `Elsa.Api.FastEndpoints -> Elsa.Foundation.Identity.Abstractions`.
- **Rationale**: Identity already owns `IAuthorizationPolicyProvider`, evaluator, catalog, implication, normalization, and resource handlers. Identity Abstractions already references `Microsoft.AspNetCore.Authorization`; add `Microsoft.AspNetCore.Authorization.Policy` for middleware result handling and the full `Microsoft.AspNetCore.Http` package for endpoint metadata, `IHttpContextAccessor`, and `AddHttpContextAccessor`, without adding a framework reference or FastEndpoints dependency.
- **Rejected**: Identity API (cycle: it already references FastEndpoints); Identity AspNetCore (unnecessary public package); `Elsa.Http.Core` (wrong domain); a new `Elsa.Api.Authorization` project (no independent contract); FastEndpoints-owned `Require*` methods (blocks Minimal API neutrality).

## Decision 2 — Preserve single-permission public interfaces and add a structured companion

- **Decision**: Keep `IPermissionEvaluator`, `IPermissionResourceHandler`, `IPermissionPolicyNameFormatter`, `PermissionEvaluationContext`, `RequirePermissionAttribute`, and `PermissionAuthorizationRequirement(string Permission)` compatible. Add `PermissionRequirementMode`, a composite requirement/descriptor, and a structured codec interface. One shared internal per-member evaluator backs both single and composite authorization handlers.
- **Rationale**: Evaluator and resource implementations already consume one permission and one resource. Single-member evaluation is the smallest stable primitive; the policy handler owns any/all composition. A separate composite requirement avoids changing the existing record constructor/property or forcing custom evaluators to understand endpoint boolean composition.
- **Any/all**: `Any` succeeds when one final member succeeds. `All` succeeds only when every final member succeeds. Duplicate canonical members collapse and order is deterministic.

## Decision 3 — Resource denial is a member-local hard veto

For each permission member:

1. Invoke resource handlers in deterministic registration order while they return normal decisions.
2. Any explicit denial marks that member denied, even if another handler grants.
3. Otherwise any resource grant marks the member granted and skips the general evaluator.
4. Only unanimous abstention calls `IPermissionEvaluator`.
5. Compose the final member outcomes using single/any/all semantics.

A denied member fails `All` but does not veto another member that satisfies `Any`. The first exception, timeout, or cancellation short-circuits that member and the entire requirement; no later resource handler, member, or general evaluator is consulted. This replaces the current ambiguous behavior where a later grant/evaluator can shadow a resource denial. The existing `DoesNotShadowLaterSuccess` expectation becomes a hard-veto regression test, and a throw-first/grant-second fixture proves operational failures cannot fall through.

## Decision 4 — Operational failures propagate; normal denials do not

- Authentication and provider-specific claim normalization together establish a usable normalized Elsa principal. `DefaultClaimsNormalizer` strips any incoming internal marker and emits `IdentityClaimTypes.Normalized = "v1"` only after provider/tenant-scoped mapping succeeds. All first-party principal/token factories emit or preserve that marker only after their trusted projection completes.
- A marker alone is not trusted. Foundation Identity options contain an initially empty, ordinal set of normalized runtime authentication types. `AddNormalizedAuthenticationType` registers the exact post-authentication `ClaimsIdentity.AuthenticationType`, not merely an ASP.NET scheme registration name. The ASP.NET Core Identity package registers `"Elsa.Foundation.Identity"` for its normalized external principal factory and `AspNetCoreIdentityDefaults.CookieScheme` for reconstructed cookie requests. The OpenIddict package registers `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` for validated bearer requests; the token-creation identity type `"openiddict"` is not an endpoint principal and is not trusted. A custom provider explicitly registers its observed post-authentication type only after it guarantees the same strip-map-mark contract.
- At request time an internal normalized-principal validator requires exactly one authenticated identity whose runtime type is registered and which carries exactly one valid marker. Zero or multiple matches fail closed; trusted identities are never unioned across tenants/providers. The handler passes a new principal containing only the one selected identity to resource handlers/evaluators, so raw or forged claims on another identity cannot leak into a replacement evaluator.
- The Foundation permission policy includes an internal normalized-principal requirement. The shared permission handler refuses a principal with no trusted normalized identity without consulting grant sources. A policy-aware `IAuthorizationMiddlewareResultHandler` recognizes only a failed policy containing that requirement and an otherwise authenticated but untrusted/unmarked principal, rewrites the result to `PolicyAuthorizationResult.Challenge()`, and delegates it to the captured host/default result handler; it delegates anonymous, normalized, successful, and unrelated-policy results unchanged. “Otherwise authenticated” is determined with `principal.Identities.Any(identity => identity.IsAuthenticated)`, never `principal.Identity`, so identity ordering cannot change 401/403 classification. Because this runs inside authorization middleware after policy selection, it does not depend on endpoint discovery or on `UseAuthentication` ordering.
- No current shipping request-authentication handler calls `IClaimsNormalizer`: cookie requests reconstruct stored projections, bearer requests validate already-issued signed tokens, and `AspNetCoreIdentityPrincipalFactory` is a principal factory rather than an ASP.NET authentication handler. This slice therefore does not add an unused external-provider handler. The real factory must propagate a normalization exception before returning any principal/marker, and a representative ASP.NET authentication handler in the same-host contract fixture must convert that failure to `AuthenticateResult.Fail`, yielding HTTP 401 and no ticket. Any future external-provider request adapter is required to use this tested failure mapping and must never mint the marker on failure.
- Result-handler registration is descriptor-based and idempotent. On the first Foundation registration, zero pre-existing host descriptors selects `AuthorizationMiddlewareResultHandler`, one descriptor is removed and captured by a non-recursive fallback factory, and two or more fail immediately with every concrete/factory/instance descriptor identified. It then installs exactly one tagged Foundation wrapper. Later Foundation registrations see the tag and do nothing (`Foundation -> Foundation`). A host handler must be registered before Foundation to be captured (`host -> Foundation`); adding one afterward (`Foundation -> host`) is an explicit startup conflict named by the validator, not supported order independence.
- A normalized principal with a normal negative result receives 403.
- Evaluator/resource exceptions and timeouts propagate as operational failures, do not become 403, and do not consult later grant sources. Foundation Identity defines no implicit timeout budget; a host or implementation that needs one owns and documents it and must surface expiration as an operational failure.
- For HTTP endpoint authorization, the shared handler obtains the active request from `AuthorizationHandlerContext.Resource` when it is an `HttpContext`, otherwise from injected `IHttpContextAccessor`. It records that request's `RequestAborted` on the additive `PermissionEvaluationContext.CancellationToken` property and passes the same token to every resource handler/evaluator call, while preserving the original `AuthorizationHandlerContext.Resource` as the protected resource. When neither source supplies an HTTP context, direct `IAuthorizationService` calls use `CancellationToken.None`; direct interface callers retain the existing ability to pass their own token. `OperationCanceledException` propagates.

This is fail-closed without hiding an authorization-store outage as a caller permission problem.

## Decision 5 — One canonical key identity, presentation preserved

`PermissionKey.Normalize` performs Unicode NFC normalization followed by invariant uppercase conversion. Canonical keys use ordinal comparison everywhere policy identity or authorization equality matters: declarations, catalog indexes/implications, normalized-claim comparison, evaluator inputs, and wildcard checks.

Presentation remains compatible: catalog `Permission.Key` values and emitted normalized claims retain the declared spelling; indexes and comparisons use canonical material. Thus existing JSON/token/session contracts do not silently switch every lowercase permission to uppercase, while canonically equivalent spellings authorize identically.

The reserved wildcard is canonical `*`. It is never cataloged or implied. An explicit `*` grant satisfies any ordinary permission; only explicit `*` satisfies a request for `*`.

## Decision 6 — Canonical v1 policy identity plus a parse-only legacy window

Canonical grammar:

```text
Elsa.Permission:v1:<mode>:<tokens>
mode = s | a | l
token = unpadded base64url(UTF-8(canonical permission key))
```

Any/all members are de-duplicated and sorted by decoded canonical key. The codec distinguishes `NotPermission`, `Valid`, and `MalformedReservedPolicy`; malformed v1 names—including mixed-case namespace/version markers—fail closed and never delegate or become legacy permissions.

`IPermissionPolicyNameFormatter` remains unchanged. The default formatter emits v1 single names and accepts canonical or legacy single input. A new structured codec handles single/any/all descriptors. The provider parses structured v1 first, then a host-supplied legacy/custom single formatter, then delegates unrelated policies to its preserved fallback provider.

Legacy `Elsa.Permission:<permission>` is accepted for one major-version deprecation window but never emitted by new metadata. This is the replacement path for the existing formatter/attribute contract.

## Decision 7 — Standard metadata without Authorization.Policy dependency

`RequirePermission`, `RequireAnyPermission`, and `RequireAllPermissions` extend `IEndpointConventionBuilder` and attach an `AuthorizeAttribute` containing one canonical policy name through `builder.Add(...)`. This is standard ASP.NET Core authorization metadata and requires only ASP.NET Core authentication/HTTP abstractions in addition to the existing authorization package.

The Foundation provider builds policies with `RequireAuthenticatedUser()` plus the appropriate requirement, ensuring middleware produces 401 for anonymous callers and 403 for normalized authenticated denials.

## Decision 8 — FastEndpoints uses exactly one policy

Keep all `ConfigurePermissions(params string[])` endpoint calls and the public `ElsaEndpointPermissions.Compose(...)` helper. Add a canonical policy helper used by the six bases:

- no action permissions -> single `*` policy;
- one or more action permissions -> one `Any` policy containing `*` plus the actions.

Each base calls FastEndpoints `Policies(onePolicyName)`, never `Permissions(...)`. Multiple ASP.NET policy names would compose as AND and would break the existing wildcard/action OR contract.

`IdentityClaimTypeFastEndpointsConfigurator` remains during transition for non-Elsa/third-party FastEndpoints routes; Elsa endpoint bases no longer rely on it for authorization.

## Decision 9 — Catalog owner and contributor provenance is immutable snapshot data

Add non-positional `OwnerId` and `ContributorType` init properties to `Permission`, preserving the existing positional constructor. `IPermissionContributor` gains a default owner identity for compatibility; Studio Preferences and Module Management declare stable explicit owner IDs. `CompositePermissionCatalog` stamps provenance, indexes canonical keys, rejects wildcard definitions/implications and cross-owner duplicates, and includes both owners in diagnostics.

Lifecycle is provider-scoped, not a mutable global registry: enabled contributors build one immutable catalog in a shell service provider; disabling, unloading, or replacing a module builds a new provider/catalog, while the old generation drains and is disposed. Tests construct successive providers and prove no stale entries.

Complete endpoint-to-catalog inventory belongs to #1346. This slice proves the ownership contract with Studio Preferences and Module Management.

## Decision 10 — Replacement contracts and additive seams stay explicit

- Replacements: `IPermissionEvaluator`, `IPermissionPolicyNameFormatter`, `IPermissionCatalog`.
- Additive fan-in: `IPermissionContributor`, `IPermissionResourceHandler`.

The official registration surface provides `ReplacePermissionEvaluator<T>()`, `ReplacePermissionPolicyNameFormatter<T>()`, and `ReplacePermissionCatalog<T>()`. Defaults and explicit replacements each install an internal registration marker containing the contract and implementation type. Each `Replace*` method removes the contract descriptors and its prior marker, then installs exactly one replacement plus marker with the documented lifetime; calling it before or after `AddFoundationIdentityAbstractions()` is supported and idempotent for the same selection. `AddFoundationIdentityAbstractions()` rejects a pre-existing untagged descriptor instead of mistaking it for an explicit replacement. A startup validator resolves the three replacement sets and requires exactly one descriptor whose concrete type matches exactly one marker, so zero, multiple, or direct post-default registrations fail with named diagnostics rather than becoming last-write-wins. The two additive seams continue enumerable resolution by design and are documented in `EXTENSION_POINTS.md`.

## Decision 11 — Evidence placement and FastEndpoints isolation

- Identity contract/provider/evaluator/normalizer and trusted-runtime-type matrix: `AuthorizationContractsTests.cs`, `ClaimsNormalizationTests.cs`, plus end-to-end cookie and OpenIddict bearer tests that assert the post-authentication `AuthenticationType` and authorize through the bridge. The external principal-factory test covers its `"Elsa.Foundation.Identity"` type before cookie projection.
- Replacement and authorization-result registration matrix: `ReplacementContractRegistrationTests.cs`, covering default, explicit-before/after, direct-before/after, zero, multiple, repeated Foundation registration, host-result-before, no-host/default, host-result-after conflict, challenge/forbid delegation, and unrelated-policy delegation.
- Same-host Minimal API/FastEndpoints single/any/all matrix: `PermissionEndpointAdapterIntegrationTests.cs` in the Identity test project, using `[Collection(FastEndpointsHostCollection.Name)]`, one host at a time, and exact endpoint assembly filters.
- Adapter helper: `ElsaEndpointPermissionsTests.cs`.
- Catalog owner/lifecycle canaries: `PermissionCatalogOwnershipLifecycleTests.cs` in Modularity tests.
- Architecture boundary: use Roslyn symbol analysis (not text matching) over first-party API endpoint/base code to reject FastEndpoints permission helpers and data flow from any permission claim type into an authorization decision. Cover `FindFirst`, `FindFirstValue`, `FindAll`, `HasClaim`, `Claims.Any`, aliases, and provider-specific permission constants with separate mutation fixtures. Keep a symbol/path-scoped reviewed allowlist for Identity token/session projection and the two transport contexts explicitly deferred to #1356. The complete endpoint/catalog inventory remains #1346.

## Explicitly deferred discovery

`HttpContextActivityDesignAuthorizationContext` and `HttpContextActivityExecutionInspectionAuthorizationContext` perform direct synchronous permission-claim matching outside the endpoint-base adapter. Migrating their contracts without sync-over-async is separate program follow-up [#1356](https://github.com/elsa-workflows/elsa-foundation/issues/1356), blocked by #1344 and required before final retirement planning closes.

## Open questions

None blocking. Concrete type names may change during implementation, but the package placement, compatibility surface, policy grammar, evaluation order, and evidence matrix are fixed by the specification and contracts.
