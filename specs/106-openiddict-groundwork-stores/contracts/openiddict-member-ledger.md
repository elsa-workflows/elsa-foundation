# OpenIddict 7.5 Store Member Ledger

This ledger freezes the public `OpenIddict.Abstractions` 7.5.0 denominator that the
Groundwork adapter must implement. Generic overloads are listed separately because
they have a different bounded-capability contract from the named operations.

## Reproducible source

The source is the XML documentation shipped in the restored
`OpenIddict.Abstractions` 7.5.0 package:

```bash
package_root="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
xml="$package_root/openiddict.abstractions/7.5.0/lib/net10.0/OpenIddict.Abstractions.xml"
for store in Application Authorization Scope Token; do
  printf '%s: ' "$store"
  rg -c "M:OpenIddict\\.Abstractions\\.IOpenIddict${store}Store" "$xml"
done
```

Actual counts from the restored package:

| Store | Members |
|---|---:|
| Application | 42 |
| Authorization | 32 |
| Scope | 28 |
| Token | 43 |
| **Total** | **145** |

Each entry below is identified by its interface method name. Parenthetical text
distinguishes overloads with the same name.

## Application store — 42

| Capability group | Members | Count |
|---|---|---:|
| Lifecycle and concurrency | `CreateAsync`, `DeleteAsync`, `InstantiateAsync`, `UpdateAsync` | 4 |
| Count/list/generic projection | `CountAsync(CancellationToken)`, `CountAsync<TResult>(query, CancellationToken)`, `GetAsync<TState,TResult>`, `ListAsync(count, offset, CancellationToken)`, `ListAsync<TState,TResult>` | 5 |
| Named lookup | `FindByClientIdAsync`, `FindByIdAsync`, `FindByPostLogoutRedirectUriAsync`, `FindByRedirectUriAsync` | 4 |
| Scalar/collection getters | `GetApplicationTypeAsync`, `GetClientIdAsync`, `GetClientSecretAsync`, `GetClientTypeAsync`, `GetConsentTypeAsync`, `GetDisplayNameAsync`, `GetDisplayNamesAsync`, `GetIdAsync`, `GetJsonWebKeySetAsync`, `GetPermissionsAsync`, `GetPostLogoutRedirectUrisAsync`, `GetPropertiesAsync`, `GetRedirectUrisAsync`, `GetRequirementsAsync`, `GetSettingsAsync` | 15 |
| Scalar/collection setters | `SetApplicationTypeAsync`, `SetClientIdAsync`, `SetClientSecretAsync`, `SetClientTypeAsync`, `SetConsentTypeAsync`, `SetDisplayNameAsync`, `SetDisplayNamesAsync`, `SetJsonWebKeySetAsync`, `SetPermissionsAsync`, `SetPostLogoutRedirectUrisAsync`, `SetPropertiesAsync`, `SetRedirectUrisAsync`, `SetRequirementsAsync`, `SetSettingsAsync` | 14 |

## Authorization store — 32

| Capability group | Members | Count |
|---|---|---:|
| Lifecycle and concurrency | `CreateAsync`, `DeleteAsync`, `InstantiateAsync`, `UpdateAsync` | 4 |
| Count/list/generic projection | `CountAsync(CancellationToken)`, `CountAsync<TResult>(query, CancellationToken)`, `GetAsync<TState,TResult>`, `ListAsync(count, offset, CancellationToken)`, `ListAsync<TState,TResult>` | 5 |
| Named lookup | `FindAsync`, `FindByApplicationIdAsync`, `FindByIdAsync`, `FindBySubjectAsync` | 4 |
| Bounded lifecycle mutation | `PruneAsync`, `RevokeAsync`, `RevokeByApplicationIdAsync`, `RevokeBySubjectAsync` | 4 |
| Scalar/collection getters | `GetApplicationIdAsync`, `GetCreationDateAsync`, `GetIdAsync`, `GetPropertiesAsync`, `GetScopesAsync`, `GetStatusAsync`, `GetSubjectAsync`, `GetTypeAsync` | 8 |
| Scalar/collection setters | `SetApplicationIdAsync`, `SetCreationDateAsync`, `SetPropertiesAsync`, `SetScopesAsync`, `SetStatusAsync`, `SetSubjectAsync`, `SetTypeAsync` | 7 |

## Scope store — 28

| Capability group | Members | Count |
|---|---|---:|
| Lifecycle and concurrency | `CreateAsync`, `DeleteAsync`, `InstantiateAsync`, `UpdateAsync` | 4 |
| Count/list/generic projection | `CountAsync(CancellationToken)`, `CountAsync<TResult>(query, CancellationToken)`, `GetAsync<TState,TResult>`, `ListAsync(count, offset, CancellationToken)`, `ListAsync<TState,TResult>` | 5 |
| Named lookup | `FindByIdAsync`, `FindByNameAsync`, `FindByNamesAsync`, `FindByResourceAsync` | 4 |
| Scalar/collection getters | `GetDescriptionAsync`, `GetDescriptionsAsync`, `GetDisplayNameAsync`, `GetDisplayNamesAsync`, `GetIdAsync`, `GetNameAsync`, `GetPropertiesAsync`, `GetResourcesAsync` | 8 |
| Scalar/collection setters | `SetDescriptionAsync`, `SetDescriptionsAsync`, `SetDisplayNameAsync`, `SetDisplayNamesAsync`, `SetNameAsync`, `SetPropertiesAsync`, `SetResourcesAsync` | 7 |

## Token store — 43

| Capability group | Members | Count |
|---|---|---:|
| Lifecycle and concurrency | `CreateAsync`, `DeleteAsync`, `InstantiateAsync`, `UpdateAsync` | 4 |
| Count/list/generic projection | `CountAsync(CancellationToken)`, `CountAsync<TResult>(query, CancellationToken)`, `GetAsync<TState,TResult>`, `ListAsync(count, offset, CancellationToken)`, `ListAsync<TState,TResult>` | 5 |
| Named lookup | `FindAsync`, `FindByApplicationIdAsync`, `FindByAuthorizationIdAsync`, `FindByIdAsync`, `FindByReferenceIdAsync`, `FindBySubjectAsync` | 6 |
| Bounded lifecycle mutation | `PruneAsync`, `RevokeAsync`, `RevokeByApplicationIdAsync`, `RevokeByAuthorizationIdAsync`, `RevokeBySubjectAsync` | 5 |
| Scalar/collection getters | `GetApplicationIdAsync`, `GetAuthorizationIdAsync`, `GetCreationDateAsync`, `GetExpirationDateAsync`, `GetIdAsync`, `GetPayloadAsync`, `GetPropertiesAsync`, `GetRedemptionDateAsync`, `GetReferenceIdAsync`, `GetStatusAsync`, `GetSubjectAsync`, `GetTypeAsync` | 12 |
| Scalar/collection setters | `SetApplicationIdAsync`, `SetAuthorizationIdAsync`, `SetCreationDateAsync`, `SetExpirationDateAsync`, `SetPayloadAsync`, `SetPropertiesAsync`, `SetRedemptionDateAsync`, `SetReferenceIdAsync`, `SetStatusAsync`, `SetSubjectAsync`, `SetTypeAsync` | 11 |

## Coverage status

The denominator is frozen, but no Groundwork store implementation exists yet.
T042 must replace this status with one implementation identity and one direct test
identity for every member before the full-store contract can be considered complete.
