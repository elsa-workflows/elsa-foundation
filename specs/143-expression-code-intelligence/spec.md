# Feature Specification: Expression Code Intelligence Foundation

**Feature Branch**: `143-expression-code-intelligence`
**Created**: 2026-07-28
**Status**: Implemented
**Input**: Provide a safe, language-neutral, design-time-only Foundation contract that lets Studio give JavaScript and Liquid expressions contextual code intelligence and semantic diagnostics, without evaluating user code or exposing runtime values.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Request a safe authoring context (Priority: P1)

A workflow author opens an expression property. Studio obtains a versioned authoring-context snapshot for that exact workflow draft, activity node, property, and expression type. The snapshot lists only the names, documentation, signatures, value shapes, and hierarchy that the caller may know; it never includes live runtime values or executes the expression.

**Why this priority**: Context is the common authority that makes completion, documentation, and validation correct without turning the editor into a runtime inspector.

**Independent Test**: Request a context for a JavaScript or Liquid binding with workflow inputs, visible variables, and activity-result references; verify deterministic, scoped metadata only, with inaccessible symbols omitted and the expected-result type preserved.

**Acceptance Scenarios**:

1. **Given** a valid draft location and an installed expression type, **When** an authorized designer requests its context, **Then** the response identifies the document, revision, expected result type, visible symbol hierarchy, and tooling capability version.
2. **Given** a symbol whose source is unauthorized, disabled by host policy, or outside the lexical scope, **When** context is requested, **Then** that symbol and its documentation are absent rather than redacted or represented by a live value.
3. **Given** a variable or result whose structure is known but whose value is runtime-only, **When** context is requested, **Then** only its type/value shape and design-time identity are returned.
4. **Given** a stale draft revision, cancellation, an unavailable provider, or an unknown capability version, **When** a client requests context, **Then** the response has an explicit, machine-readable state and no stale or partial success is mislabeled as current.

---

### User Story 2 - Obtain language-specific semantic assistance (Priority: P1)

A workflow author uses JavaScript or Liquid and receives completion candidates, hover documentation, and semantic diagnostics that match the expression type's actual language rules and the safe context snapshot. A host can enable either language independently.

**Why this priority**: Expression types own their language semantics; a shared generic global catalog would be wrong for JavaScript, Liquid, and future languages.

**Independent Test**: With the same authoring context, request JavaScript and Liquid tooling. Verify each provider returns its own syntax and symbol projection, filters unauthorized symbols, and reports an unsupported/incompatible type explicitly without executing code.

**Acceptance Scenarios**:

1. **Given** JavaScript is enabled, **When** a tooling request includes a compatible JavaScript document and context revision, **Then** the provider returns language-appropriate completions and diagnostics for safe symbols.
2. **Given** Liquid is enabled, **When** a tooling request includes a compatible Liquid document and context revision, **Then** the provider returns Liquid tags, filters, variables, and properties according to Liquid rules.
3. **Given** a language provider is absent, disabled, or incompatible with the requested contract version, **When** a client asks for tooling, **Then** it receives `unavailable` or `incompatible`, never a fabricated generic result.
4. **Given** a language provider handles user source, **When** it supplies help or diagnostics, **Then** it must not evaluate the source, resolve live values, call external services, or mutate workflow design state.

---

### User Story 3 - Gate consequential operations on full-draft validation (Priority: P2)

A workflow author can save a draft while editing incomplete code, but a test run and publication perform a full, current-draft semantic validation. Known-invalid expressions block the consequential action; a temporary validation service outage produces a visible test-run warning and a fail-closed publication result.

**Why this priority**: Typing must remain fluid while test runs and publication cannot proceed from an expression known to be invalid.

**Independent Test**: Start from a draft with one invalid JavaScript/Liquid expression. Verify draft save succeeds, test run and publication invoke the same full-draft gate, and outcomes differ correctly for invalid, unavailable, and canceled validation.

**Acceptance Scenarios**:

