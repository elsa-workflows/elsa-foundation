# Research: auth/bridge extraction

## Existing implementation and contracts

Decision: preserve Web's bearer scheme, permission handler, bounded hashed-key session cache and backend
bridge/read mappings; move ownership into Hosting rather than adding another auth abstraction. Public
StudioBridgeAuthorization already owns the scheme, permission claim and policy builder; add the five
existing policy/permission constants there. Api already owns backend options/status/read-client contracts.

Rationale: original tests exercise actual HTTP statuses, headers and backend requests. Copying those
contracts into Hosting would create another public surface and undermine dynamic module compatibility.
Alternative rejected: depend on backend identity implementation or expose private handlers/cache/client DTOs.

## Root sharing and test boundaries

Decision: use the qualified released CShells0.0.31 ShareSingletonWithShells API for cache/clock/options and
the root session transport. Preserve normal authorization-handler enumeration rather than sharing an
entire potentially heterogeneous handler collection as one singleton alias.

Use public HttpClientFactory builder filters for recording backends; assertions observe paths/headers/JSON.
Tests can call public IAuthenticationService through a retained shell scope to prove the root cache alias
is used, rather than merely exercising pre-routing root middleware twice. A root-owned disposable clock
checks that shell drain does not dispose it, and host disposal remains its owner.

## Configuration and security

Decision: use the same installed AuthEnabled/trusted-prefix snapshot as runtime JS. Existing configuration
is supplied before AddElsaStudio; restart applies changed security choices. Keep missing privileged key/URL
separate from ordinary session introspection. Preserve existing positive/negative TTL behavior and uncached
indeterminate backend failures; no distributed revocation or single-flight guarantee is added.

Trusted prefixes already reject blank/root/wildcard input; consuming them as auth policy must also reject
ambiguous all-slash/root variants. Qualification should exercise public registration and real path matching.
No unresolved architecture choice remains; actual public CShells contracts and the accepted hosting contract
supply the boundary.

Actual migration evidence: globally borrowing IHttpClientFactory hid named-client actions registered only in
shell features, causing two existing Extension Builder HTTP cases to fail with an unset BaseAddress. Share
only a private root session transport that delegates to the root factory; preserve shell-owned general HTTP
factories. This is a concrete configuration/lifetime boundary, not another public transport abstraction.

CShells deliberately excludes root IAuthenticationSchemeProvider and IAuthorizationPolicyProvider, and
sharing either excluded type is rejected. Sharing a closed private IOptionsMonitor also conflicts with the
normal open-generic registration. The mandatory StudioApi feature restores standard AddAuthenticationCore
and AddAuthorization shell services; copied root scheme/policy callbacks reconstruct the installed choices,
while handlers stay scoped. Public IAuthenticationService/IAuthorizationService tests exercise that boundary.
No production friend, global scheme-provider override or closed-monitor replacement is needed.

## Embedding authentication defaults

Independent review found that adding Studio's named scheme turns a host's single application scheme into
a multi-scheme configuration, removing ASP.NET Core's implicit default. Preserve the sole non-Studio
scheme as the fallback default after normal configuration callbacks; retain explicit general/action defaults,
multiple-application-scheme ambiguity and the standard auto-default suppression switch. Registration order
must not matter. Studio policies continue to select their own scheme explicitly, so an application cookie
never grants Studio access. The implementation follows the framework's documented default behavior and
[AuthenticationOptions source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Authentication.Abstractions/src/AuthenticationOptions.cs).
