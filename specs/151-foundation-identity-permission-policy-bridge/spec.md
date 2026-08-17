# Feature Specification: Foundation Identity Permission Policy Bridge

**Feature Branch**: `1305-permission-policy-bridge`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "Unify endpoint permission authorization on Foundation Identity policies for Minimal APIs and transitional FastEndpoints routes, including explicit any/all semantics, wildcard compatibility, normalized claims, module-owned catalog contributions, hard-veto resource denial, and shared evaluator integration."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One Authorization Outcome Across Endpoint Styles (Priority: P1)

As an Elsa host operator, I need every first-party permission-protected endpoint to reach the same authorization decision regardless of how that endpoint is mapped, so a module migration cannot weaken or unexpectedly tighten access.

**Why this priority**: A shared outcome is the security boundary that makes incremental REST API migration safe. Without it, two routes asking for the same permission can disagree for the same caller.

**Independent Test**: Host one endpoint from each supported endpoint style with the same permission requirement and exercise the same anonymous, unauthorized, exact-grant, implied-grant, wildcard-grant, and replacement-evaluator callers against both. Each pair must return the same outcome.

**Acceptance Scenarios**:

1. **Given** two first-party endpoints mapped with different supported endpoint styles and the same permission requirement, **When** an anonymous caller requests either endpoint, **Then** both requests return HTTP 401 before any permission evaluator or resource decision can grant access.
2. **Given** the same endpoints and an authenticated caller without a satisfying grant, **When** the caller requests either endpoint, **Then** both requests return HTTP 403.
3. **Given** the same endpoints and a caller whose exact, implied, wildcard, or replacement-evaluator grant satisfies the requirement, **When** the caller requests either endpoint, **Then** both requests are authorized.
4. **Given** a transitional endpoint with an existing public route and action-scoped permission composition, **When** it is routed through the shared authorization path, **Then** its route and effective access behavior remain unchanged.
5. **Given** an authentication scheme that supplies an authenticated principal containing raw provider claims but no trusted normalized-principal marker, **When** either endpoint is requested, **Then** the normalized-principal requirement fails, the authorization result is challenged as HTTP 401, and no permission resource source or evaluator runs.
6. **Given** an unregistered runtime authentication type that supplies a forged normalized marker and raw Elsa permission claim, **When** either endpoint is requested, **Then** both return HTTP 401 with zero resource/evaluator calls; **When** a composite principal also contains a trusted normalized identity, **Then** only that identity is visible to resource handlers and evaluators.
7. **Given** a principal with two trusted normalized identities, including identities with different tenants/providers or disjoint grants, **When** a permission endpoint is requested, **Then** normalization trust fails closed with HTTP 401 and grants are never unioned.

---

### User Story 2 - Explicit Permission Composition and Resource Precedence (Priority: P1)

As a module author, I need unambiguous single, any, and all permission requirements with documented implication, wildcard, and resource-specific behavior, so I can secure an endpoint without depending on framework-specific claim checks.

**Why this priority**: Ambiguous boolean composition or denial precedence is a direct authorization risk and would make the shared path unsafe to adopt.

**Independent Test**: Evaluate one single-permission requirement, one any-permission requirement, one all-permissions requirement, and one resource-aware requirement against exact grants, partial grants, implied grants, wildcard grants, abstentions, grants, and denials. Every outcome must match the rules below.

**Acceptance Scenarios**:

1. **Given** a single-permission requirement, **When** the caller has that permission directly or through a valid implication, **Then** the requirement succeeds.
2. **Given** an any-permission requirement, **When** the caller satisfies at least one listed permission, **Then** the requirement succeeds; **When** none are satisfied, **Then** it fails.
3. **Given** an all-permissions requirement, **When** the caller satisfies every listed permission, **Then** the requirement succeeds; **When** any one is missing, **Then** it fails.
4. **Given** a caller with the administrative wildcard grant, **When** an ordinary permission is requested, **Then** the requirement succeeds; **When** the wildcard permission itself is requested without an explicit wildcard grant, **Then** it fails.
5. **Given** one or more resource-specific decision sources evaluating one permission member, **When** any source explicitly denies that member, **Then** denial is final for that member even if another source or the general evaluator would grant it.
6. **Given** resource-specific decision sources that all abstain, **When** the general permission evaluator grants access, **Then** the requirement succeeds.
7. **Given** a composite any- or all-permission requirement and a protected resource, **When** each permission member is resolved through resource sources and the general evaluator, **Then** an explicit resource deny vetoes only that member; any succeeds if another member succeeds, while all fails if any member is denied.
8. **Given** a resource source that throws, times out, or observes request cancellation before a later source that would grant, **When** authorization runs, **Then** the operational failure propagates and no later source, member, or evaluator is consulted.

