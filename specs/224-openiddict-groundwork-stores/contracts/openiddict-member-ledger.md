# OpenIddict 7.5 Store Member Ledger

This is the complete public-contract denominator for Spec 106. Its 145 rows are
the members of the four generic OpenIddict store interfaces, not a count of
Elsa's current callers. Every row has one intended Groundwork store, a semantic
objective, an execution classification, and an assigned direct-test/evidence
owner. A row is not implemented merely because another member in its group is.

## Reproducible source

The denominator comes from the XML documentation restored with
`OpenIddict.Abstractions` 7.5.0. The package ships its contract XML under
`lib/netstandard2.0`, not `lib/net10.0`.

```bash
root="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
xml="$root/openiddict.abstractions/7.5.0/lib/netstandard2.0/OpenIddict.Abstractions.xml"
for store in Application Authorization Scope Token; do
  printf '%s=' "$store"
  rg -c "M:OpenIddict\\.Abstractions\\.IOpenIddict${store}Store" "$xml"
done
```

Expected denominator: `Application=42`, `Authorization=32`, `Scope=28`,
`Token=43`, total **145**. Re-run this source inventory on the exact reviewed
head before T042; this preparation ledger does not assert a restored package,
capability admission, or passing store test.

The XML member identity (interface plus member signature) is authoritative. The
abbreviated signatures below preserve overload identity: `plain` is the
non-delegate overload and `generic` is the `IQueryable` delegate overload.

## Classification and ownership key

| Code | Required execution semantics | Direct-test and evidence owner |
|---|---|---|
| `L` | Local descriptor/lifecycle mapping; no collection query. CRUD also uses CAS where applicable. | Store suite `T033`/`T034`/`T035`/`T023`; shared member suite `T037`; four-provider evidence `T051`. |
| `P` | Declared, finite storage count or id-ordered page; validated bounds and no client paging. | Respective store suite; `T037`; route-plan evidence `T046`/`T052`; provider matrix `T051`. |
| `R` | Declared named point, relationship, compound, or collection-membership route executed at storage. | Respective store suite; `T037`; `T046`/`T052`; `T051`. |
| `M` | Declared finite storage mutation with CAS/UoW, exact count where the external contract returns one, cancellation, and recovery evidence. | Respective store suite plus `T025` or `T036`; `T037`; mutation-plan evidence `T046`/`T052`; `T051`. |
| `G` | Only a restricted bounded translator may admit the supplied `IQueryable` delegate. Every other delegate fails **before provider work** with the stable capability outcome; it is **never client-evaluated or materialized**. | Respective store suite; generic-rejection branch tests; `T037`; capability/plan evidence `T046`/`T052`; `T051`. |

`T023` is the direct token-store suite. `T033`, `T034`, and `T035` are the
application, authorization, and scope suites. T037 is the black-box 145-member
suite. These assignments are obligations, not claims that tests exist or pass.
T042 later replaces each assignment with the exact passing test identity and
evidence artifact.

## Application — 42

**Intended implementation:** `GroundworkOpenIddictApplicationStore` implementing
`IOpenIddictApplicationStore<TApplication>` over the T014 Groundwork application
record type.

