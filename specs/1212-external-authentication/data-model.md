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
preserve old rows and histories; the 1.0.0-to-1.1.0 chain preserves existing content. Both IAM and authentication state now write schema 1.2.0, with additive semantic migrations and content-preserving upcasters so an older host cannot silently ignore the qualified-link session binding.

The optional authentication EF module also advances to schema `1.1.0` with an additive `DurableSessions` table,
preserving its Initial history and prior flow payloads through a content-preserving upcaster. The table uses exact
hashed tenant/user indexes, random 256-bit session IDs/revisions, protected immutable account/provider/link/time
bindings, UTC millisecond creation/absolute expiry and explicit revisioned revocation. Native reads are tenant-local;
token validation asks the canonical authority to open its own signed-identity tenant scope before resource selection.
No raw framework security stamp is retained: the authority checks an opaque SHA-256 binding against current IAM.
Status transitions update that epoch in the IAM transaction, preventing disable→enable resurrection. Missing older
epochs initialize only during verified sign-in, never eligibility/preview. Session creation/selected revocation reject
caller-owned transactions. Session paging is capped at 100; per-creation expiry cleanup is capped at 64 current-schema
rows of the current tenant. Cookie/access/refresh reference the same session and absolute lifetime; refresh never
extends it. Direct non-user bearer compositions retain their separate typed external subject boundary.

Qualified external sessions and recovery observations bind the exact link revision. Removing a qualified link advances
the owning user root beyond the removed link revision; new qualified links start above that root. Deleting and
recreating the same key, owner and timestamps therefore cannot revive the removed instance's credentials. Issuer-less
legacy links retain their existing revision behavior. Older external sessions without an exact link binding require a fresh sign-in; an upcaster never invents that binding. Removal preserves the account security version and other links,
so another provider for the same user remains usable. The native removal primitive owns a serializable transaction,
checks opaque user and exact link revisions, and updates the root registry and link row atomically. The administrative endpoint commits prospective remaining-user and global recovery guards with this mutation and the unchanged catalog CAS in database-managed mode.

## Private recovery observations

Every new observation also binds the actual issued durable Elsa session and its opaque account version. The
canonical sign-in result carries a server-only session ID excluded from JSON; recovery recording re-reads that
committed session for the exact user and connection. Recovery checks reuse the protected account/link binding
within the observation's own lifetime. A login ownership rebind rotates both affected account versions atomically,
so neither old credentials nor old observations revive after a link returns to its earlier owner. Older unbound
observations require a fresh normal administrative sign-in. Individual session termination cannot renew evidence.

Separate host-tenant/logical-key rows in the existing revision-aware global provider configuration store hold a versioned, Data Protection-protected bounded envelope of successful normal administrative sign-ins. Each proof carries source record, material revision, trusted callback, exact issuer/subject/user, opaque credential binding and observed UTC. No upstream tokens or secret values are retained. Up to sixteen recent observations per key; default eligibility window 24 hours, maximum thirty days. Expired entries never count and are pruned on the next record write. Preview never writes proofs. Configuration changes and live IAM/link/permission/credential/callback checks can invalidate proof earlier. These observations do not change material revision and are never a public management projection. Administrative catalog saves coordinate the complete catalog CAS with opaque editor, provisioning-role and selected recovery IAM revisions in one owned serializable transaction. Guarded attachment uses the same snapshot boundary and unchanged catalog CAS; deployment-only attachment needs only IAM. Qualified unlink uses protected normal-session provenance to prove a remaining user path and current administrative recovery, excludes the removed key, and commits the selected IAM guards with the mutation.

Protected normal-session records additionally carry selected connection material revision, opaque credential binding
and trusted callback. These optional server fields do not appear in session administration JSON or tokens. They
supply bounded retained credential history for unlink in both modes, without adding provider persistence to baseline
hosts. Authentication still uses the original expiry/revocation state; historical recovery checks never reactivate a
session. Older records lacking these optional fields cannot supply that evidence until a fresh normal sign-in.