---

### User Story 3 - Auditable Module Permission Ownership (Priority: P2)

As an Elsa security administrator, I need this slice to prove that enabled module permissions carry auditable ownership and that external identity claims are normalized before evaluation, so the program has a safe catalog contract it can apply to every first-party endpoint in later migration slices.

**Why this priority**: A shared evaluator is only operationally useful when administrators can discover the permissions it evaluates and identity-provider details do not leak into endpoint definitions. Complete endpoint-to-catalog coverage is a program outcome owned by the compatibility harness and subsequent module waves, not by this bridge slice alone.

**Independent Test**: Build a catalog from the enabled Studio Preferences and Module Management contributors, verify their permission ownership and lifecycle, and authorize representative callers whose grants originate from an external provider. The endpoints must consume only tenant- and provider-scoped normalized grants.

**Acceptance Scenarios**:

1. **Given** either the Studio Preferences or Module Management bridge-canary contributor, **When** its permission catalog is built, **Then** every permission in that module's declared bridge inventory is present and attributed to that module.
2. **Given** a permission claim from an external identity provider, **When** the caller is authenticated, **Then** the claim is normalized into Elsa's permission model before endpoint authorization and can satisfy the same requirement as a native grant.
3. **Given** a host replacement for the permission evaluator, **When** it grants or denies a requirement, **Then** every supported endpoint style honors that replacement without inspecting raw permission claims directly.
4. **Given** a non-permission custom authorization policy, **When** it is resolved alongside generated permission policies, **Then** its existing behavior is preserved.

### Edge Cases