| Member | Semantic objective | Class | Owner |
|---|---|:---:|---|
| `CountAsync(plain)` | Count applications. | P | T033/T037/T046/T051 |
| `CountAsync<TResult>(generic)` | Restricted projected count. | G | T033/T037/T046/T051 |
| `CreateAsync` | Persist a new application; reject duplicate client id. | M | T033/T036/T037/T046/T051 |
| `DeleteAsync` | CAS-delete application under dependent-policy UoW. | M | T033/T036/T037/T046/T051 |
| `FindByIdAsync` | Point lookup by application identity. | R | T033/T037/T046/T051 |
| `FindByClientIdAsync` | Unique client-id lookup. | R | T033/T037/T046/T051 |
| `FindByPostLogoutRedirectUriAsync` | Post-logout URI membership lookup. | R | T033/T037/T046/T051 |
| `FindByRedirectUriAsync` | Redirect URI membership lookup. | R | T033/T037/T046/T051 |
| `GetApplicationTypeAsync` | Read application type descriptor. | L | T033/T037/T051 |
| `GetAsync<TState,TResult>(generic)` | Restricted projected query. | G | T033/T037/T046/T051 |
| `GetClientIdAsync` | Read client identifier. | L | T033/T037/T051 |
| `GetClientSecretAsync` | Read client secret descriptor. | L | T033/T037/T051 |
| `GetClientTypeAsync` | Read client type descriptor. | L | T033/T037/T051 |
| `GetConsentTypeAsync` | Read consent type descriptor. | L | T033/T037/T051 |
| `GetDisplayNameAsync` | Read invariant display name. | L | T033/T037/T051 |
| `GetDisplayNamesAsync` | Read localized display-name map. | L | T033/T037/T051 |
| `GetIdAsync` | Read opaque application identity. | L | T033/T037/T051 |
| `GetJsonWebKeySetAsync` | Read JSON web-key set. | L | T033/T037/T051 |
| `GetPermissionsAsync` | Read permission collection. | L | T033/T037/T051 |
| `GetPostLogoutRedirectUrisAsync` | Read post-logout URI collection. | L | T033/T037/T051 |
| `GetPropertiesAsync` | Read JSON property map. | L | T033/T037/T051 |
| `GetRedirectUrisAsync` | Read redirect URI collection. | L | T033/T037/T051 |
| `GetRequirementsAsync` | Read requirement collection. | L | T033/T037/T051 |
| `GetSettingsAsync` | Read settings map. | L | T033/T037/T051 |
| `InstantiateAsync` | Create an unpersisted descriptor instance. | L | T033/T037/T051 |
| `ListAsync(plain)` | Return validated finite id-ordered page. | P | T033/T037/T046/T051 |
| `ListAsync<TState,TResult>(generic)` | Restricted projected list. | G | T033/T037/T046/T051 |
| `SetApplicationTypeAsync` | Set application type descriptor. | L | T033/T037/T051 |
| `SetClientIdAsync` | Set client id for create/update uniqueness enforcement. | L | T033/T037/T051 |
| `SetClientSecretAsync` | Set client secret descriptor. | L | T033/T037/T051 |
| `SetClientTypeAsync` | Set client type descriptor. | L | T033/T037/T051 |
| `SetConsentTypeAsync` | Set consent type descriptor. | L | T033/T037/T051 |
| `SetDisplayNameAsync` | Set invariant display name. | L | T033/T037/T051 |
| `SetDisplayNamesAsync` | Set localized display-name map. | L | T033/T037/T051 |
| `SetJsonWebKeySetAsync` | Set JSON web-key set. | L | T033/T037/T051 |
| `SetPermissionsAsync` | Set permissions collection. | L | T033/T037/T051 |
| `SetPostLogoutRedirectUrisAsync` | Set post-logout URI collection. | L | T033/T037/T051 |
| `SetPropertiesAsync` | Set JSON property map. | L | T033/T037/T051 |
| `SetRedirectUrisAsync` | Set redirect URI collection. | L | T033/T037/T051 |
| `SetRequirementsAsync` | Set requirement collection. | L | T033/T037/T051 |
| `SetSettingsAsync` | Set settings map. | L | T033/T037/T051 |
| `UpdateAsync` | CAS-update and rotate concurrency value. | M | T033/T036/T037/T046/T051 |

## Authorization — 32

**Intended implementation:** `GroundworkOpenIddictAuthorizationStore` implementing
`IOpenIddictAuthorizationStore<TAuthorization>` over the T014 Groundwork
authorization record type.

| Member | Semantic objective | Class | Owner |
|---|---|:---:|---|
| `CountAsync(plain)` | Count authorizations. | P | T034/T037/T046/T051 |
| `CountAsync<TResult>(generic)` | Restricted projected count. | G | T034/T037/T046/T051 |
| `CreateAsync` | Persist a new authorization. | M | T034/T036/T037/T046/T051 |
| `DeleteAsync` | CAS-delete authorization under relationship policy. | M | T034/T036/T037/T046/T051 |
| `FindAsync` | Compound subject/client/status/type/scopes route. | R | T034/T037/T046/T051 |
| `FindByApplicationIdAsync` | Relationship lookup by application id. | R | T034/T037/T046/T051 |
| `FindByIdAsync` | Point lookup by authorization identity. | R | T034/T037/T046/T051 |
| `FindBySubjectAsync` | Subject relationship lookup. | R | T034/T037/T046/T051 |
| `GetApplicationIdAsync` | Read application relationship. | L | T034/T037/T051 |
| `GetAsync<TState,TResult>(generic)` | Restricted projected query. | G | T034/T037/T046/T051 |
| `GetCreationDateAsync` | Read creation timestamp. | L | T034/T037/T051 |
| `GetIdAsync` | Read opaque authorization identity. | L | T034/T037/T051 |
| `GetPropertiesAsync` | Read JSON property map. | L | T034/T037/T051 |
| `GetScopesAsync` | Read scope collection. | L | T034/T037/T051 |
| `GetStatusAsync` | Read authorization status. | L | T034/T037/T051 |
| `GetSubjectAsync` | Read subject. | L | T034/T037/T051 |
| `GetTypeAsync` | Read authorization type. | L | T034/T037/T051 |
| `InstantiateAsync` | Create unpersisted authorization descriptor. | L | T034/T037/T051 |
| `ListAsync(plain)` | Return validated finite id-ordered page. | P | T034/T037/T046/T051 |
| `ListAsync<TState,TResult>(generic)` | Restricted projected list. | G | T034/T037/T046/T051 |
| `PruneAsync` | Bounded date lifecycle prune with exact count. | M | T034/T036/T037/T046/T051 |
| `RevokeAsync` | Bounded compound status mutation with exact count. | M | T034/T036/T037/T046/T051 |
| `RevokeByApplicationIdAsync` | Bounded revoke by application relationship. | M | T034/T036/T037/T046/T051 |
| `RevokeBySubjectAsync` | Bounded revoke by subject. | M | T034/T036/T037/T046/T051 |
| `SetApplicationIdAsync` | Set application relationship. | L | T034/T037/T051 |
| `SetCreationDateAsync` | Set creation timestamp. | L | T034/T037/T051 |
| `SetPropertiesAsync` | Set JSON property map. | L | T034/T037/T051 |
| `SetScopesAsync` | Set scope collection. | L | T034/T037/T051 |
| `SetStatusAsync` | Set authorization status. | L | T034/T037/T051 |
| `SetSubjectAsync` | Set subject. | L | T034/T037/T051 |
| `SetTypeAsync` | Set authorization type. | L | T034/T037/T051 |
| `UpdateAsync` | CAS-update and rotate concurrency value. | M | T034/T036/T037/T046/T051 |

