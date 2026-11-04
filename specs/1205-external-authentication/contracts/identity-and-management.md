# Contracts and acceptance boundary

## Typed token identity

`ITokenService.IssueAsync` accepts an explicit typed subject and Elsa permission scopes. `RevokeSubjectAsync` accepts the same typed subject, never a bare string. Canonical-user subjects carry tenant and Elsa user ID; external subjects carry tenant, provider, validated issuer and opaque subject. Refresh preserves the complete identity. First-party validation cannot convert an external subject into a canonical user by matching its text. Missing/duplicate/conflicting identity claims fail exchange/revocation closed. External bearer normalization stamps validated origin after stripping incoming Elsa-internal claims.

Required regressions: equal subjects in different providers, issuers and tenants; case-only subject/issuer differences; framing collisions; external subject equal to an Elsa user ID; canonical claims replacing upstream identifiers; actual HTTP sign-out revokes A while B refreshes; malformed refresh identity rejected; failed revocation remains retryable. Later admission/session work reuses this boundary for disable/delete and selected-session operations.

## Connection management

Host-wide permission/capability checks precede global access. API projections contain references and safe metadata only. Expected catalog revisions protect all mutations. List/detail includes source/effectiveness/shadowing, enablement, validity, callback URL and observations. Commands: disabled create/update, explicit full override, enable/disable, archive/restore/remove, test and preview initiation. Each lifecycle command reports resulting effectiveness before confirmation. Role assignment requires existing Elsa roles inside deployment/editor delegation bounds.

## Login and preview

Backend chooser lists local and enabled effective external choices; preferred does not bypass. Normal challenge/callback and preview use distinct purposes and exact backend-derived URLs. Real protocol validation plus atomic durable state precedes pure admission evaluation. Normal completion performs authorized atomic IAM writes, creates ordinary Elsa session and redirects locally. Preview stores a bounded redacted prospective result for its initiating administrator and performs no IAM/session/token mutation. Return URLs must be local/allowed, state/nonce/PKCE/browser binding required, callbacks consumed once and pinned to current material/secret generation.

## Links and sessions

Permission-scoped bounded list/detail and explicit link/unlink/revoke commands. Reject cross-tenant lookup and conflicting links. Revocation invalidates the selected normal session and its token family; user disable/delete invalidates all canonical-user credentials. Safe event sink receives outcomes/correlation/record IDs, never secrets or full protocol responses. Precise wire routes/SDK types are finalized with the API implementation and contract tests; no placeholder endpoint counts as delivery.
