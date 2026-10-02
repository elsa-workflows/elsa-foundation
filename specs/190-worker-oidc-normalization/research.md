# Research decisions: Worker external bearer normalization

Source baseline: fetched main `0bb61d9990c23d426e92d2ea0ac0ea13fd71b828`. This is source/design evidence, not executed adapter acceptance.

## R1 — Keep the existing OIDC owner and opt in through settings

**Decision**: Add `NormalizeBearerClaims=false` and `Audience` to existing OIDC options/feature, expose existing `ProviderId` and `TenantId` in feature configuration, and retain the same package/handler. `Audience` absent uses legacy `ClientId`; supplied blank refuses in opt-in mode. Only `ClientId` controls interactive-handler registration.

**Rationale**: [OIDC registration](../../src/essentials/Foundation/Identity/Oidc/Extensions/OidcAuthenticationServiceCollectionExtensions.cs) already captures the configure delegate to register named handlers; the feature passes bound settings through that delegate. [Current bearer options](../../src/essentials/Foundation/Identity/Oidc/ConfigureOidcOptions.cs) couple Audience to ClientId. A default-false setting offers declarative and C# parity without a new feature hierarchy or project.

**Alternatives**: C#-only adapter would exclude the builder; a new identity system duplicates existing seams; adding an audience while using it as the interactive-registration trigger retains the current Worker fault.

## R2 — Fixed host namespace, durable owned mapping rules

**Decision**: Capture configured ProviderId/TenantId, load `IClaimMappingStore.ListForProviderAsync` per validated request, filter incoming internal claims, then call `IClaimsNormalizer` with a distinct adapter-owned normalized authentication type. Namespace changes never come from claims. Preserve current normalizer behavior for other callers.

**Rationale**: [Normalizer/evaluator](../../src/essentials/Foundation/Identity/Authorization/AuthorizationServices.cs) supports provider/tenant rule matching and normalized claim authorization without user lookup. Its shared normalizer evaluates rules before filtering internal claims, so the OIDC boundary must filter its input first. [NormalizedPrincipalValidator](../../src/essentials/Foundation/Identity/Authorization/NormalizedPrincipalValidator.cs) requires exactly one authenticated trusted-type identity with exactly one marker; merely adding the scheme to trusted types is insufficient.

**Alternatives**: Reuse the ASP.NET Identity principal factory would provision/link users; derive namespace from JWT claims would let callers choose authority; changing the shared normalizer broadens compatibility scope. Dynamic tenancy remains deferred until trusted context sourcing has its own contract.

## R3 — Guard the complete bearer event boundary

**Decision**: Install an owned scoped `JwtBearerEvents` type in opt-in mode, capture ordinary existing callback delegates before installation, and validate final named-options ownership. Reject foreign event types/subclasses rather than guessing how to compose them. Preserve earlier Fail/NoResult; reject a successful short circuit from MessageReceived, AuthenticationFailed or a prior TokenValidated callback. Call ordinary token-validated callbacks first, then filter/map/validate as the final principal-changing operation. Preserve existing token extraction but require real validation before mapping.

**Rationale**: The official [.NET10 JwtBearerHandler](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs) returns non-null event results from MessageReceived before validation, TokenValidated after validation and AuthenticationFailed after failure. A lone OnTokenValidated wrapper cannot protect every success route. [JwtBearerEvents](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs) provides virtual boundaries for an owned adapter.

**Alternatives**: Silent callback replacement loses existing refusal; accepting all EventsType replacements allows bypass; general decorator discovery invents support for unreviewed custom handlers. Ordinary extraction/validation/failure callbacks may be composed under the explicit guarded contract. Challenge/Forbidden response callbacks are unsupported in this first opt-in layout and activation rejects custom delegates; the adapter owns safe401/403. Host-provided collaborators remain trusted host code; this is not a sandbox against arbitrary host mutation.

## R4 — Validate final registration instead of trusting declaration order