## Scope — 28

**Intended implementation:** `GroundworkOpenIddictScopeStore` implementing
`IOpenIddictScopeStore<TScope>` over the T014 Groundwork scope record type.

| Member | Semantic objective | Class | Owner |
|---|---|:---:|---|
| `CountAsync(plain)` | Count scopes. | P | T035/T037/T046/T051 |
| `CountAsync<TResult>(generic)` | Restricted projected count. | G | T035/T037/T046/T051 |
| `CreateAsync` | Persist a scope; reject duplicate name. | M | T035/T036/T037/T046/T051 |
| `DeleteAsync` | CAS-delete scope. | M | T035/T036/T037/T046/T051 |
| `FindByIdAsync` | Point lookup by scope identity. | R | T035/T037/T046/T051 |
| `FindByNameAsync` | Unique scope-name lookup. | R | T035/T037/T046/T051 |
| `FindByNamesAsync` | Finite scope-name-set lookup. | R | T035/T037/T046/T051 |
| `FindByResourceAsync` | Resource membership lookup. | R | T035/T037/T046/T051 |
| `GetAsync<TState,TResult>(generic)` | Restricted projected query. | G | T035/T037/T046/T051 |
| `GetDescriptionAsync` | Read invariant description. | L | T035/T037/T051 |
| `GetDescriptionsAsync` | Read localized description map. | L | T035/T037/T051 |
| `GetDisplayNameAsync` | Read invariant display name. | L | T035/T037/T051 |
| `GetDisplayNamesAsync` | Read localized display-name map. | L | T035/T037/T051 |
| `GetIdAsync` | Read opaque scope identity. | L | T035/T037/T051 |
| `GetNameAsync` | Read scope name. | L | T035/T037/T051 |
| `GetPropertiesAsync` | Read JSON property map. | L | T035/T037/T051 |
| `GetResourcesAsync` | Read resource collection. | L | T035/T037/T051 |
| `InstantiateAsync` | Create unpersisted scope descriptor. | L | T035/T037/T051 |
| `ListAsync(plain)` | Return validated finite id-ordered page. | P | T035/T037/T046/T051 |
| `ListAsync<TState,TResult>(generic)` | Restricted projected list. | G | T035/T037/T046/T051 |
| `SetDescriptionAsync` | Set invariant description. | L | T035/T037/T051 |
| `SetDescriptionsAsync` | Set localized description map. | L | T035/T037/T051 |
| `SetDisplayNameAsync` | Set invariant display name. | L | T035/T037/T051 |
| `SetDisplayNamesAsync` | Set localized display-name map. | L | T035/T037/T051 |
| `SetNameAsync` | Set scope name for create/update uniqueness enforcement. | L | T035/T037/T051 |
| `SetPropertiesAsync` | Set JSON property map. | L | T035/T037/T051 |
| `SetResourcesAsync` | Set resource collection. | L | T035/T037/T051 |
| `UpdateAsync` | CAS-update and rotate concurrency value. | M | T035/T036/T037/T046/T051 |

## Token — 43

**Intended implementation:** `GroundworkOpenIddictTokenStore` implementing
`IOpenIddictTokenStore<TToken>` over the T014 Groundwork token record type.

