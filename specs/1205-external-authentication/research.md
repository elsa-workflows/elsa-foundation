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

## R6 — Role authority (owner decision pending)

The initial card was withdrawn by Sipke after a misclick. The repeated card asks whether mapped roles are external-session grants (recommended) or synchronize saved assignments. Do not use the withdrawn selection. Independent work, including R5, may proceed. Confirm and encode the response before mapped-role persistence/projection implementation.

In either design: raw provider permission/role claims confer no Elsa grants; mapping resolves existing Elsa roles; deployment role allow-lists and the editor's own grant boundary constrain policy changes; preview calculates prospective grants without provisioning.

Owner clarification, 4 November: users and roles are Elsa records; role permissions are defined in Elsa, and an external provider's own role names have no inherent Elsa meaning. Preserve the integrator-programmable admission/provisioning policy that can assign configured Elsa roles to newly created users. The pending card concerns only how an optional explicit mapping assigns existing Elsa roles on later external sign-ins; it does not propose importing role definitions or upstream permissions. This clarification does not settle session-only versus persisted mapped membership.

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

Configuration readiness is distinct from recovery eligibility. A valid enabled provider with default rejection and no eligible link cannot count as a recovery path. Removing/replacing an enabled effective path needs a proved remaining external path or actual local recovery, then whole-catalog CAS. A source transition may be safe even if the retired record itself is disabled; the effective baseline/override result is what matters. Protocol/session adapters must supply these checks before runtime qualification; their unavailable defaults make no readiness/recovery claim.