**Decision**: The existing configure-delegate path explicitly selects opt-in registration and the scheme snapshot. Validate final OIDC/JWT named options and actual handler/event type before serving. Late options that enable normalization without its adapter, change its enrolled scheme or replace its guarded event type refuse. Namespace/audience configuration must be frozen for an activated host; reconfiguration requires fresh activation. Do not silently enroll another scheme on option reload.

**Rationale**: Existing OIDC registration makes decisions before later options resolution. Enrollment detached from the guarded handler could trust an unnormalized principal. The output uses `Elsa.Foundation.Identity.Oidc.Bearer.Normalized`, distinct from the raw token identity type; the JWT scheme still binds the adapter to the actual named handler. Raw identity type is governed by token-validation parameters/handler behavior, not assumed equal to the scheme name. Repeated opt-in registration is explicitly refused, not silently deduplicated or replaced. Final options validation plus host activation verification makes that mismatch explicit. Existing FoundationIdentityAbstractionsFeature owns shell authentication middleware; its selected shell must resolve the actual adapter/store. DevelopmentOrDemoGuard demonstrates the existing dual hosted-startup/IShellInitializer gate pattern, which is preferable to assuming root options validation runs for shells.

The rule store already has explicit replacement guards. IClaimsNormalizer is unmarked and registered with TryAddScoped today; opt-in activation must separately require exactly one normalizer descriptor, while consuming rather than replacing it.

**Alternatives**: Assume registration order is safe; broaden trust to all JWT schemes; introduce a new multi-provider options system. All add uncertainty or weaken the reviewed trust boundary.

## R5 — Preserve failure and cancellation ownership

**Decision**: Bad trust configuration refuses activation. Store/normalizer or malformed-result failures use a fixed authentication failure, no raw exception, and no successful ticket. RequestAborted cancellation is propagated and rechecked at await/publication boundaries; the owned AuthenticationFailed event must not suppress observed cancellation or turn it into success. Evaluator/resource exceptions retain Spec151 FR-024 propagation, while ordinary denial returns403. Disable detailed challenge error descriptions for the opt-in lane.

**Rationale**: Existing [PermissionAuthorizationService](../../src/essentials/Foundation/Identity/Authorization/AuthorizationServices.cs) links request cancellation and propagates operational errors. The stock bearer handler invokes AuthenticationFailed after a thrown exception; real-handler cancellation tests must prove the adapter does not swallow it. The bridge can control its own fixed failures/evidence, not make a blanket assertion about arbitrary host logging.

## R6 — Preserve capability metadata and store ownership

**Decision**: Keep ExternalOidcDefault TokenRefreshBoundary. Load current local rules per request, prove same-token rule removal, and report those observations separately. First host proof explicitly configures the complete IAM EF authority backend, provisions its schema, and uses named Runtime SQLite persistence; no automatic IAM enrollment or bearer user writes.

**Rationale**: [OIDC provider module](../../src/essentials/Foundation/Identity/Oidc/OidcAuthenticationProviderModule.cs) and [effective capabilities resolver](../../src/essentials/Foundation/Identity/Ownership/DefaultEffectiveCapabilitiesResolver.cs) govern the upstream-token boundary. [IAM feature](../../src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/IdentityIamEntityFrameworkCoreFeature.cs) does not carry shared-resource participant enrollment; [EfClaimMappingStore](../../src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/Stores/EfClaimMappingStore.cs) is the existing durable rule backend. Claims evaluation does not require provisioning.

## Deferred outcomes and triggers

- Dynamic tenancy: revisit before more than one static tenant is required.
- Multi-provider/scheme arbitration: revisit before a host chooses more than one normalized external scheme.
- Interactive normalization and IdP interoperability: separate real interactive/deployed-provider journeys.
- Worker profile publication: only after adapter actor proof and exact profile membership/prerequisite review.
- Builder human study, unknown-setting export and Authoring API scope: existing program gates; this leaf cannot answer them.
