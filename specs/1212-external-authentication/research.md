# Research and design decisions

Source baseline: Foundation `73040cd7b1368818820d2a60902443a9f8a82c7f`; Elsa 3 local source `28091a9d7b5f29fe9c5dbf2a9ef373d107fae755`. Research date: 2026-11-04. Source inspection is not runtime qualification.

## R1 — Reuse Identity, keep provider interaction on the backend

Decision: a fixed backend external-authentication broker and installed protocol adapters, initially OIDC, finish through the existing Identity cookie and OpenIddict pipeline. Preserve settings-driven direct external bearer mode.

Rationale: `Identity/Oidc/Extensions/OidcAuthenticationServiceCollectionExtensions.cs` registers one startup-configured remote handler. Runtime named handlers would require every node to coordinate scheme registration, options invalidation, secret resolution and historical revisions, while still needing shared one-time state. The broker reads effective connections and pins material revisions. Reuse IdentityModel discovery, OIDC and JWT validators. Do not reproduce Elsa 3's Blazor-specific client-code/token broker.

## R2 — One bounded revisioned connection catalog

Decision: keep typed host-wide connection documents in one versioned global provider-configuration record. Database management remains opt-in; deployment baselines remain options. A catalog CAS includes effective-state computation and the final-login guard. Flow, preview and session state use separate Identity-owned durable stores.

Rationale: `EfProviderConfigurationStore` already implements create-only/expected-revision writes on four relational providers, but has no list/delete contract. A bounded catalog avoids a second configuration layer and prevents two independent writes concurrently removing different final login paths. Authorize host-wide administration before entering privileged global persistence. Initial bound: 256 database records, including archived records, with bounded settings/diagnostics. Per-connection rows would need enumeration/deletion and a separate serializable catalog guard.

## R3 — Explicit complete overrides

Apply the owner decision in #3579: same-key database records do not override automatically. An authorized explicit full override must be permitted by deployment policy. Disabled overrides shadow; archive/removal reveals the baseline; restore reinstates the override. Record IDs are not logical provider identity. No field merging.

## R4 — Admission and exact external identities

Decision: share pure admission assessment between login and preview. Resolve an active existing link; otherwise reject by default, explicitly provision, or use one explicitly configured matcher. Initial matching supports verified email with a unique eligible match; extension contracts permit installed matchers. No username/email inference. Atomic IAM publication must prevent concurrent JIT from producing orphan users or duplicate links.

Current principal factory automatically creates missing users and links. Existing links lack issuer and stores case-fold subjects. Managed links require exact `(tenant, logical key, issuer, subject)` identity. Issuer/subject remain opaque and case-sensitive. Do not infer issuer for old links; pre-GA migration must preserve or explicitly refuse ambiguous old records.

## R5 — Scoped token identity (#2848 prerequisite)

Decision: a typed token subject distinguishes canonical Elsa users `(tenant, user ID)` from external identities `(tenant, provider, validated issuer, subject)`. Versioned length-framed hashing provides the token-store/revocation key, without delimiter concatenation or issuer/subject case folding. Canonical identifiers replace upstream NameIdentifier/sub. Refresh retains typed identity; logout/admin revocation use the same scope with no raw-subject fallback.

`FoundationIdentityApi` chooses first NameIdentifier; `IdentityClaimsProjector.AddIfMissing` appends identifiers; OpenIddict stores/revokes on bare subject. Fix managed sign-in and existing normalized direct bearer exchange. Tests must prove provider/issuer/tenant B's refresh token survives A's HTTP sign-out. Pre-GA public token APIs may change; update tracking, consumers and docs together.

## R6 — Provisioning policy only (owner decision, 4 November)

The owner selected: “Provisioning policy only: assign saved Elsa roles through the integrator’s policy; omit external group-to-role mappings for now.” This considered answer replaces the withdrawn misclick. Elsa owns users, saved role memberships, role definitions and their permissions. An integrator's named provisioning policy may assign existing Elsa roles when creating a user, within the connection's declared role boundary. Later sign-ins use saved Elsa memberships and preserve manual assignments.

There is no recurring external group-to-role mapping, session-only mapped grant or provider-managed saved membership in this feature. External role/group names do not grant authority by name; upstream permission claims never pass through implicitly. Deployment role allow-lists and the editor's own delegation boundary still constrain provisioning policy changes. Preview computes prospective saved grants without provisioning. The separate existing direct-bearer normalization bridge retains its own explicit configuration and scope.

## R7 — Sessions and revocation

Decision: attach durable session identity/connection provenance to normal Elsa sessions/token state. Check revocation and user status on cookie, bearer and refresh use. Support inspection and selected-session revocation; existing logout-all semantics stay explicit. Disable blocks new sign-ins; a separately confirmed disable-and-revoke action revokes sessions. No competing upstream credential authority or audit store.

