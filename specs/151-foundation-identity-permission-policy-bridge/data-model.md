# Phase 1 Data Model — Foundation Identity permission policy bridge

No persistent schema changes are required. These are immutable authorization values built from endpoint declarations, DI contributions, and authenticated principals.

## Permission key identity

| Field | Rule |
|---|---|
| Declared value | Non-empty; leading/trailing whitespace rejected; retained for presentation compatibility |
| Canonical value | Unicode NFC, then invariant uppercase |
| Equality/order | Ordinal over canonical value |
| Wildcard | Exact canonical `*`; grant-only except an explicit request for `*` |

Canonicalization occurs at every comparison/index boundary but does not rewrite catalog/session/token presentation values.

## Permission policy descriptor

```text
PermissionPolicyDescriptor
├── Mode: Single | Any | All
└── Permissions: non-empty, canonical, de-duplicated, ordinal-sorted list
```

Invariants:

- `Single` has exactly one member.
- `Any` and `All` have at least one member.
- Duplicate or canonically equivalent members collapse.
- Wildcard may be requested explicitly and may be one member of the transitional FastEndpoints any-set.
- The descriptor has one deterministic v1 policy identity.

## Authorization requirements

- Existing `PermissionAuthorizationRequirement(string Permission)` represents the compatible single-member contract.
- A new composite requirement represents `Any` or `All` and carries the canonical ordered member set.
- Both are resolved only by Foundation Identity's registered policy provider and delegate member evaluation to one shared internal service.

## Permission member outcome

```text
MemberOutcome = Granted | Denied
```

Resolution for one member:

1. Any resource denial -> `Denied`.
2. Else any resource grant -> `Granted`.
3. Else every resource source abstained -> general evaluator result.
4. Exception/timeout/cancellation -> propagates; no `MemberOutcome` is produced.

Resource handlers run in registration order only while normal decisions are returned. The first
operational failure short-circuits the requirement, so a later handler or evaluator cannot convert
that failure into a grant. For an HTTP endpoint, the handler finds the active `HttpContext` first
from the authorization resource and then from `IHttpContextAccessor`; the additive
`PermissionEvaluationContext.CancellationToken` property and the existing method token arguments
both carry that context's `RequestAborted`. The original authorization resource is preserved in
`PermissionEvaluationContext.Resource`. Direct non-HTTP authorization uses `CancellationToken.None`.

Composition:

- Single: member outcome.
- Any: granted if at least one member is granted.
- All: granted only if every member is granted.

## Permission catalog entry

The existing positional permission definition remains source/binary compatible and gains non-positional immutable provenance:

| Field | Meaning |
|---|---|
| Key / display / category / description / implications | Existing definition contract |
| OwnerId | Stable module/feature identity supplied by the contributor or compatible default |
| ContributorType | Fully qualified implementation type that supplied the definition |

Catalog invariants:

- Index by canonical key, retain declared presentation.
- Exactly one entry per canonical key.
- Duplicate diagnostics name both owner/contributor sources.
- `*` cannot be a definition or implication target.
- Studio Preferences and Module Management entries expose explicit stable owners.
- A catalog snapshot contains only contributors in its service provider.

## Normalized principal

The authentication/provider layer supplies a principal containing Elsa internal tenant/provider/permission claims. Raw incoming Elsa internal claims are stripped before mapped claims are added. Mapping rules are filtered by current tenant/provider and applied in deterministic order with stop behavior.

Authorization canonicalizes permission claim values for comparison but does not authorize from raw provider-specific claims. A normalized identity has exactly one internal `elsa.identity.normalized = v1` marker emitted only after normalization/projection completes. The normalizer first strips incoming Elsa-internal claims, including an untrusted incoming marker, then adds the tenant, provider, mapped grants, and marker. First-party cookie and token factories add the marker only after their trusted claims projection completes.

The marker is necessary but not sufficient. `FoundationIdentityOptions.NormalizedAuthenticationTypes`
is empty by default and compared ordinally. Each first-party provider package explicitly contributes the
exact runtime `ClaimsIdentity.AuthenticationType` observed after authentication by calling
`AddNormalizedAuthenticationType` under the strip-map-mark obligation. ASP.NET Core Identity registers
`"Elsa.Foundation.Identity"` for its external principal factory and its cookie scheme for cookie requests;
OpenIddict registers its validation scheme and does not trust the token-creation type `"openiddict"`.

The normalized-principal validator requires exactly one authenticated identity whose type is registered
and which has exactly one valid marker. Zero or multiple matching identities fail closed, so grants from
different tenants/providers are never unioned. It builds the principal supplied to authorization from
only that selected identity. Forged marker/internal permission claims on an unregistered raw-provider
identity are therefore excluded.

Every Foundation permission policy contains an internal normalized-principal requirement. The shared
permission handler refuses a principal with no trusted normalized identity without evaluating grants.
For HTTP authorization, a policy-aware authorization-middleware result handler changes only an
authenticated, untrusted/unmarked failure of that requirement into a challenge; it delegates unrelated
policies and all other outcomes to the captured host/default handler. This produces 401 with zero
permission-resource/evaluator calls without depending on endpoint lookup or authentication/routing order.
A provider adapter whose normalization throws must fail authentication and must not issue a marked
principal. A trusted normalized principal with no satisfying permission remains authenticated and
therefore receives 403.

## Policy parsing result

```text
PolicyParseResult
├── NotPermission
├── Valid(PermissionPolicyDescriptor, IsLegacyAlias)
└── MalformedReservedPolicy(reason)
```

Only `NotPermission` delegates to the host provider. Malformed canonical v1 input fails closed. Legacy aliases are single-only and parse-only during the replacement window.