1. **Given** an edited draft with a syntax or semantic error, **When** it is saved, **Then** the source is retained and diagnostics can be read without blocking the edit.
2. **Given** a test-run request and a current validation result containing errors, **When** the request proceeds, **Then** the run is rejected with the expression diagnostics attached to the relevant authored locations.
3. **Given** publication and an unavailable, canceled, or incompatible expression validator, **When** publication is attempted, **Then** publication fails closed with an actionable validation-state diagnostic.
4. **Given** test-run validation is unavailable but no known-invalid expression exists, **When** the caller explicitly confirms the warning, **Then** the test run may proceed; the confirmation and validation state are recorded in its design-time request metadata.

---

### User Story 4 - Integrate through discoverable, permissioned APIs (Priority: P2)

A Studio or other authorized client discovers the additive expression-tooling capability from the host and follows canonical links to request a context or semantic validation. A client on an older shell continues to use ordinary expression editing.

**Why this priority**: Studio must not infer server composition or rely on private module names, and partial deployments must degrade safely.

**Independent Test**: Exercise capability discovery and each endpoint for authorized, unauthenticated, unauthorized, unavailable, and version-incompatible callers; verify no source or symbol metadata is disclosed outside the authorized request.

**Acceptance Scenarios**:

1. **Given** a host with expression tooling composed, **When** it advertises capabilities, **Then** it exposes versioned, canonical links for context and validation without claiming permission on behalf of a caller.
2. **Given** an unauthenticated or unauthorized caller, **When** it follows a tooling endpoint, **Then** the endpoint denies access before resolving a context, invoking a provider, or emitting diagnostics.
3. **Given** a host without the feature, **When** a compatible client reads capabilities, **Then** the links are absent and the client can continue basic authoring.
4. **Given** a tooling request is canceled or superseded, **When** the server observes cancellation, **Then** it propagates the cancellation to all context and language-provider work and returns no cacheable partial response.

### Edge Cases

- An empty expression is valid editor content but may produce a type-specific diagnostic at test-run/publication time.
- Two identical property names on different activity nodes must not share a context identity or cached result.
- A symbol catalog may be supported and empty; that state is distinct from unavailable, unauthorized, and incompatible.
- A request may arrive after the draft has changed; diagnostics must identify the evaluated document revision so clients can discard stale results.
- Provider documentation may contain markup supplied by a module; the API exposes only a sanitized documentation subset.
- Validation faults must be isolated into explicit validation-state diagnostics; they must not make drafts unreadable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define an `ExpressionAuthoringContext` in `Elsa.Workflows.Design` that is language-neutral, revisioned, location-scoped, and contains design-time facts only.
- **FR-002**: An authoring context MUST identify workflow draft, node, property/input, expression type, expected result type, document revision, and caller-visible symbol catalog revision.
- **FR-003**: The system MUST define stable language-neutral symbol, callable-signature, value-shape, documentation, and hierarchical-member contract types in `Elsa.Expressions.Core`.
- **FR-004**: The system MUST define an `IExpressionToolingProvider` contract in `Elsa.Expressions.Core`, resolved strictly by expression type and declared tooling-contract version.
- **FR-005**: A tooling provider MUST support cancellable requests for context projection and semantic validation; it MUST NOT evaluate source, resolve runtime values, or mutate workflow state.
- **FR-006**: JavaScript and Liquid MUST each contribute their own provider and language descriptor; no `Elsa`-wide JavaScript/Liquid globals contract may be introduced.
- **FR-007**: The authoring-context builder in `Elsa.Workflows.Design` MUST derive visible symbols from the current design graph, lexical scope, activity contracts, expected type, caller permissions, and active host policy.
- **FR-008**: The builder MUST omit inaccessible symbols before the provider receives context, and providers MUST treat the supplied context as the complete authority boundary.
- **FR-009**: Context and tooling responses MUST explicitly distinguish `supported-empty`, `unavailable`, `unauthorized`, `incompatible`, `stale`, and `canceled` outcomes.
- **FR-010**: The context and semantic-validation contracts MUST carry a contract version and document/context revisions; servers and clients MUST reject unknown mandatory versions explicitly.
- **FR-011**: `Elsa.Workflows.Design.Api` MUST advertise discoverable, permissioned capability links for its additive expression-tooling endpoints only when both the Design context service and at least one expression-type tooling provider are composed, without changing the existing expression descriptor list contract.
- **FR-012**: `Elsa.Workflows.Design.Api` MUST add permissioned endpoints to resolve a location-scoped authoring context and validate a submitted/current-draft expression document.
- **FR-013**: Endpoints MUST apply authorization before loading a draft, projecting symbols, or forwarding source to a provider, and responses MUST be `no-store`.
- **FR-014**: The request/response contracts MUST be bounded: catalogs are searchable and paged after authorization/policy filtering, value-shape members are inlined to a maximum depth of four, and a response cannot require delivery of all workflow symbols. Lazy member retrieval is explicitly unsupported in v1.
- **FR-015**: The design validation lifecycle MUST integrate a full-draft expression semantic validator through the existing draft-validation gate while preserving shielded read behavior for draft diagnostics.
- **FR-016**: A local/ad-hoc validation request MUST report diagnostics against only its supplied document and must not persist or alter draft state.
- **FR-017**: Test-run entry points MUST perform full-current-draft expression validation before compilation/dispatch and reject known-invalid expressions.
- **FR-018**: Test-run entry points MAY proceed only after an explicit caller confirmation when validation is unavailable; they MUST surface and record the warning state.
- **FR-019**: Publication and promotion paths MUST perform full-current-draft expression validation and fail closed when validation returns errors, unavailable, canceled, or incompatible.
- **FR-020**: Diagnostics MUST have stable code, severity, source range/path, document revision, and sanitized message/documentation fields; source text, source prefixes, values, and symbol names MUST NOT enter telemetry.
- **FR-021**: Existing expression descriptor APIs and editing behavior MUST remain compatible for clients that do not discover the new capability.
- **FR-022**: The implementation MUST add conformance tests for provider routing, cancellation, outcome states, authorization/host-policy filtering, revision staleness, JavaScript/Liquid behavior, and full-draft operation gates.