| Member | Semantic objective | Class | Owner |
|---|---|:---:|---|
| `CountAsync(plain)` | Count token entries. | P | T023/T037/T046/T051 |
| `CountAsync<TResult>(generic)` | Restricted projected count. | G | T023/T037/T046/T051 |
| `CreateAsync` | Persist token; reject duplicate obfuscated reference id. | M | T023/T025/T037/T046/T051 |
| `DeleteAsync` | CAS-delete token. | M | T023/T025/T037/T046/T051 |
| `FindAsync` | Compound subject/client/status/type route. | R | T023/T037/T046/T051 |
| `FindByApplicationIdAsync` | Relationship lookup by application id. | R | T023/T037/T046/T051 |
| `FindByAuthorizationIdAsync` | Relationship lookup by authorization id. | R | T023/T037/T046/T051 |
| `FindByIdAsync` | Point lookup by token identity. | R | T023/T037/T046/T051 |
| `FindByReferenceIdAsync` | Unique obfuscated-reference lookup. | R | T023/T037/T046/T051 |
| `FindBySubjectAsync` | Subject relationship lookup. | R | T023/T037/T046/T051 |
| `GetApplicationIdAsync` | Read application relationship. | L | T023/T037/T051 |
| `GetAsync<TState,TResult>(generic)` | Restricted projected query. | G | T023/T037/T046/T051 |
| `GetAuthorizationIdAsync` | Read authorization relationship. | L | T023/T037/T051 |
| `GetCreationDateAsync` | Read creation timestamp. | L | T023/T037/T051 |
| `GetExpirationDateAsync` | Read expiration timestamp. | L | T023/T037/T051 |
| `GetIdAsync` | Read opaque token identity. | L | T023/T037/T051 |
| `GetPayloadAsync` | Read token payload. | L | T023/T037/T051 |
| `GetPropertiesAsync` | Read JSON property map. | L | T023/T037/T051 |
| `GetRedemptionDateAsync` | Read redemption timestamp. | L | T023/T037/T051 |
| `GetReferenceIdAsync` | Read obfuscated reference id. | L | T023/T037/T051 |
| `GetStatusAsync` | Read token status. | L | T023/T037/T051 |
| `GetSubjectAsync` | Read token subject. | L | T023/T037/T051 |
| `GetTypeAsync` | Read token type. | L | T023/T037/T051 |
| `InstantiateAsync` | Create unpersisted token descriptor. | L | T023/T037/T051 |
| `ListAsync(plain)` | Return validated finite id-ordered page. | P | T023/T037/T046/T051 |
| `ListAsync<TState,TResult>(generic)` | Restricted projected list. | G | T023/T037/T046/T051 |
| `PruneAsync` | Bounded lifecycle prune with exact count. | M | T023/T025/T037/T046/T051 |
| `RevokeAsync` | Bounded compound revoke with exact count. | M | T023/T025/T037/T046/T051 |
| `RevokeByApplicationIdAsync` | Bounded revoke by application relationship. | M | T023/T025/T037/T046/T051 |
| `RevokeByAuthorizationIdAsync` | Bounded revoke by authorization relationship. | M | T023/T025/T037/T046/T051 |
| `RevokeBySubjectAsync` | Bounded revoke by subject. | M | T023/T025/T037/T046/T051 |
| `SetApplicationIdAsync` | Set application relationship. | L | T023/T037/T051 |
| `SetAuthorizationIdAsync` | Set authorization relationship. | L | T023/T037/T051 |
| `SetCreationDateAsync` | Set creation timestamp. | L | T023/T037/T051 |
| `SetExpirationDateAsync` | Set expiration timestamp. | L | T023/T037/T051 |
| `SetPayloadAsync` | Set token payload. | L | T023/T037/T051 |
| `SetPropertiesAsync` | Set JSON property map. | L | T023/T037/T051 |
| `SetRedemptionDateAsync` | Set redemption timestamp. | L | T023/T037/T051 |
| `SetReferenceIdAsync` | Set obfuscated reference id for uniqueness enforcement. | L | T023/T037/T051 |
| `SetStatusAsync` | Set token status. | L | T023/T037/T051 |
| `SetSubjectAsync` | Set token subject. | L | T023/T037/T051 |
| `SetTypeAsync` | Set token type. | L | T023/T037/T051 |
| `UpdateAsync` | CAS-update and rotate concurrency value. | M | T023/T025/T037/T046/T051 |

## T001 completion boundary

T001 freezes **what** must be implemented and how it must be demonstrated. It
does not claim any Groundwork store or test is complete. The 12 generic member
rows are deliberately fail-closed until a restricted translator proves an
admitted route; implementing them by running an arbitrary delegate over a
materialized collection is prohibited.