- Empty single, any, or all permission declarations are rejected rather than becoming accidental allow-all or deny-all rules.
- Duplicate permissions in one declaration do not change its meaning or generate unstable policy identities.
- Permission names containing punctuation cannot collide with another single, any, or all policy representation.
- Implication cycles terminate deterministically and do not grant permissions outside the reachable implication set.
- Implication is grant-side and directional: a stronger granted permission may imply a requested permission, but a requested permission is never expanded upward into a stronger grant.
- Requesting the administrative wildcard is distinct from using an administrative wildcard grant to satisfy an ordinary permission.
- The administrative wildcard is the exact reserved key `*`. Every permission identifier is canonicalized by Unicode NFC normalization followed by invariant uppercase conversion and then compared ordinally across declarations, catalog and implication lookup, normalized claims, evaluator contexts, and policy identities. Declarations with leading or trailing whitespace are rejected, and `*` cannot be cataloged, implied, or produced by a catalog implication.
- For one permission member, an explicit resource denial wins over a resource grant, evaluator grant, implication, and wildcard grant; an abstention alone does not deny. In an any requirement another member may still satisfy the requirement, while in an all requirement one denied member fails it.
- A missing, malformed, or unknown generated permission policy fails safely and does not replace the host's fallback handling for unrelated policies.
- Existing single-permission policy names in the form `Elsa.Permission:<permission>` remain parse-only aliases during the documented deprecation window; new metadata emits only the canonical v1 form, and malformed names beginning `Elsa.Permission:v1:` never fall back to the legacy parser.
- A permission key attributed to multiple module owners is rejected rather than silently reassigned.
- Disabled, unloaded, or replaced modules do not leave phantom bridge-canary permissions in the active catalog.
- Multiple authentication providers can normalize different source claim names into the same Elsa permission without endpoint-specific knowledge, but only mapping rules bound to the current provider and tenant may contribute grants.
- Invalid declarations and catalog ownership conflicts fail during configuration or activation. An authenticated principal without a trusted post-normalization marker fails the Foundation permission policy's normalization requirement and is challenged as HTTP 401 by the authorization-middleware result handler. Evaluator or resource-handler exceptions propagate as operational failures rather than being disguised as permission denials; they cannot authorize or fall through to another grant source. HTTP request cancellation is obtained from the active `HttpContext` without replacing the protected resource, passed to the existing evaluator/resource-handler cancellation-token parameters, and propagated as cancellation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide one framework-neutral authorization contract for first-party endpoint permission requirements.
- **FR-002**: The public endpoint contract MUST expose `RequirePermission`, `RequireAnyPermission`, and `RequireAllPermissions` entry points for exactly one required permission, any-of a non-empty permission set, and all-of a non-empty permission set respectively.
- **FR-003**: Each public entry point MUST attach standard ASP.NET Core authorization metadata that Minimal APIs and the transitional FastEndpoints adapter can consume; adapter verification MUST cover all three entry points. All generated and legacy permission policy names MUST resolve through Foundation Identity's existing `IAuthorizationPolicyProvider` registration and its shared requirement handler/evaluator; endpoint adapters MUST NOT install or consult a competing permission policy provider.
- **FR-004**: Permission-protected endpoints MUST authenticate before permission, resource, implication, wildcard, or replacement-evaluator grants are considered. Anonymous callers MUST receive HTTP 401 and authenticated callers without a satisfying grant MUST receive HTTP 403 in both endpoint styles.
- **FR-005**: An exact permission grant MUST satisfy the matching requested permission.
- **FR-006**: Permission implications MUST be transitive, cycle-safe, and applied from granted permissions toward the requested permission only.
- **FR-007**: The administrative wildcard MUST be the reserved exact key `*`. An explicit normalized `*` grant MUST satisfy any ordinary requested permission, while a request for `*` itself MUST require an explicit normalized `*` grant. The wildcard MUST NOT be cataloged, implied, or accepted as an implication target.
- **FR-008**: Any-permission requirements MUST succeed when at least one member is satisfied and MUST fail when no member is satisfied.
- **FR-009**: All-permissions requirements MUST succeed only when every member is satisfied.
- **FR-010**: Empty, whitespace-padded, or otherwise malformed permission declarations MUST be rejected deterministically during endpoint configuration or activation before they can authorize a request. One canonical permission-key function—Unicode NFC normalization followed by invariant uppercase conversion—MUST be applied to declarations, catalog keys and implications, normalized permission claims, evaluator inputs, wildcard comparison, and policy formatting; canonical keys MUST be compared ordinally. Duplicate canonical keys MUST be de-duplicated without changing meaning or policy identity.
- **FR-011**: Permission policy identities MUST use the canonical grammar `Elsa.Permission:v1:<mode>:<tokens>`, where `<mode>` is `s` (single), `a` (any), or `l` (all), and each token is unpadded base64url of the UTF-8 bytes of a canonical permission key from FR-010. Any/all tokens MUST be de-duplicated and sorted ordinally by decoded canonical key, joined by `.`, and contain at least one token; single MUST contain exactly one. Normative fixtures are `read` → `Elsa.Permission:v1:s:UkVBRA`, any(`write`,`read`) → `Elsa.Permission:v1:a:UkVBRA.V1JJVEU`, all(`write`,`read`) → `Elsa.Permission:v1:l:UkVBRA.V1JJVEU`, `réad` (including canonically equivalent decomposed spelling) → `Elsa.Permission:v1:s:UsOJQUQ`, and `*` → `Elsa.Permission:v1:s:Kg`. The `Elsa.Permission:` namespace and the `v1:` version marker are recognized case-insensitively, but generated names MUST use the shown casing. Any case variant of a name beginning `Elsa.Permission:v1:` that does not strictly parse or whose decoded token is not canonical UTF-8 MUST fail closed and MUST NOT be reinterpreted as a legacy permission. For one documented major-version deprecation window, a non-empty, non-whitespace-padded `Elsa.Permission:<permission>` name whose suffix does not begin `v1:` under ordinal-ignore-case comparison MUST be accepted as a single-permission alias and canonicalized through FR-010; formatters and new endpoint metadata MUST never emit the legacy form. Names outside the reserved namespace MUST continue through unrelated-policy resolution.
- **FR-012**: Authentication-provider claims MUST be normalized into Elsa's permission representation before evaluation; endpoint mappings and evaluators MUST NOT depend on or authorize from provider-specific raw claim names. Normalization MUST strip untrusted incoming Elsa-internal identity claims—including any incoming normalized-principal marker—add only grants from rules scoped to the authenticated provider and tenant, combine matching rules deterministically by their configured order and stop behavior, and emit exactly one `elsa.identity.normalized = v1` marker only after successful mapping. Trusted first-party principal/token projection paths MUST follow the same marker rule. The marker MUST be trusted only on an authenticated identity whose exact runtime `AuthenticationType` is explicitly registered in an ordinal normalized-authentication-type set that is empty by default; provider packages/adapters MUST register that observed post-authentication type only after guaranteeing the strip-map-mark contract. The built-in mapping MUST cover `"Elsa.Foundation.Identity"` for the normalized external principal factory, `AspNetCoreIdentityDefaults.CookieScheme` for reconstructed cookie requests, and `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme` for validated bearer requests; the `"openiddict"` token-construction type MUST NOT be trusted as an endpoint identity. Every Foundation permission policy MUST include an internal normalized-principal requirement, and the shared permission handler MUST require exactly one trusted normalized identity without invoking resource sources or the evaluator on zero/multiple matches. Resource sources/evaluators MUST receive a principal containing only that one identity, so multiple tenants/providers are never unioned. A Foundation authorization-middleware result handler MUST challenge only an authenticated untrusted/unmarked/ambiguous failure of that requirement, MUST determine authentication by scanning all `ClaimsPrincipal.Identities` rather than the first identity, and MUST delegate unrelated policies and every other outcome to the captured host/default handler, independently of identity and authentication-versus-routing middleware order.
- **FR-013**: A host MUST be able to replace the existing single-permission general evaluator, and every supported endpoint style MUST honor the replacement with the same normalized principal, tenant context, permission member, and protected resource. The shared policy handler MUST own single/any/all composition and invoke the evaluator once for each unresolved member, preserving existing evaluator implementations without giving endpoint adapters a direct-claim fallback.
- **FR-014**: Resource-specific authorization MUST retain the existing single-permission handler contract and its three outcomes—grant, deny, and abstain. The shared policy handler MUST evaluate resource sources independently for each permission member before composing the member outcomes as single, any, or all.
- **FR-015**: For a permission member, any explicit resource denial MUST be a hard veto over resource grants, the general evaluator, implications, and wildcard grants for that member. A denied member MUST fail an all requirement but MUST NOT veto a different member that satisfies an any requirement.
- **FR-016**: For each permission member, any resource deny MUST mark that member denied; otherwise any resource grant MUST mark it granted; only when every resource source abstains MUST the general evaluator decide that member. The policy handler MUST then apply the declared single/any/all composition to those final member outcomes.
- **FR-017**: Transitional endpoints MUST use the shared policy path without changing public routes or established action-scoped permission composition. Existing `ConfigurePermissions()` with no arguments MUST map to a single `*` requirement; with one or more action permissions it MUST map to one any-of requirement containing `*` and those permissions. It MUST NOT emit multiple policies whose host composition would change OR into AND.
- **FR-018**: First-party endpoint code MUST NOT bypass the shared evaluator through direct permission-claim matching. Roslyn symbol/data-flow enforcement MUST reject FastEndpoints permission helpers and endpoint/base authorization decisions fed by permission claims, including `FindFirst`, `FindFirstValue`, `FindAll`, `HasClaim`, `Claims.Any`, aliases, and provider-specific permission constants. Each form MUST have a mutation fixture. A symbol/path-scoped reviewed allowlist is limited to non-authorization Identity token/session projection and the transport contexts explicitly deferred to #1356.
- **FR-019**: The enabled Studio Preferences and Module Management contributors MUST prove the bridge's module-owned catalog contract. Each catalog entry MUST expose stable owner/contributor provenance, each declared canary permission MUST have exactly one owner, and duplicate cross-owner attribution MUST fail activation. The reserved `*` grant is not a catalog entry.
- **FR-020**: Disabled, unloaded, or replaced modules MUST NOT leave their canary contributor entries in the active catalog; re-enabling or replacing a module MUST rebuild ownership from the active contribution set.
- **FR-021**: Existing non-permission authorization policies and the host's fallback policy resolution MUST remain functional.
- **FR-022**: Automated verification MUST prove identical authorization outcomes for representative endpoints from every supported endpoint style.
- **FR-023**: Automated verification MUST detect a regression that returns first-party endpoint bases to direct permission matching.
- **FR-024**: Invalid declarations, malformed generated policy identities, catalog ownership conflicts, normalized-authentication-type trust configuration errors, and provider/tenant mapping configuration errors MUST fail during configuration or activation. Authentication and provider-specific claim normalization together establish the trusted normalized Elsa principal before endpoint authorization. The shipping `AspNetCoreIdentityPrincipalFactory` MUST propagate a normalization exception before returning any principal or marker. Because no shipping request-authentication handler currently invokes `IClaimsNormalizer`, this slice MUST NOT add an unused provider handler; instead, the same-host contract adapter MUST map that exception to `AuthenticateResult.Fail`, HTTP 401, and no ticket, and any future external-provider request adapter MUST adopt that mapping. An authenticated untrusted or unmarked principal MUST fail the normalized-principal requirement and be challenged; both endpoint styles MUST return HTTP 401 and MUST skip resource sources and replacement evaluators. Once a trusted normalized principal exists, the first evaluator or resource-source exception/timeout MUST short-circuit the whole requirement and propagate as an operational failure, MUST NOT be converted into HTTP 403, and MUST NOT fall through to a later handler, member, or evaluator. For HTTP endpoint authorization, Foundation registration MUST call `AddHttpContextAccessor`; the shared handler MUST obtain `RequestAborted` from an `HttpContext` authorization resource or the injected accessor, MUST preserve the original protected resource, and MUST pass the token through both an additive `PermissionEvaluationContext.CancellationToken` property and the existing evaluator/resource-handler cancellation-token method parameter; `OperationCanceledException` MUST propagate. Without an active HTTP context it MUST use `CancellationToken.None`. Foundation Identity MUST NOT invent an implicit timeout budget. Only a normal negative authorization result produces HTTP 403.
- **FR-025**: Provider-level regression tests MUST resolve canonical single/any/all names and legacy single aliases through the registered Foundation Identity policy provider, prove they reach the shared evaluator, prove malformed v1 names—including mixed-case namespace and version-marker variants—fail closed without legacy reinterpretation, and prove unrelated named, default, and fallback policies still delegate to the host's existing provider behavior.
- **FR-026**: `IPermissionEvaluator`, `IPermissionPolicyNameFormatter`, and `IPermissionCatalog` MUST each remain single-replacement contracts with an explicit `Replace*` registration method. Default and explicit registrations MUST carry an internal marker naming the contract/implementation. `AddFoundationIdentityAbstractions()` MUST reject untagged pre-existing descriptors; startup validation MUST reject zero, multiple, direct post-default, or marker/descriptor-mismatched registrations and diagnostics MUST name all conflicting concrete types. Explicit replacement MUST work before or after Foundation registration; registration order MUST NOT select a winner.
- **FR-027**: Foundation's `IAuthorizationMiddlewareResultHandler` registration MUST be idempotent across repeated Foundation feature registration. On first registration, zero earlier host descriptors MUST use ASP.NET Core's default, one MUST be captured into a non-recursive fallback factory, and multiple MUST fail immediately with every descriptor identified; Foundation then installs one tagged wrapper. A host handler registered after Foundation MUST fail startup with a named ordering diagnostic. Challenge, forbid, success, and unrelated-policy delegation MUST preserve the captured handler's behavior.