Current cookie security stamps affect every user session; token refresh simply reissues stored claims. Session identity permits selected revocation while preserving unrelated users/sessions.

## R8 — Durable flow state and preview

Decision: separate Identity-owned stores carry purpose, hashed opaque handle, expiry, browser binding, connection/material revision and protected payload with atomic conditional consume. Use shared host Data Protection keys. Pin secret generation/fingerprint without persisting resolved values. Reject stale material, expiry and replay. Preview has separate purpose/callback and administrator-bound results; stop before IAM writes, cookie or token issuance.

Multi-node claims require shared connection/IAM/flow/session/token stores, key ring and signing credentials plus actual restart/two-node journeys. In-memory and independent SQLite files are not multi-node evidence.

## R9 — API/Studio and bounded Phase 1 dependencies

Decision: optional Identity external-auth API/Studio modules use existing permissions, capability discovery, SDK transport, Security navigation and theme tokens. Use public Secrets metadata picker and `ISecretValueResolver`, not UI internals. Workflow/service Connections provides UX patterns, not the identity aggregate.

#2774/#2776 are closed. User/role APIs/pages (#2777/#2778/#2780/#2781), escalation/last-admin (#2779), applications (#2775) and global Studio permission work (#2782) remain open. Add only bounded user/role lookup and delegation checks needed here; do not claim those issues delivered. Preserve ownership capabilities; cookie composition does not make external IAM editable.

## R10 — Justified improvements

Carry forward Elsa 3's tests, previews and secret protection. Improve provenance/effective-state confirmation, copyable callback URLs, configure → test → preview → enable continuity, bounded actionable diagnostics and prospective JIT permission projection. No new protocols, scheduled health history, audit database or speculative advanced trust overrides.

## Authority and initiative boundary

Owner authorization on 4 November covers #3579 end to end and necessary #2848 integration. Neither #3579 nor #2771 has a native parent/Part-of link. The open Initiative search found #3052: its board covers workflow conversion/expression/runtime semantics and lists parity only as related work, without external authentication/ADR 0085 as a board topic. The initiative agreement gate does not apply here. Amend ADR 0085 D3 under the explicit owner decision; change no constitution gate and ratify no provisional sections.

## Implementation refinement — recovery and optional dependency envelope

Managed connection contracts and implementation use the optional `Identity/ExternalAuthentication` submodule. The Core references Identity.Core and Secrets.Core, and the implementation consumes Runtime.Core scope contracts, keeping those dependencies out of the base Identity.Core. Catalog persistence still reuses the existing provider-configuration authority and its schema.

Configuration readiness is distinct from recovery eligibility. A valid enabled provider with default rejection and no eligible link cannot count as a recovery path. Removing/replacing an enabled effective path needs a proved remaining external path or actual local recovery, then whole-catalog CAS. A source transition may be safe even if the retired record itself is disabled; the effective baseline/override result is what matters. Managed defaults now compose actual installed protocol/session/IAM dependencies. External recovery needs recent protected normal administrator sign-in evidence plus current permission/eligibility/exact-link and bounded discovery/JWKS checks; local recovery requires an actual eligible administrative password account. Configuration and preview alone never count. Default evidence window is 24 hours and local search considers the actor first, then at most 1,000 accounts. Source/material/callback/secret generation invalidate observations; catalog CAS prevents two connection writers from both removing their proved fallback. Cross-IAM/link/session mutation integration and full deployment proof remain required.


## Admission implementation refinement

The managed service separates pure assessment from atomic admission. Default rejection cannot be overridden by a
contribution named `reject`. Built-in `jit` uses explicit default Elsa roles; `match` plus `verified-email` requires
one boolean verification claim and a unique active Foundation-managed target. It does not auto-link an earlier
unverified imported profile. A custom named policy may choose a subset of the connection's declared role boundary;
matching also checks the candidate's complete effective role/direct-permission boundary. These are routine safe
implementation choices within explicit provisioning/matching acceptance, under the provisioning-policy-only owner decision.

Host ownership/capability settings govern whether new user/link state may be written; existing qualified links
remain readable. Matching passes a frozen membership snapshot to the atomic authority because membership changes
can add privilege without changing UserRecord fields. The old IPrincipalFactory auto-create/import behavior is
retired; its pre-GA context is canonical `(tenant, user ID, optional provider metadata)` only. Neither managed
assessment nor canonical projection imports upstream grants. Direct bearer normalization remains separate.


## Managed OIDC implementation refinement