### Key Entities

- **Expression Authoring Context**: Permission-filtered, language-neutral design-time facts for one expression document location and draft revision.
- **Expression Tooling Provider**: Per-expression-type contributor that projects language semantics and validates source within a supplied context.
- **Expression Symbol**: A caller-visible name or member with kind, documentation, signature/value shape, hierarchy, and stable identity.
- **Tooling Outcome**: Versioned response state separating supported-empty, unavailable, unauthorized, incompatible, stale, and canceled.
- **Expression Diagnostic**: Stable, revision-bound feedback used by ad-hoc requests and the full-draft validation gate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of tooling-context and semantic-validation tests prove that no evaluated runtime value, runtime service, or expression execution is reachable from the request path.
- **SC-002**: 100% of authorization and host-policy test cases prove that an inaccessible symbol is absent from both context and language-provider input.
- **SC-003**: A warm request for a bounded context or document validation completes within 250 ms at the 95th percentile for a draft with 500 visible symbols in the conformance harness.
- **SC-004**: 100% of stale-revision tests prove a response identifies the evaluated revision so a client can reject superseded output.
- **SC-005**: 100% of JavaScript and Liquid provider conformance cases return language-specific results or an explicit non-success outcome; none silently fall back to generic text semantics.
- **SC-006**: 100% of full-draft gate tests prove invalid expressions block test run and publication, while an unavailable validator warns-and-confirms for test run and fails closed for publication.
- **SC-007**: Existing descriptor-list and ordinary draft-editing tests remain green for hosts and clients that do not compose expression tooling.

## Assumptions

- Existing workflow-design read/manage permissions are the baseline authorization surface; a feature may introduce narrower tooling permissions only if they preserve the same no-disclosure invariant.
- JavaScript and Liquid are the first providers. Other expression types can opt in only by implementing the same provider contract.
- Studio owns editor rendering, local syntax highlighting, and client cache/session mechanics; Foundation owns design-time facts, policy, language semantic authority, and consequential-operation gates.
- The current draft-validation event/gate is the integration seam; no new persisted diagnostic store is introduced in this feature.
- This is a Foundation work unit and coordinates with Studio spec `094-expression-code-intelligence`; neither repository gains a direct source dependency on the other.

## Out of Scope

- Live runtime value inspection, executing expressions for completion/validation, debug evaluation, or side-effectful script tooling.
- A general language-server transport, a new persistence store, or a full TypeScript compiler service.
- Studio editor UX, CodeMirror/Monaco integration, client-side formatting policy, and editor telemetry implementation.