### Key Entities

- **Permission requirement**: A security condition consisting of a composition mode (single, any, or all) and a canonical, non-empty set of permission identifiers.
- **Permission grant**: A normalized caller capability that may satisfy a requirement directly, by implication, or through the administrative wildcard rule.
- **Permission implication**: A directed relationship from a granted permission to another permission that it includes.
- **Resource decision**: A module-provided grant, deny, or abstain outcome for a specific protected resource.
- **Permission catalog entry**: A discoverable permission identifier, description, implication relationships, and stable owning module/contributor provenance.
- **Normalized principal**: An authenticated caller identity whose provider-specific claims have been converted into Elsa's canonical permission representation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fixed adapter matrix containing one Minimal API endpoint and one transitional FastEndpoints endpoint for each of single, any, and all requirements produces identical outcomes for every tested caller and resource decision.
- **SC-002**: Anonymous and authenticated-but-unauthorized callers receive HTTP 401 and HTTP 403 respectively in 100% of cases in the fixed adapter matrix.
- **SC-003**: Exact, implied, wildcard, any, all, non-ASCII/canonically-equivalent key, normalized-provider, resource-aware, and replacement-evaluator scenarios are each covered by repeatable automated evidence with no contradictory outcomes.
- **SC-004**: 100% of the declared Studio Preferences and Module Management permissions appear in the active catalog with exactly one stable owner; disabling, unloading, or replacing either contributor leaves no stale entries. The reserved `*` grant is excluded.
- **SC-005**: No migrated or transitional first-party endpoint route changes its public path, HTTP method, or established action-scoped permission behavior as part of this work unit.
- **SC-006**: Automated architecture verification reports zero non-allowlisted first-party endpoint/base authorization paths that use FastEndpoints permission helpers or read permission claims directly, and a mutation fixture proves the guard fails when such a bypass is inserted.
- **SC-007**: A host-supplied evaluator can change an authorization result for every supported endpoint style without any endpoint code change.
- **SC-008**: One registered Foundation Identity policy provider resolves 100% of canonical and legacy permission-policy fixtures, rejects 100% of malformed v1 fixtures including mixed-case variants, and preserves the tested host named/default/fallback policy fixtures; no endpoint adapter registers a second permission provider.