The optional adapter uses fixed broker inputs, the installed IdentityModel signature/OIDC validators, and fresh
bounded discovery/JWKS HTTP reads. It does not mutate dynamic ASP.NET authentication schemes or the direct bearer
feature. Strict exact issuer/client audience, public-key algorithms, S256/nonce, endpoint-origin policy and
point-of-use referenced-secret generation/value binding are implemented. Unknown-kid rotation refreshes the
trusted JWKS once without reusing the code. The callback broker still owns distinct callback/purpose, browser
binding, durable atomic state and secret/material pins; the protocol contribution alone cannot issue an Elsa session.

Primary protocol constraints were checked against OIDC Core §3.1.3.7, RFC 9700 §4.4.2 and RFC 7636 §4 (linked in
the adapter README). A controlled Kestrel provider fixture exercises actual HTTP client authentication, PKCE,
single-use codes, signed tokens, malformed evidence and bounded failures. It is not an external-product or
composed Elsa browser/deployment witness, and T026-T029 remain open.

## Coordinated IAM/provider persistence refinement

Decision: reuse `EfSharedTransaction` for one physical, serializable transaction over the existing IAM and
provider-configuration contexts. The selected EF coordinator requires the same relational provider and exact
configured connection string. A different authority can implement the Core coordination contract; unsupported
or split-target compositions must not claim atomic recovery protection. No lock journal, lease, fence table or
distributed transaction is introduced.

`IRevisionAwareUserStore` and `IRevisionAwareRoleStore` can capture opaque `IamRevisionGuard` snapshots. A user
guard includes the account instance/security version and numeric revision; a role guard includes its definition
contents and revision. This prevents delete/recreate at the same numeric revision from satisfying an older
snapshot. Guard capture is read-only and old accounts without an epoch return no guard. The provider store can
save a global configuration together with conditional IAM revision advances, preserving account epochs,
permissions and payload schema. Any stale guard, stale catalog, save failure or unknown schema aborts the whole
owned transaction. Caller transactions/changes are refused. Serializable reads protect the snapshot check until
its conditional update, including deletion/recreation between those operations.

Implemented primitive proof: native SQLite commit/rollback, competing independent writers, stale user/role/
catalog/tenant guards, recreated user/role instances, old-readable/future schema, split target, caller transaction
and pending changes. An actual managed HTTP composition resolves the coordinator and preserves existing cookie,
access and refresh credentials after a guard-only revision advance. This does not yet wire managed retirement or
unlink; T024 remains open.

Remaining integration:

1. Capture user and all referenced role guards before and after actual authorization, delegation and recovery
   checks. Include the editor, outbound admission roles and selected local/external recovery identity. Merge equal
   guards; conflicting snapshots require a retry. Default unsupported contributions fail closed.
2. Carry guards as server-only catalog write metadata and route managed mutations through coordinated provider
   saves. Preserve the source/material model and existing full-catalog expected revision. Expose actual missing
   coordination capability in API/Studio guidance.
3. Bind external sessions and protected recovery observations to the exact qualified link revision as well as
   account epoch. Qualified removal must not revive old credentials after the same link is recreated.
4. Preserve monotonic qualified-link revisions through deletion/recreation without another schema column: a new
   link's revision must exceed its owner's previous root revision, ownership changes must advance both root counters to
   at least the link revision, and removal must advance the owner's counter beyond the removed link's revision.
   Creation/admission and all qualified writes must maintain that invariant. An account recreation changes its
   epoch. Verify with a frozen clock and ownership round trips; do not rely on timestamps or a numeric revision
   that restarts at one. Ordinary sign-in must preserve sibling sessions.
5. Remove a qualified link and touch the unchanged catalog revision in that same coordinated transaction, using
   the expected owner guard/link revision. Preserve other credentials and evidence when only that link is removed;
   explicit ownership rebind still rotates affected account epochs. Prove per-user alternative sign-in and global
   administrative recovery, including forced catalog/link/IAM races and truthful storage failures, before exposing
   the removal endpoint or completing T024. Finish T025 and the full deployment/acceptance matrix afterward.


### Managed catalog coordination integrated

Integration items 1–4 above are implemented: the default authorizer captures the live editor account, current saved
role definitions and proposed provisioning roles around current grants/delegation and canonical sign-in eligibility.
Local and external recovery capture the eligible identity and its saved/admission role roots around actual checks.
Repeated guards must agree; mismatched snapshots refuse. The writer passes server-only guard metadata to the catalog,
which routes administrative saves through the owned serializable IAM/provider transaction. Missing storage capability
refuses writes and is projected as read-only host management in API/Studio. No fallback to an uncoordinated save occurs.

