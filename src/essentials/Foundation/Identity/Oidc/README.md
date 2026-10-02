# External OIDC authentication

`FoundationIdentityOidc` registers the existing external provider module, JWT bearer handler and, when ClientId is supplied, the interactive OpenID Connect handler. Feature settings and `AddFoundationIdentityOidc(configure)` use the same registration path. Host-selected authentication defaults remain authoritative.

## Fixed-host bearer normalization

`NormalizeBearerClaims` defaults to false. Enable it with an Authority, effective API Audience, ProviderId and nonblank TenantId. Audience is independent of ClientId: only an absent Audience falls back to ClientId; explicit blank refuses opt-in activation. ClientId alone selects interactive registration.

The Authority must use HTTPS except for loopback HTTP when `RequireHttpsMetadata=false`, for local development and tests. The final JWT bearer `MetadataAddress` is checked by the same rule, including when it overrides Authority; an absent address retains the handler's Authority-based discovery default, and custom HTTPS metadata endpoints remain supported. Invalid or non-loopback HTTP addresses refuse opt-in activation. This restriction does not change the legacy `NormalizeBearerClaims=false` path. Keep `RequireHttpsMetadata=true` for deployments: disabling it also disables the stock retriever's HTTPS check for discovery-advertised key URLs. The loopback exception assumes trusted local issuer/key configuration; the options guard checks configured Authority/MetadataAddress and does not replace the host's configuration manager or backchannel.

Before composing the shell, the host must initialize an ordinary persistence context for that TenantId through existing `AddPersistenceCore(tenantId)` or its own equivalent accessor. Supply one explicit `IClaimMappingStore` and one `IClaimsNormalizer`; durable IAM provider/connection/schema setup belongs to the host. A named Runtime database default does not automatically enroll IAM.

The adapter keeps real issuer, signature, audience and lifetime validation, then filters incoming Elsa internal claims, loads current owned mapping rules and invokes the existing normalizer. It admits one exact `Elsa.Foundation.Identity.Oidc.Bearer.Normalized` identity. The bridge does not provision users or external-identity links. The shared permission pipeline authorizes the resulting permissions.

The owned scoped events protect MessageReceived, TokenValidated and AuthenticationFailed from successful short circuits. Ordinary token extraction and external-claim mutation callbacks can be composed; custom Challenge/Forbidden callbacks, foreign events or authentication scheme handlers and duplicate opt-in registration refuse. Hosted startup and shell initialization verify the actual handler, final options, trusted type and static scope. Changes to the registered bridge mode, namespace/scheme, raw identity type, effective discovery URL or captured callback intent require fresh activation. Store/normalizer failures use fixed authentication refusals; observed request cancellation propagates. Existing upstream TokenRefreshBoundary metadata remains unchanged.

For the canonical configuration/event/failure contract and proof boundaries, see [Spec190 bearer contract](../../../../../specs/190-worker-oidc-normalization/contracts/bearer-normalization.md) and [implementation evidence](../../../../../specs/190-worker-oidc-normalization/implementation-evidence.md). The implementation is in progress under #2308; these documents do not certify a deployed IdP, interactive normalization, dynamic tenancy, distributed topology or Worker profile publication.

## Registered services

- Existing authentication provider module and OIDC/JWT/default-scheme options configurators.
- Registration snapshot, final OIDC/JWT options validator and dual hosted/shell activation guard.
- Opt-in scoped `OidcBearerNormalizationEvents` and its distinct normalized-type enrollment.

## Cross-domain contributions

This module contributes `IAuthenticationProviderModule` and consumes `IClaimMappingStore`/`IClaimsNormalizer` from [Identity Core's extension-point catalog](../Core/EXTENSION_POINTS.md). The opt-in bridge consumes existing Runtime Core persistence scope contracts; it adds no Runtime feature activation or EF dependency. Host-supplied token validators, handlers and ordinary callbacks remain trusted host code, as defined by the bearer contract; the bridge does not sandbox them. The acceptance host proves the stock JWT validation path against real issuer discovery/JWKs.
