# Feature Specification: Authorized module context on every Weaver turn

**Feature Branch**: `agent/codex/3439-weaver-turn-context`

**Created**: 2026-10-31

**Status**: Draft

**Input**: Program [#3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), M2 [#3431](https://github.com/elsa-workflows/elsa-foundation/issues/3431), feature [#3439](https://github.com/elsa-workflows/elsa-foundation/issues/3439). Carry authorized module context and tools through every turn while preserving the module ownership and delivery boundaries in the [Program charter](../../docs/program-goals/weaver-across-studio.md).

## User Scenarios & Testing

### User Story 1 - See the context used for this request (Priority: P1)

A Studio user asks about the current screen or submits a module-prepared request with explicit context. Weaver preserves the request identity, includes appropriate current context and shows which evidence was admitted, limited or unavailable. Opening another resource changes subsequent requests without changing the target of an earlier proposed operation.

**Why this priority**: Correct answers and safe review require knowing the resource and revision the assistant actually received.

**Independent Test**: Submit correlated requests on resource A and then resource B, including explicit context, filters, selection and unsaved state. Compare the displayed evidence with what the assistant received; try the earlier prepared operation after navigation.

**Acceptance Scenarios**:

1. **Given** a request with explicit attachments and an originating module, **When** current context is gathered, **Then** explicit attachments and correlation survive alongside eligible active context, with their distinct provenance.
2. **Given** collection produces restricted, partial or oversized evidence, **When** the request is admitted, **Then** the display describes the admitted evidence and its limitations without presenting rejected content as available.
3. **Given** a session started on A, **When** the user navigates to B or changes its selection or working state and sends a new request, **Then** the new request describes B's current state and the prior operation remains bound to its original target or refuses explicitly.
4. **Given** a module contributes a previously unknown resource, **When** its context is shown, **Then** generic Weaver presentation uses contribution metadata without interpreting domain-specific fields.

### User Story 2 - Keep every disclosure within current authority (Priority: P1)

A user can lose a permission, change identity, or have a module disabled between sending a request and receiving an answer. Weaver checks the current authority and policy before offering tools or disclosing resource evidence, including when continuing after an actual applied result.

**Why this priority**: Authority at message acceptance does not authorize every later disclosure or operation.

**Independent Test**: Accept a request, revoke the resource's read authority, authenticate normally with the changed authority and then continue. Repeat after one genuine applied effect. Inspect the assistant's input and independently verify the owning resource.

**Acceptance Scenarios**:

1. **Given** accepted context and an authority change before provider use, **When** the turn starts, **Then** currently forbidden resource content and guidance are withheld and the limitation is explicit.
2. **Given** a prepared operation and changed user, tenant, permissions, scope, module availability or host policy, **When** tools are advertised or invoked, **Then** advertisement and execution apply the applicable current restrictions; model output and client claims confer no authority.
3. **Given** one accepted applied result before an authority change, **When** the assistant resumes, **Then** current disclosure restrictions apply to context, history and results while the accepted effect remains truthfully recorded and is never repeated to recover evidence.
4. **Given** sign-out/sign-in or module reconciliation, **When** another request is sent, **Then** the active identity and contribution lifetime determine the context; an older editor or invocation cannot silently acquire the new lifetime.

### User Story 3 - Continue safely when a contributor fails (Priority: P1)

A user receives a bounded, understandable outcome even when a context contributor throws, reports a limitation, ignores cancellation or never finishes. Optional missing context is visible; missing required context prevents an unsupported answer. Stop ends the pending request, and late collection cannot change a newer request.

**Why this priority**: One optional module must not prevent useful work, and required missing evidence must not be hidden.

**Independent Test**: Combine a successful contributor with required and optional contributors that fail, stall, exceed limits or finish after cancellation. Observe the request's terminal disposition and what is disclosed.

**Acceptance Scenarios**:

1. **Given** a failing optional contributor, **When** complete eligible contributions are available, **Then** the request can continue with a visible bounded limitation.
2. **Given** a required contributor that reports failure, throws or does not finish within the configured bound, **When** collection ends, **Then** provider use is blocked with the missing requirement identified.
3. **Given** collection that never resolves, **When** the user selects Stop or the configured bound expires, **Then** the request finishes with an explicit outcome without waiting for that contributor to cooperate.
4. **Given** a cancelled request followed by a newer request, **When** old collection finishes, **Then** it cannot update the newer request's context or start a provider turn.
5. **Given** two requests dispatched together, **When** admission occurs, **Then** each correlation receives one explicit disposition and neither request is silently overwritten or accidentally duplicated.

### User Story 4 - Use the same module boundaries through either provider (Priority: P2)

A module author contributes guidance, evidence and tools once. Users receive the same admitted tool set and denial outcomes through either supported provider. Resource text remains data, independently of whether it looks like instructions.

**Why this priority**: The Program's extensibility promise must survive shared-lifecycle changes.

**Independent Test**: Exercise the same separate resource package through both existing provider paths, including authorized and denied requests, instruction-shaped resource data, secret content and lazy detail acquisition.

**Acceptance Scenarios**:

1. **Given** installed module guidance and untrusted resource text, **When** the assistant receives them, **Then** trusted guidance is distinguished from resource data and resource text cannot elevate trust or authority.
2. **Given** secret values or unrelated records, **When** context or tool results are prepared, **Then** forbidden values and unrelated evidence are excluded before provider use and bounded authorized detail acquisition remains available when permitted.
3. **Given** the same current grants and policy, **When** either supported provider starts a turn, **Then** the same canonical tools and refusal causes are observed.
4. **Given** the separately packaged M1 resource, **When** the shared lifecycle changes are integrated, **Then** its context-to-tool-to-review-to-applied-result journey still succeeds through supported package consumers and removal withdraws its contributions.

### Edge Cases

- Authority or resource revision changes after message acceptance, after tool preparation, after an accepted effect, or between continuation and provider disclosure.
- An older bearer still contains earlier grants: qualification obtains a fresh normal token and independently confirms the owning resource's current denial.
- Repeated identifiers, malformed contributions, unavailable owners, mixed required/optional evidence and a contributor that mutates or resolves after its deadline.
- An explicit attachment names a resource outside the active surface; explicit provenance is preserved but does not bypass authorization or expand capture to unrelated resources.
- A current resource is unavailable: the request reports that fact instead of silently substituting another resource or old context.
- Budgets truncate evidence: the missing portion and authorized detail route remain explicit; truncation cannot turn missing required evidence into success.
- A tool receipt records a real prior effect while its details can no longer be disclosed; evidence of the effect is not converted into a new execution request.
- Standard host remount and custom retained editor lifetimes differ; both must preserve identity, module lifetime and outstanding target boundaries.

## Requirements

### Functional Requirements

- **FR-001**: Preserve explicit attachments, originating source, request correlation and applicable active context on each new request.
- **FR-002**: Present admitted context with module provenance, target/revision where applicable and partial, redacted or unavailable status. Present no rejected content as admitted evidence.
- **FR-003**: Refresh the applicable surface, resource, selection and working-state context for subsequent requests after navigation or lifecycle changes.
- **FR-004**: Preserve the original resource, revision and originating lifetime of outstanding operations; never silently retarget them.
- **FR-005**: Reevaluate current actor/tenant authority, module availability, contribution permissions/scopes and host policy before provider disclosure and tool advertisement/execution.
- **FR-006**: Apply current disclosure rules to retained context, historical resource evidence and applied results at continuation without erasing accepted receipt truth or repeating effects.
- **FR-007**: Treat installed-module guidance and untrusted resource content as distinct sources of trust. Client-supplied metadata and instruction-shaped resource text cannot grant trust, permissions or scope.
- **FR-008**: Minimize and redact provider-bound context and results; exclude forbidden secret values and unrelated resource evidence. Support bounded authorized module-owned detail reads.
- **FR-009**: Make optional contributor failures visible while preserving useful complete contributions; block provider use when required evidence is missing or exceeds an applicable bound.
- **FR-010**: Bound collection time and volume. Cancellation and deadline outcomes must terminate collection even when a contributor ignores its signal, and late completion cannot affect another request.
- **FR-011**: Give each dispatched correlation one explicit accepted, rejected, cancelled or failed disposition; prevent silent overwrite and unintended duplicate provider turns.
- **FR-012**: Produce equivalent admitted tool sets and refusal causes through both existing provider paths under equivalent authority and policy.
- **FR-013**: Keep domain context, guidance, tools, validation, risk, rendering and application semantics owned by participating modules. Shared orchestration and presentation must not interpret their domain payloads.
- **FR-014**: Preserve the proven separate-package journey and normal workflow validation, atomic working-state edits, whole-batch undo and explicit save/publish during shared-lifecycle changes.

### Key Entities

- **Request**: User intent with correlation, originating source, explicit attachments and active context at submission.
- **Admitted evidence**: Authorized, minimized context with owner, provenance, target/revision, trust category and any limitation.
- **Contribution outcome**: Complete or limited evidence, or a missing required/optional contribution with its reason.
- **Outstanding operation**: A prepared tool invocation bound to its original target, revision and contribution lifetime.
- **Accepted receipt**: A truthful record of an observed prior effect whose later disclosure is separately governed by current authority.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every qualified request preserves its explicit source and correlation; displayed admitted evidence matches the evidence disclosed to the assistant, including all tested exclusions and limitations.
- **SC-002**: In each tested navigation, selection, identity and module transition, fresh intent uses current eligible context and every older prepared operation remains bound to its original target or explicitly refuses.
- **SC-003**: Both supported provider paths withhold newly forbidden context and result details in acceptance-to-dispatch and effect-to-continuation scenarios; the independently observed prior effect occurs exactly once.
- **SC-004**: Every required/optional failure, noncompletion, cancellation, late completion and budget case reaches its specified disposition; missing required evidence never reaches provider use.
- **SC-005**: Both provider paths expose equivalent canonical tools and refusal causes for every qualified authority/policy scenario; secret canaries and unrelated records are absent from their actual inputs.
- **SC-006**: The separate-package witness retains its complete applied-result journey without resource-specific changes to Weaver core, Studio host or provider adapters; required exact-head checks, review and resulting-main verification are recorded on #3439 before completion.

## Assumptions

- M1/#3438 is verified Done on main `ab46bc22a52aa6e456bca6ac0e803c80904de081`; its accepted minimum is a dependency, not completion of M2–M5.
- The six acceptance items on #3439 remain authoritative. Durable session/approval recovery belongs to #3441; broad workflow/current-module journeys belong to M3/M4. These boundaries defer none of the requirements above.
- Existing authentication, owning-resource authorization and module composition remain authoritative. Qualification uses normal fresh authentication after grant changes, with independent owner observations.
- Time bounds are correctness/cancellation limits, not performance targets. Risk-based behavior tests and actual composed/package-consumer evidence are required.
- Deterministic tests, actual-adapter protocol qualification and live-provider evidence remain separate. This slice does not authorize live-provider purchases, credentials, releases, deployment or publisher activation.
- Demonstrated journeys determine the smallest required generic contracts; no universal resource schema or second orchestration engine is introduced.