## Assumptions

- The existing authentication system continues to establish authenticated principals; this feature owns authorization after authentication, not authentication-provider selection.
- HTTP/JSON remains the management and module API protocol; protocol redesign and broad endpoint migration are outside this work unit.
- Resource-specific denial is intentionally fail-closed and final. A resource source that cannot decide must abstain rather than deny.
- Administrative wildcard compatibility is retained only as a grant-side rule; ordinary grants cannot satisfy a request for the wildcard permission.
- Legacy `Elsa.Permission:<permission>` single-policy names are a compatibility input, not a new authoring format. They remain accepted for one documented major-version deprecation window and must be removed only through the repository's replacement-contract process.
- This work unit proves catalog ownership and lifecycle with the existing Studio Preferences and Module Management contributors. Complete endpoint-to-catalog inventory is enforced by the migration safety harness in #1346 and completed per module in #1347-#1350 follow-up waves.
- Existing public and host-credential security dispositions remain separate from permission-required endpoints and are not converted into permission requirements here.
- Removal of the transitional endpoint framework is handled by later program slices; this work unit supplies the compatibility bridge required during migration.
- The architecture baseline in ADR 0068 and parent program #1342 governs endpoint scope and migration sequencing.

## References & Traceability

- Delivery slice: [#1344](https://github.com/elsa-workflows/elsa-foundation/issues/1344)
- Parent program: [#1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
- Architecture decision: [ADR 0068](../../docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
- Spike report and evidence: [REST endpoint framework and authorization spike](../../docs/reports/endpoint-framework-authorization-spike-2026-08.md)
- Follow-on catalog completeness and migration evidence: [#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346), [#1347](https://github.com/elsa-workflows/elsa-foundation/issues/1347), [#1348](https://github.com/elsa-workflows/elsa-foundation/issues/1348), [#1349](https://github.com/elsa-workflows/elsa-foundation/issues/1349), and [#1350](https://github.com/elsa-workflows/elsa-foundation/issues/1350)