Actual managed-host tests change actor/recovery status, membership, framework lockout, role definitions or the exact
qualified link from an independent scope after all final-login checks and before save. Every catalog mutation is
rejected without changing its revision or enabled paths. A provisioning-role definition change also rejects a draft.
Bypassing coordinated storage makes all seven race cases fail; restored source uses the coordinated branch.

Qualified link/session revision binding and monotonic remove/recreate counters are also implemented, with semantic
1.2.0 schema markers preventing an older host from silently ignoring these semantics. Item 5 (prospective guarded
unlink, including the unchanged catalog in database-managed mode and preserving deployment-only operation) remains
required before exposing removal or checking T024. Manual attachment now carries the delegation snapshot and optional unchanged catalog CAS into its atomic commit. T025 and the deployment/acceptance matrix remain unchanged.


### Guarded attachment and native link coordination integrated

The selected IAM backend owns optional `IExternalIdentityAdministrationStore`. It captures no policy itself:
trusted callers provide an opaque target owner and every checked tenant-local IAM root. The coordinator owns a
serializable IAM transaction and, when selected, the co-located global provider transaction. It advances guards,
performs existing-user attachment or exact qualified removal, and CAS-saves the unchanged provider document as one
unit. Qualified removal leaves its target root untouched until the relationship mutation compares the original
owner guard and advances the monotonic counter. Both operations preserve account epochs and sibling links.

`IExternalLinkAuthorizer` captures target/editor guards around live canonical editor eligibility, current grants,
roles, membership, ownership and delegation checks. The managed attachment API uses that snapshot after remote
metadata testing and source rechecks. Its default catalog bridge prepares the unchanged full-catalog document;
missing coordination fails closed. In deployment-only mode, no provider context/store is required or resolved.
The IAM backend marker owns the optional coordinator; replacing it after selection is a composition error.

Native tests cover both modes, stale IAM/catalog guards, rollback after provider save failure, and acknowledgement
loss after physical commit. Four actual HTTP races change editor role/status/membership or target role after the
final capture; all refuse. Bypassing native guard comparison makes all four fail. A deployment-only HTTP host omits
provider persistence entirely and proves attachment followed by actual provider sign-in as that existing user.

The prospective unlink integration described below is now implemented and qualified. T025 and all full product/deployment/consumer/review/merge/main gates remain required.

For prospective unlink, retain actual normal-sign-in material revision, opaque credential binding and trusted callback
in the existing protected durable session provenance. This gives deployment-only hosts a bounded history of actual
credential use without adding provider persistence or treating a merely configured link as proof. Recheck current
material/secret/callback/account/exact-link bindings, and exclude the removed key. Per-user recovery may use that
user's eligible local password or a proved remaining provider; global recovery additionally requires current provider
management permission. Capture those identities/roles with the editor/owner snapshot and commit through the existing
guarded removal, including the unchanged catalog CAS when database management is enabled. Ended individual sessions
may supply bounded historical credential evidence, consistently with existing private recovery observations; they
must never become live sessions again. Missing old provenance fails closed and requires a fresh normal sign-in.


### Prospective unlink integrated

Normal sessions now retain the material/credential/callback provenance above in their existing protected payload.
`IExternalLinkRecovery` checks the target's remaining local password or actual normal-session credential history,
and a current administrative recovery identity, excluding the removed qualified key (including provider casing
aliases). External evidence rechecks material, secret binding, callback, account epoch, exact link revision, current
eligibility and admission. Historical observation does not restore the ended session. Scans use existing bounds,
retain at most the newest hundred sessions per examined user and stop after thirty seconds; missing/cleaned-up old
evidence requires fresh normal sign-in. Current grants, not upstream roles, determine administrative eligibility.

The removal endpoint compares exact owner/link/user/catalog revisions, captures editor/delegation and both recovery
snapshots, and invokes the guarded native removal/catalog transaction. Inspection exposes `canRemove`; legacy
issuer-less entries and denied capabilities are read-only. Requests share the declared user/link grants, closed JSON,
antiforgery and body bounds. Storage failure/unknown acknowledgement returns an unavailable envelope, requiring
state refresh, and emits a redacted failure notice without claiming no mutation occurred.

Actual controlled-provider HTTP tests cover both modes, ended evidence, last-user/global path refusal, configured-only
links, stale secret/source/callback/window/account/link evidence, exact owner/issuer/subject/revisions, request/security
boundaries, missing old provenance/fresh sign-in, post-check IAM/catalog races, rollback, acknowledgement loss and
retry. Two independently constructed runtime nodes share native stores/keys and cannot remove both remaining links.
This is native SQLite/in-process node qualification; complete full-host/separate-process/server-engine/Studio
acceptance remains in T026–T031.
