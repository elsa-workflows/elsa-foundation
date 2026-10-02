# Feature Specification: Opt-in external bearer normalization for a Worker host

**Feature Branch**: `codex/2304-worker-oidc-normalization-spec`

**Created**: 2026-10-02

**Status**: Draft

**Input**: [Program #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), [profiles epic #1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961), [specification task #2304](https://github.com/elsa-workflows/elsa-foundation/issues/2304), and the [Worker identity no-go](../../docs/reports/runtime-composition/worker-http-identity-boundary.md). This specification enables the missing authentication boundary; it does not publish a Worker profile or certify an external identity-provider deployment.

## User Scenarios & Testing

### User Story 1 - Authorize a Worker request from an external identity (Priority: P1)

An operator configures one external token issuer, API audience, provider and tenant for a single-host Worker. A caller with a valid token and an operator-owned permission mapping can execute and resume a persisted workflow through the existing Runtime API, without creating a local user account.

**Why this priority**: A Worker starting profile is useful only when its actual HTTP surface enforces the existing Elsa permission policies with real token validation.

**Independent Test**: Run one rebuilt web host with a real local issuer, durable mapping rules and workflow state. Send anonymous, invalid, valid-but-unmapped and legitimately mapped requests; observe the actual authorization results and complete a suspend/resume workflow.

**Acceptance Scenarios**:

1. **Given** an anonymous request, tampered token, wrong issuer or wrong audience, **when** execution is requested, **then** authentication fails with401 and no workflow is started.
2. **Given** a valid token without a legitimate execution grant, **when** execution is requested, **then** the caller is authenticated but denied with403.
3. **Given** a valid token containing forged Elsa normalization, tenant, provider, role or permission claims, **when** it is normalized without a legitimate mapping, **then** the internal claims are removed before rule evaluation and cannot self-authorize; the request is denied with403.
4. **Given** a legitimate mapping in the configured provider/tenant namespace, **when** the caller executes, suspends and resumes through the actual HTTP stimulus path, **then** the workflow completes and its state survives a fresh host restart.
5. **Given** a token naming another provider or tenant, **when** mappings are loaded, **then** only the host-configured namespace is used. The token cannot choose a mapping namespace.

### User Story 2 - Compose authentication explicitly without changing existing hosts (Priority: P1)

A host developer opts into normalization using declarative feature configuration or the equivalent service-registration API. A bearer-only host supplies its API audience without needing an interactive login client. Existing hosts that do not opt in retain their authentication behavior.

**Why this priority**: The configuration program needs a feature that future developer tools and the web builder can discover and configure, with predictable compatibility.

**Independent Test**: Compare a legacy OIDC composition, a bearer-only normalized composition and a mixed first-party/external composition. Exercise both registration orders, selected defaults and actual scheme behavior.

**Acceptance Scenarios**:

1. **Given** normalization is absent, **when** an existing host is rebuilt, **then** its token processing, host-selected defaults, interactive registration and first-party token paths remain unchanged.
2. **Given** an API audience but no interactive client, **when** a bearer-only normalized host starts, **then** bearer validation uses that audience and no interactive login handler is registered merely because an audience was configured.
3. **Given** no explicit API audience and an existing interactive client identifier, **when** bearer options are resolved, **then** the legacy client identifier remains the audience fallback.
4. **Given** missing or contradictory trust configuration or an incompatible event/registration replacement or response-writing callback, **when** the host activates the opt-in composition, **then** it refuses before serving requests with a stable, value-free diagnostic.
5. **Given** individually selected features and operator-owned deployment settings, **when** the composition is inspected, **then** its actual feature closure, routes, authentication scheme and mapping namespace are explainable. Issuers, connection values and credentials are not embedded into an immutable starting profile.

### User Story 3 - Change owned mappings and observe truthful failures (Priority: P1)

An operator changes durable mapping rules and understands the difference between authentication failure, permission denial and an operational failure. Updating local mappings affects later authenticated requests without pretending that upstream token claims have changed.

**Why this priority**: A grant that remains cached indefinitely or a backend failure disguised as permission denial would make the secured Worker misleading to operate.

**Independent Test**: Use the same valid token before and after a persisted rule update, restart the host, and inject mapping, normalization, evaluator and cancellation failures at their actual request boundaries.

**Acceptance Scenarios**:

1. **Given** an execution grant is removed from a stored rule, **when** the same valid token is used on a later request, **then** the request is denied403 without refreshing the token. Rules and their update survive a fresh host restart.
2. **Given** rules for another provider or tenant, **when** they are added or changed, **then** the configured namespace's grants are unchanged.
3. **Given** a request-time mapping-store or normalization failure, **when** authentication runs, **then** no successful ticket or partial normalized principal is published and the request returns401 with no raw peer exception detail.
4. **Given** an authorization backend failure after successful normalization, **when** permission evaluation runs, **then** the existing operational failure propagates rather than being reported as403 or permitting another grant source to authorize.
5. **Given** an aborted request, **when** mapping, normalization or permission evaluation is in flight, **then** cancellation is honored and no successful authentication ticket or workflow effect is produced after cancellation is observed.
6. **Given** an unchanged upstream token after an external group change, **when** capability metadata is inspected, **then** the existing external-provider token-refresh boundary remains advertised separately from per-request local rule evaluation.

### Edge Cases

- An explicit blank audience is invalid in opt-in mode; absence alone selects the legacy client fallback.
- A configured bearer scheme is changed, disabled, duplicated or replaced after the adapter is registered.
- Existing token-validated callbacks refuse, short-circuit, mutate the principal, or throw; a custom event type replaces the guarded normalization path. Custom challenge/forbidden response callbacks are unsupported in the first opt-in layout.
- A normalizer returns no authenticated identity, multiple identities, a raw or wrong normalized authentication type, duplicate/malformed markers, a wrong namespace or a null result.
- Incoming internal claims are used as rule match inputs, including casing variants; they must not survive the input filter.
- A rule update occurs during one request. That request uses its captured rule set; a later request loads the new set. No concurrent transactional snapshot promise is added.
- A cancellation arrives before rule loading, after rule loading, after normalization or immediately before ticket publication.
- A nondefault configured tenant encounters the untouched default persistence scope; an already-bound conflicting or global/privileged scope must refuse before lookup.
- A host composes the durable IAM store without local users. Merely selecting the store must not introduce account-provisioning writes.
- Two persistence resource names refer to one target, or IAM uses a different explicit target. Existing ownership and migration constraints continue to apply.

## Requirements

### Functional Requirements

- **FR-001**: Normalization MUST be explicit opt-in, discoverable through feature/settings metadata and available through equivalent developer registration; absence MUST preserve the legacy OIDC and first-party authentication paths.
- **FR-002**: The first supported layout MUST use one host-configured provider and nonblank static tenant. Incoming claims MUST NOT select either namespace.
- **FR-003**: Real issuer, signature, token lifetime and audience validation MUST precede normalization. Invalid tokens MUST NOT reach owned mapping lookup or become a trusted principal.
- **FR-004**: Bearer audience configuration MUST be independent of interactive client registration, with legacy client-identifier fallback only when audience is absent. Opt-in activation MUST reject missing or blank effective audience and missing issuer configuration.
- **FR-005**: The bridge MUST load the configured namespace's owned mapping rules on each validated request, filter incoming internal identity claims before matching, and reuse the existing normalization and permission contracts. It MUST NOT provision users or link external identities.
- **FR-006**: Successful normalization MUST produce exactly one authenticated identity with the exact adapter-owned normalized authentication type, distinct from the raw token identity type, exactly one valid normalization marker and the configured provider/tenant. Only the installed guarded path may enroll that authentication type as trusted; declaration of a scheme or incoming marker alone MUST NOT establish trust.
- **FR-007**: Registration and callback composition MUST preserve prior refusal/failure, prevent successful short-circuit bypass and refuse incompatible replacement. Normalization MUST be the final principal-changing operation on the guarded token-validation path before successful ticket publication.
- **FR-008**: Invalid configuration MUST fail registration/activation; request-time mapping or normalization failure MUST fail authentication401 without a ticket; normal negative permission evaluation MUST deny403; operational evaluator/resource failures MUST retain the existing propagation contract.
- **FR-009**: The active request-aborted token MUST be passed to rule loading and normalization, checked before and after their calls and immediately before publication. Cancellation MUST NOT be relabeled an ordinary permission denial or grant a ticket after observation; permission evaluation retains [Spec151 FR-024](../151-foundation-identity-permission-policy-bridge/spec.md).
- **FR-010**: The existing external-provider token-refresh capability metadata MUST remain unchanged. Per-request owned mapping updates MUST be documented separately from upstream token refresh/revocation.
- **FR-011**: The first acceptance host MUST use the existing durable IAM rule store with explicit target and schema provisioning, alongside a named workflow persistence resource. IAM MUST NOT be presumed enrolled in generic shared defaults. The host MUST initialize one ordinary persistence scope equal to the configured tenant before stores resolve; activation and request lookup MUST refuse absent/conflicting/global/privileged/across-scope context rather than overwrite it or let a token choose it. Persisted updates and fresh-process reload MUST be observed directly.
- **FR-012**: Declarative settings MUST expose the opt-in, namespace and bearer audience without embedding deployment settings into immutable profiles or inventing group-level settings inheritance. Host-selected authentication defaults MUST remain authoritative.
- **FR-013**: New diagnostics and evidence MUST avoid tokens, raw claims, mapping match values, connection values, credentials and raw collaborator exception text. Fixed failure classifications MAY identify the failed stage without carrying those values.
- **FR-014**: Acceptance MUST use real token validation, actual HTTP Runtime routes and persisted workflow state, not a handler that fabricates grants. It MUST cover adverse controls, namespace isolation, no account writes, compatibility and a restored mutation/revert control proving the actual trust boundary.
- **FR-015**: Worker profile publication MUST remain separate until the adapter and subsequent exact-composition host gates are delivered. Dynamic tenancy, multiple-provider arbitration, interactive normalization, external IdP interoperability and distributed topology remain deferred with explicit revisit triggers.

### Key Entities

- **Configured authentication boundary**: Operator-owned issuer, bearer audience, provider, tenant and exact runtime scheme; separate from profile membership.
- **Validated external principal**: Claims produced by real token validation before internal-claim filtering and owned mapping.
- **Owned mapping snapshot**: Rules loaded for one configured provider/tenant during one request; persisted independently of the token.
- **Normalized principal**: A rebuilt single trusted identity with mapped grants and canonical namespace/marker.
- **Acceptance host**: One from-source web host, isolated issuer, explicit durable IAM store and named workflow resource whose HTTP/database outcomes are observed.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every anonymous or invalid-token control returns401, every valid-but-ungranted control returns403, and the legitimately mapped caller completes the persisted execute/suspend/resume journey.
- **SC-002**: Zero forged internal claims or cross-namespace rules create an execution grant in the defined adverse matrix.
- **SC-003**: Removing a stored grant denies the next request using the same token; the updated rule and workflow state remain observable after a fresh host restart, with zero bearer-request user/link records created.
- **SC-004**: Both developer configuration entry points yield the same configured trust boundary; all retained legacy/default/first-party compatibility controls pass.
- **SC-005**: Every injected configuration, authentication, authorization and cancellation failure follows its defined outcome without a successful partial ticket or newly emitted sensitive value.
- **SC-006**: A controlled removal of each selected validation/normalization guard makes its adverse acceptance control fail; restoring the production guard restores the pass. The evidence labels local-issuer proof separately from external deployment interoperability.

## Assumptions

- #2057's no-go is the starting evidence, not a working external-authentication claim. Existing claim authorization does not require local user provisioning.
- Operator-owned mapping administration and rule persistence already exist; this slice adds no new IAM model or administrative UI.
- A single configured provider/tenant is a bounded first deployment. Revisit trusted tenant-context sourcing before supporting dynamic tenancy; revisit scheme/default ownership before multi-provider arbitration.
- Local issuer/discovery/key proof establishes protocol wiring and trust enforcement only. External IdP interoperability requires a later real deployment proof.
- Authoring API scope, portable unknown-setting export and six real builder-study participants retain their separate program gates.
