# Contract — Foundation Identity permission policies

## Endpoint declaration surface

```csharp
TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
    where TBuilder : IEndpointConventionBuilder;

TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
    where TBuilder : IEndpointConventionBuilder;

TBuilder RequireAllPermissions<TBuilder>(this TBuilder builder, params string[] permissions)
    where TBuilder : IEndpointConventionBuilder;
```

Each method attaches one standard ASP.NET Core `AuthorizeAttribute` policy metadata entry and returns the same builder. Null, empty, whitespace-padded, or empty-set declarations fail during mapping/configuration. Duplicate canonical members collapse.

## Canonical policy fixtures

| Declaration | Policy name |
|---|---|
| single `read` | `Elsa.Permission:v1:s:UkVBRA` |
| any `write`, `read` | `Elsa.Permission:v1:a:UkVBRA.V1JJVEU` |
| all `write`, `read` | `Elsa.Permission:v1:l:UkVBRA.V1JJVEU` |
| single `réad` or decomposed equivalent | `Elsa.Permission:v1:s:UsOJQUQ` |
| single `*` | `Elsa.Permission:v1:s:Kg` |

Namespace and version-marker recognition is case-insensitive. Generation uses the exact casing above. Malformed v1 case variants fail closed and never become legacy aliases.

Legacy `Elsa.Permission:<permission>` names are accepted as single-permission input for one documented major-version window. New attributes, formatters, and endpoint metadata emit only v1.

## Provider contract

Exactly one registered Foundation Identity `IAuthorizationPolicyProvider` owns canonical and legacy permission names. It:

1. parses canonical v1;
2. rejects malformed reserved v1 input;
3. accepts legacy/custom single input through the compatible formatter;
4. builds a policy with `RequireAuthenticatedUser()` and the shared requirement;
5. delegates unrelated named/default/fallback policies to the preserved host provider with its lifetime intact.

Endpoint adapters register no policy provider.

## Evaluation contract

The existing `IPermissionEvaluator` and `IPermissionResourceHandler` remain single-permission interfaces. Foundation Identity owns any/all composition.

For each member, resource handlers run in deterministic registration order; resource denial vetoes resource grant/evaluator for that member; resource grant wins only if no denial; evaluator runs only after unanimous abstention. Any and all compose final member outcomes. The first exception, timeout, or cancellation short-circuits the whole requirement and propagates without consulting later handlers, members, or the evaluator. Foundation Identity owns no implicit timeout budget.

`DefaultClaimsNormalizer` strips an incoming `elsa.identity.normalized` marker and emits exactly one fresh `elsa.identity.normalized = v1` marker only after provider/tenant normalization succeeds. Trusted first-party principal/token projectors follow the same rule. A marker is trusted only on an authenticated identity whose exact runtime `AuthenticationType` is in the ordinal `FoundationIdentityOptions.NormalizedAuthenticationTypes` set. The set is empty by default; first-party provider packages and custom adapters use `AddNormalizedAuthenticationType` only after guaranteeing strip-map-mark behavior. ASP.NET Core Identity registers `"Elsa.Foundation.Identity"` and its cookie scheme; OpenIddict registers its validation scheme, not its `"openiddict"` token-construction type. The internal validator requires exactly one matching identity and supplies resource handlers/evaluators a principal containing only that identity. Zero or multiple matches fail closed, excluding raw identities and preventing cross-tenant/provider grant union.

Every Foundation permission policy contains an internal normalized-principal requirement, and the shared handler refuses a principal with no trusted normalized identity without calling grant sources. For an otherwise authenticated untrusted/unmarked principal, a Foundation `IAuthorizationMiddlewareResultHandler` rewrites only a failure containing that requirement to `PolicyAuthorizationResult.Challenge()` and passes it to the captured host/default result handler; it delegates unrelated policies and all other outcomes unchanged. It determines whether any identity is authenticated by scanning `ClaimsPrincipal.Identities`, never from the first `ClaimsPrincipal.Identity`. Therefore anonymous/untrusted/unmarked permission-endpoint callers -> 401 and zero resource/evaluator calls; trusted normalized negative authorization -> 403; success -> endpoint result. The behavior is independent of identity ordering and authentication-versus-routing middleware order because it runs after authorization policy selection.

No shipping request authentication handler currently invokes `IClaimsNormalizer`. `AspNetCoreIdentityPrincipalFactory` must propagate normalization failure before returning any principal or marker. The representative ASP.NET handler in the adapter contract maps that exception to `AuthenticateResult.Fail`, producing 401/no ticket; this is the mandatory contract for any future external-provider request adapter. Cookie and bearer production paths prove their already-projected runtime identities separately.

For HTTP endpoint authorization, the shared handler obtains the active `HttpContext` from the authorization resource when possible and otherwise from `IHttpContextAccessor`, sets the additive `PermissionEvaluationContext.CancellationToken` property to `RequestAborted`, and passes the same token to the existing evaluator/resource-handler method parameter. It does not replace the original protected resource. Direct non-HTTP authorization uses `CancellationToken.None`; direct interface callers can continue passing their own cancellation token. `OperationCanceledException` is not translated.

## Grant rules

- Exact canonical grant succeeds.
- Grant-side implications are transitive and cycle-safe.
- Requested permissions are never expanded upward.
- Explicit `*` grant satisfies ordinary requests.
- Request for `*` requires explicit `*` grant.
- `*` cannot be cataloged or implied.
- Raw provider-specific claims never satisfy a permission.

## Replacement and contribution kinds

- Single replacements: evaluator, formatter, catalog.
- Additive fan-in: permission contributors, resource handlers.
- Defaults and `ReplacePermissionEvaluator<T>()`, `ReplacePermissionPolicyNameFormatter<T>()`, and `ReplacePermissionCatalog<T>()` install an internal marker naming the selected contract/implementation. Each `Replace*` removes all descriptors and markers for that contract, then installs exactly one implementation with its documented lifetime plus one marker. Explicit replacement works before or after Foundation registration.
- `AddFoundationIdentityAbstractions()` rejects an untagged pre-existing replacement descriptor. A startup validator requires exactly one implementation and matching marker for each contract and names every conflicting/mismatched type. Zero, multiple, direct post-default, and remove-then-direct registrations fail; registration order never selects a winner.
- Result-handler registration is idempotent. On the first Foundation call, zero earlier host descriptors selects `AuthorizationMiddlewareResultHandler`, one is captured into a non-recursive fallback factory, and multiple fail immediately with all descriptors identified. Foundation then installs one tagged wrapper; repeated Foundation calls are no-ops. A host handler must be registered before Foundation; a direct handler registered afterward is a startup conflict. Tests cover zero/one/multiple implementation-type, factory, and instance descriptors plus challenge/forbid and unrelated-policy delegation.
