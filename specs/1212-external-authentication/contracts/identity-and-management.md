# Contracts and acceptance boundary

## Typed token identity

`ITokenService.IssueAsync` accepts an explicit typed subject and Elsa permission scopes. `RevokeSubjectAsync` accepts the same typed subject, never a bare string. Canonical-user subjects carry tenant and Elsa user ID; external subjects carry tenant, provider, validated issuer and opaque subject. Refresh preserves the complete identity. First-party validation cannot convert an external subject into a canonical user by matching its text. Missing/duplicate/conflicting identity claims fail exchange/revocation closed. External bearer normalization stamps validated origin after stripping incoming Elsa-internal claims.

Required regressions: equal subjects in different providers, issuers and tenants; case-only subject/issuer differences; framing collisions; external subject equal to an Elsa user ID; canonical claims replacing upstream identifiers; actual HTTP sign-out revokes A while B refreshes; malformed refresh identity rejected; failed revocation remains retryable. Later admission/session work reuses this boundary for disable/delete and selected-session operations.

## Connection management

Host-wide permission/capability checks precede global access. API projections contain references and safe metadata only. Expected catalog revisions protect all mutations. List/detail includes source/effectiveness/shadowing, enablement, validity, callback URL and observations. Commands: disabled create/update, explicit full override, enable/disable, archive/restore/remove, test and preview initiation. Each lifecycle command reports resulting effectiveness before confirmation. Role assignment requires existing Elsa roles inside deployment/editor delegation bounds.

## Login and preview

Backend chooser lists local and enabled effective external choices; preferred does not bypass. Normal challenge/callback and preview use distinct purposes and exact backend-derived URLs. Real protocol validation plus atomic durable state precedes pure admission evaluation. Normal completion performs authorized atomic IAM writes, creates ordinary Elsa session and redirects locally. Preview stores a bounded redacted prospective result for its initiating administrator and performs no IAM/session/token mutation. Return URLs must be local/allowed, state/nonce/PKCE/browser binding required, callbacks consumed once and pinned to current material/secret generation.

## Links and sessions

Implemented HTTP protocol routes and browser/correlation/form contracts are documented in the
[owning API module](../../../Essentials/src/Foundation/Identity/ExternalAuthentication/Api/README.md).
The ordinary token exchange exposes access token, expiry and its refresh token. Preview has separate routes,
cookie purposes and result handles, with an explicit existing administrator cookie requirement.

Permission-scoped bounded list/detail and explicit link/unlink/revoke commands. Reject cross-tenant lookup and conflicting links. Revocation invalidates the selected normal session and its token family; user disable/delete invalidates all canonical-user credentials. Safe event sink receives outcomes/correlation/record IDs, never secrets or full protocol responses. Precise wire routes/SDK types are finalized with the API implementation and contract tests; no placeholder endpoint counts as delivery.

## Implemented management wire boundary

The optional managed API owns `/_elsa/identity/external-connections`: GET root/detail, POST assess/commands/test and bounded user/role lookups. The [owning API README](../../../Essentials/src/Foundation/Identity/ExternalAuthentication/Api/README.md#connection-management-api) pins routes, requests, permission/capability/source/revision rules, closed errors, 64-KiB JSON/antiforgery requirements, safe configuration/reference projections and thirty-second metadata-only testing. Enable/restore use the saved policy's actual roles with current persisted actor grants. Unknown recurring-mapping fields are refused. Test observations remain returned metadata; private recovery proofs never appear in management JSON. Preview retains its separate administrator-cookie/browser/one-time flow boundary.

The separate user-access directory requires current `identity/users:read`, without provider permission. Bounded link/session reads require their own read permission plus user read; attachment/removal and revocation require their respective manage permission plus user read. The optional Studio module independently gates these panels and follows server action capabilities, source versions and ownership. Current-session revocation requires reauthentication. Uncertain mutations require state refresh before retry, and unattached drafts survive retained-tab suspension. Full host/deployment acceptance remains the following qualification phase.

The optional [Studio module/standalone SDK](../../../Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/README.md) consumes backend capability `elsa.api.identity.external-authentication` v1. Catalog metadata distinguishes current write permission, installed provisioning policy names and complete supported configuration from redacted unsupported schemas. The antiforgery response declares actual form/header names. Browser preview preparation explicitly requests the cookie-bound token and refuses a different authenticated transport identity. A protected, consumed preview can return closed failure categories to the currently allowed destination; invalid/unbound state cannot redirect. The frontend consumes its same-tab/backend intent once and removes result/failure query parameters before further reads.

`IAuthenticationSessionStore` in base Identity.Core owns committed canonical session records and explicit revisioned
revocation results; the optional managed EF module implements it. `ICanonicalUserSignInService` explicitly advertises
durable sessions and supplies current session/account validation inside an independent signed-identity tenant scope.
Its default capability/read methods refuse. Trusted `TokenIssueRequest.SessionId` and the signed internal session claim
bind first-party issuance/refresh/access to that authority. External raw claims cannot supply the ID. Missing authorities
reject already bound credentials; unbound canonical credentials are refused when the managed store is installed. The
operator-facing storage/lifetime/retry contract is in the [owning persistence README](../../../Essentials/src/Foundation/Identity/ExternalAuthentication/Persistence/EntityFrameworkCore/README.md).
