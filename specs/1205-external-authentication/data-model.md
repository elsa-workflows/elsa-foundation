# Data model

## Token subject (independent #2848 prerequisite)

A validated immutable value has tenant, subject kind, subject ID, and, for external subjects, provider and validated issuer. Canonical users use `(tenant, Elsa user ID)`; unlinked external identities use `(tenant, provider, issuer, subject)`. Exact opaque values are length-framed before hashing into a bounded versioned OpenIddict storage key. Subject kind separates a user from an equal-looking external identifier. Canonical user claims and all subject claims must be unambiguous.

Issued first-party tokens retain canonical user ID separately and external-origin provenance when applicable. Refresh state retains the full typed subject, not just a raw subject ID. First-party external JWT subjects are scoped keys; original upstream subjects remain separate internal provenance. Every first-party bearer validates its signed scope key. Old unscoped access tokens and refresh payloads are refused after the pre-GA security change, rather than guessed into a scope. Cookie/user projection replaces incoming NameIdentifier/sub/tenant with the resolved canonical user. Logout and later user/session administration invoke typed scoped revocation.

## Connection catalog

One bounded global provider-configuration aggregate holds database connection documents under a versioned kind and catalog revision/ETag. Each document has immutable record ID/logical key, adapter type/settings, secret references, admission/default-role policy, display/preferred order, enabled intent, archive timestamp and explicit override intent. Material revision excludes observation timestamps. Deployment options hold baseline documents and the allowed override/role boundary. No field merging. Catalog CAS checks source lifecycle and final usable path atomically.

## External identity and admission

Exact tuple `(tenant, logical key, issuer, subject)` maps to one Elsa user. No issuer/subject case folding. Atomic link-or-create prevents orphan JIT users and conflicting ownership; default role IDs reference existing Elsa roles. Existing-user matching returns one eligible tenant user with explicit evidence. Pure assessment returns reject/existing/create/match and prospective Elsa grants without performing writes. The integrator policy may select declared existing Elsa roles at initial provisioning. No recurring mapped-role membership or session grant is modeled; subsequent sign-ins use saved memberships.

## Flow, preview and session

Separate Identity-owned persistence: purpose, hashed handle, expiry, protected sensitive state, browser/admin binding, tenant, selected record/logical key/material revision and secret generation. The optional `Identity.ExternalAuthentication` EF module owns flow and preview artifacts, independently of base IAM, with a `1.0.0` schema family and separate four-provider migration history. Atomic conditional deletion acknowledges exactly one unexpired matching consumer; caller-owned transactions are rejected. Preview result is separately purpose/admin/tenant-bound and cannot issue a normal session. Session records carry ID, tenant/user/connection provenance, creation/expiry/revocation and safe metadata; no raw upstream credentials. Normal cookies and token entries reference session identity for inspection/revocation. User disable/delete must invalidate access/refresh/cookies under the same canonical user scope.

Managed links extend the existing `ExternalIdentityRecord` with a validated issuer and use `ExternalIdentityKey`
for qualified lookup. Null issuer is a separate legacy namespace, never a fallback. The additive IAM migrations
preserve old rows and histories; the 1.0.0-to-1.1.0 chain preserves existing content while new writes stamp 1.1.0.

## Private recovery observations

Separate host-tenant/logical-key rows in the existing revision-aware global provider configuration store hold a versioned, Data Protection-protected bounded envelope of successful normal administrative sign-ins. Each proof carries source record, material revision, trusted callback, exact issuer/subject/user, opaque credential binding and observed UTC. No upstream tokens or secret values are retained. Up to sixteen recent observations per key; default eligibility window 24 hours, maximum thirty days. Expired entries never count and are pruned on the next record write. Preview never writes proofs. Configuration changes and live IAM/link/permission/credential/callback checks can invalidate proof earlier. These observations do not change material revision and are never a public management projection. Existing catalog CAS still coordinates concurrent connection retirement; cross-IAM/link/session mutation integration remains T024.
