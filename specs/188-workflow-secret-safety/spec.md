# Feature Specification: Workflow secret safety (Connections and Secrets, phase 0)

**Feature Branch**: `188-workflow-secret-safety`

**Created**: 2026-09-30

**Status**: Draft — specification task [#2211](https://github.com/elsa-workflows/elsa-foundation/issues/2211); clarified 2026-09-30; plan and tasks drafted 2026-10-01; implementation has not started.

**Input**: Phase 0 of the [Connections and Secrets model](../../docs/plans/connections-and-secrets-model.md), decisions D1 and D10. Make secrets usable from workflows, and stop secret material from entering workflow definitions and persisted or emitted runtime output. No new concepts: Connections, authentication schemes, OAuth, and external secret stores are later phases.

## Why this exists

The Secrets module stores, versions, rotates, and audits secrets, and Studio lets an author pick one for an activity input. But nothing turns the picked reference into a value when the workflow runs. Verified on `main` (057adc44f):

- Outside `src/essentials/Secrets`, nothing references `ISecretValueResolver`, `SecretReference`, or the `Secret` expression type. A `Secret` binding compiles as a generic expression and would fail at run time for lack of a handler.
- The only way to feed a credential to an activity today is to type it as a literal. It is then stored in the definition, the git export, and every version.
- A sensitive input's resolved value is persisted in plain text in the activity execution state's input snapshot. No redaction applies on that path.
- Sensitivity can only be marked per binding by the author (`ArgumentState.IsSensitive`). An activity cannot declare "this input holds a credential."
- `RequiresEncryption` is carried and combined, but nothing encrypts, withholds, or refuses on its account, except inline variable replacement.
- Draft save records validation errors without blocking. Publish does not run draft validators. Three import paths (add-version, submit, file reconciliation) and git export run no validation.

## Clarifications

### Session 2026-09-30

- Q: At draft save, block or record the secret-literal violation? → A: Block. The literal is never stored, as an explicit exception to the non-blocking draft convention.
- Q: Which inputs refuse literals? → A: Only activity-declared credential inputs. Non-credential sensitive inputs may hold literals and are masked.
- Q: What does enforcing `RequiresEncryption` mean for values not derived from a secret reference? → A: Withhold. Persist a marker; no encryption at rest in phase 0.
- Q (raised in plan review 2026-10-01): How does an expression-bound value that requires encryption reach the activity? → A: It cannot in phase 0 without persisting it, so such bindings are refused at publish; secret references are the only binding for encryption-required inputs. Phase 1's redacting value type revisits this.
- Q (raised in plan review 2026-10-01): Which activity inputs can take a secret reference? → A: Inputs whose value the activity reads only when it runs. Publish refuses a secret reference with rule `VF-ACT-012` on intrinsic nodes, graph activities and checkpoint participants, and on inputs the activity marks as copied into its own persisted state or read at publish time (the built-ins that do either are marked, for example a ForEach collection and trigger routing options).
- Q (raised in plan review 2026-10-01): Can an input that refuses secret references be declared a credential? → A: No. A credential input would accept only a secret reference, so such an input could never be bound; the declaration is refused when the activity is cataloged.
- Q (raised in plan review 2026-10-01): When is a secret refused at publish because the input type cannot hold it? → A: Only when that is certain: a structured-text secret (`rsa-key`, `x509-certificate`) on a numeric, boolean, date/time or enum input, or a collection of those, and only in a host that composes the Secrets bridge, which knows the secret types. Every other mismatch faults at run time with `TypeMismatch`.
- Q (raised in plan review 2026-10-01): Can the credential rule judge a node whose activity is not in the catalog? → A: No. The rule applies where the activity's input contract is known; such a node is judged at publish, where it is refused, and until then its literal can be stored.
- Q (raised in plan review 2026-10-01): What does file reconciliation or git export do with a definition the rule refuses? → A: It refuses that item only, logs a warning that names the rule, definition, version, node and input but no value, and continues the pass without blocking host readiness.
- Q (raised in plan review 2026-10-01): What happens when the instance's tenant differs from the execution scope? → A: Resolution refuses with a `TenantMismatch` fault before any secret is read.
- Q (raised in plan review 2026-10-01): What happens in a host that cannot resolve secrets at all? → A: A host that does not compose the secret resolution bridge has a composition fault, not a resolution failure: the activity parks with an activation-failure incident (constitution §E2.6.1).

## User Scenarios & Testing

### User Story 1 - Use a stored secret in a workflow (Priority: P1)

A workflow author has an API key stored as a secret. In Studio they bind an activity input to that secret with the existing picker, publish, and run. The activity receives the key's current value. The definition holds only the reference.

**Why this priority**: Without this, the Secrets module has no consumer, and authors paste credentials as literals.

**Independent Test**: Create a secret, bind it to an input of a test activity that records whether it received the expected value, publish, run, and assert the activity saw the value. Rotate the secret and run again: the new value is used without republishing.

**Acceptance Scenarios**:

1. **Given** an active secret and an input bound to it, **When** the workflow runs, **Then** the activity receives the secret's current value, converted to the input's declared type.
2. **Given** the secret is rotated after publish, **When** the workflow runs again, **Then** the new value is used, with no republish.
3. **Given** a workflow that suspends after an activity consumed a secret and resumes later, **When** a later activity consumes the same reference, **Then** the value is resolved again at that point, not restored from persisted state.
4. **Given** two tenants each with a secret of the same name, **When** each tenant's instance runs, **Then** each resolves its own tenant's secret and never the other's.

### User Story 2 - A failing secret fails the run clearly and safely (Priority: P1)

When a referenced secret is missing, revoked, expired, the wrong type, or its store is unreachable, the activity faults with a message naming the reference and the reason, and never any value.

**Why this priority**: Secret lifecycle operations (revoke, expire, delete) must have visible, predictable effects on the workflows that use them.

**Independent Test**: For each resolution failure code, run a workflow bound to a secret in that state and assert the fault names the reference and the code, contains no value, and is marked retryable only for transient causes.

**Acceptance Scenarios**:

1. **Given** a revoked, expired, deleted, or missing secret, **When** the activity runs, **Then** it faults with the reference name and a safe reason code, and the fault is not retryable.
2. **Given** the secret's store is temporarily unavailable, **When** the activity runs, **Then** it faults as a transient failure that existing retry policies may retry.
3. **Given** the reference names a type that does not match the stored secret, **When** the activity runs, **Then** it faults with a type-mismatch reason.

### User Story 3 - Credentials cannot be typed into a workflow (Priority: P1)

An activity author marks an input as holding a credential. A workflow author who binds a literal to that input is told to use a secret instead, and a definition carrying such a literal cannot be saved, published, imported, or exported.

**Why this priority**: Resolving secrets (Story 1) is only half the fix. The paste-a-literal path stays open until it is refused.

**Independent Test**: Take a definition with a literal bound to a credential input and push it through every entry point (draft save, promote, publish, add-version, submit, file reconciliation, git export). Each refuses with the same rule identifier naming the activity and input. The same definition using a secret reference passes everywhere.

**Acceptance Scenarios**:

1. **Given** an activity input declared as a credential, **When** a literal or expression is bound to it, **Then** the definition is refused at every entry point with one stable rule identifier, the activity id, and the input name, and the literal is not stored.
2. **Given** the same input bound to a secret reference, **When** saved and published, **Then** it is accepted.
3. **Given** an activity input declared as a credential, **When** Studio renders it, **Then** the secret picker is offered by default and a literal cannot be entered.
4. **Given** an author marks a binding as sensitive where the activity did not, **When** compiled, **Then** the stricter policy applies; an author can never weaken an activity's declared policy.

### User Story 4 - Secret values never leak into stored or emitted output (Priority: P1)

An operator inspecting a run, reading diagnostics, collecting telemetry, or reading persisted state never sees a secret value, including when the run faults.

**Why this priority**: This is the guarantee. Without a test that enforces it, later changes will break it silently.

**Independent Test**: A canary end-to-end test (see FR-013) plants a unique secret value, exercises success, fault, suspend/resume, and rotation, then scans every surface. Deliberately removing any single protection must turn the test red.

**Acceptance Scenarios**:

1. **Given** a completed run that consumed a secret, **When** persisted instance and activity state are read, **Then** they contain the reference and a withheld marker, never the value.
2. **Given** an activity that faults with an exception message containing the resolved value, **When** the fault is persisted and reported, **Then** the value is masked.
3. **Given** execution evidence, the run inspector, diagnostic snapshots, and runtime telemetry for such runs, **When** read, **Then** none contain the value.

### User Story 5 - Studio shows sensitivity from the activity (Priority: P2)

Studio knows which inputs an activity declares as credentials or sensitive, so it defaults to the secret picker and masks any sensitive literal it does allow.

**Why this priority**: The backend refusal (Story 3) is the guarantee. This makes the right path the easy one.

**Independent Test**: Studio unit tests: an input declared as a credential resolves to the secret reference editor with literal entry unavailable; an input with a `password` UI hint that permits literals resolves to a masked editor ahead of the single-line editor.

**Acceptance Scenarios**:

1. **Given** a backend input descriptor carrying sensitivity, **When** Studio loads the activity, **Then** the SDK descriptor type exposes it to editors.
2. **Given** an input with UI hint `password`, **When** rendered, **Then** the value is masked while typing and never prefilled on reload.

### Edge Cases

- A secret reference bound to an input whose type cannot represent the secret (for example an `rsa-key` into an integer input): refuse at publish when the declared types make it certain, otherwise fault at run time with a type-mismatch reason. Certain means a structured-text secret on a numeric, boolean, date/time or enum input, or a collection of those; string, object, any-typed and JSON inputs can hold text and are accepted. The publish-time refusal needs the secret's type domain, so it applies only in hosts that compose the Secrets bridge; elsewhere the domain is unknown and the run-time fault applies.
- A secret reference on an input that cannot resolve at the point of use: an intrinsic such as Set Variable, a graph activity, a checkpoint participant, an input the activity marks as copied into its own persisted state (for example a ForEach collection), or an input it marks as read at publish (for example an HTTP endpoint path or an event name). Refused at publish with `VF-ACT-012`. Such an input cannot be declared a credential.
- A secret referenced by name only, with no type or scope constraint: resolves by name within the tenant, as `ISecretValueResolver` already does.
- A secret deleted between publish and run: faults with `Deleted` or `NotFound`, never with a stale cached value.
- An activity reads a resolved secret, then the workflow suspends for days: nothing persisted during suspension contains the value; resumption re-resolves.
- A credential input bound to an expression (for example JavaScript that builds a token from a variable): refused like a literal (FR-009). A non-credential sensitive input bound to an expression: allowed when its effective policy does not require encryption; the result follows the effective sensitive policy. A literal or expression on an input whose effective policy requires encryption is refused at publish (FR-010).
- Definitions created before this rule that already contain literals on credential inputs: Elsa 4 is unreleased, so no migration; they are refused on their next save or publish.
- A literal empty string or null on a credential input: treated as "unbound," not as a literal credential, and does not trip the rule.
- A literal on a credential input of an activity the catalog does not know: draft save, add-version, submit, file reconciliation and git export cannot tell that the input is a credential, so they accept it; publish refuses it (FR-008).
- File reconciliation or git export meets a definition version the rule refuses: that item alone is refused, with a value-free warning naming the rule, definition, version, node and input; the pass continues and host readiness is not blocked.
- The same secret consumed by many activities in one run: each consumption resolves at its point of use; phase 0 does not require caching.

## Requirements

### Functional Requirements

**Resolution**

- **FR-001**: A `Secret` binding on an activity input MUST resolve at the activity's point of use through the existing secret resolver, for the tenant of the executing workflow instance. Publish MUST refuse a `Secret` binding, with rule `VF-ACT-012`, on intrinsic nodes, graph activities, checkpoint participants, and inputs the activity marks as copied into its own persisted state or read at publish time.
- **FR-002**: Resolution MUST happen on every execution and every resumption. A resolved value MUST NOT be restored from persisted state.
- **FR-003**: Resolution failures MUST fault the activity with the reference name and the resolver's failure code, and no value or store-private detail. `StoreUnavailable` MUST be classified transient. All other codes MUST be classified permanent. A host that does not compose the secret resolution bridge cannot resolve at all; this is a composition fault, not a resolution failure, and MUST park the activity with an activation-failure incident (constitution §E2.6.1).
- **FR-004**: The resolved value MUST be converted to the input's declared type using the existing input conversion rules. An impossible conversion MUST fault with a type-mismatch reason.
- **FR-005**: The tenant of the executing instance MUST be available wherever activity inputs are resolved. A binding MUST NOT be able to select a tenant. If the instance's tenant is known and differs from the execution scope, resolution MUST refuse with a `TenantMismatch` fault before any secret is read.

**Declaring and guarding sensitive inputs**

- **FR-006**: Activity authors MUST be able to declare, on an input, that it (a) is sensitive and (b) is a credential that accepts only a secret reference. The declaration MUST reach the activity's input descriptor so Studio and validators can read it. An input that cannot take a secret reference (FR-001) MUST NOT be declarable as a credential: the declaration is refused when the activity is cataloged.
- **FR-007**: The effective policy of a binding MUST be the stricter of the activity's declaration and the author's per-binding choice. An author MUST NOT be able to downgrade a declared policy; a downgrade attempt MUST be refused, consistent with the existing value-policy downgrade rule.
- **FR-008**: A definition that binds a literal to a credential input MUST be refused, with one stable rule identifier, the activity id, and the input name, at: draft save, promote, publish, add-version, submit, file-based reconciliation import, and git export. The rule applies where the activity's input contract is known: a node whose activity is not in the catalog cannot be judged before publish, and publish MUST refuse it. During file-based reconciliation and git export the refusal MUST apply to the offending item only, with a value-free warning, and the pass MUST continue without blocking host readiness. At draft save the refusal MUST block: a literal the rule can judge is never stored, not even in a draft. This rule is a deliberate exception to the convention that draft save records validation errors without blocking, because the harm (credential material at rest) happens at save.
- **FR-009**: The rule MUST apply only to inputs the activity declares as credentials (FR-006b). Sensitive inputs that are not credentials (for example personal data) MAY hold literals; their values are masked in Studio and follow the effective sensitive policy at run time. Expressions on credential inputs are refused like literals; only a secret reference or no binding is accepted.
- **FR-010**: Values whose effective policy requires encryption MUST NOT be written to persisted state in plain text. In phase 0, an activity input whose effective policy requires encryption MUST be bound to a secret reference or left unbound; a literal or expression binding on such an input MUST be refused at publish with a stable rule identifier, because no phase 0 path can deliver that value to the activity without persisting it. Any other value whose effective policy requires encryption MUST be withheld: persisted state records a withheld marker instead of the value, and the value is not recoverable. Secret-bound inputs are re-resolved on resumption (FR-002). Phase 0 adds no encryption at rest to the workflow runtime.

**No leakage**

- **FR-011**: Persisted instance state, activity execution state (including input snapshots), execution evidence, and the run inspector MUST record a secret-bound input as its reference plus a withheld marker, never the value.
- **FR-012**: Values resolved from secrets during an activity execution MUST be masked in any fault, incident, or runtime-emitted log or telemetry text produced for that execution, even when the text comes from activity code (for example an exception message).
- **FR-013**: An end-to-end canary test MUST plant a unique secret value, then exercise a successful run, a faulting run whose exception includes the value, a suspend-and-resume run, and a rotation. It MUST then assert that the value is absent from: stored definitions and versions, git export output, persisted instance and activity state, execution evidence, inspector responses, diagnostic snapshots, runtime telemetry export, and runtime-captured logs.
- **FR-014**: The canary test MUST be bite-proofed. For each protection in FR-011 and FR-012, disabling that protection alone MUST turn the test red. The bite-proof results MUST be recorded in the pull request.

**Studio**

- **FR-015**: The Studio SDK activity input descriptor type MUST expose the activity-declared sensitivity and credential flags (FR-006), in the canonical SDK and every hand-maintained SDK typing copy that describes input descriptors.
- **FR-016**: For a credential input, Studio MUST offer the secret picker by default and MUST NOT offer literal entry.
- **FR-017**: Studio MUST provide a masked property editor for inputs with UI hint `password`, selected ahead of the single-line editor, never prefilling a stored value.

**Housekeeping**

- **FR-018**: Spec 079's runtime contract, research, plan, and tasks MUST be corrected from `ISecretResolver` to `ISecretValueResolver` and from `src/Elsa/Secrets` to `src/essentials/Secrets`, matching the code.

### Key Entities

- **Secret reference**: Existing. Name plus optional type and scope. The only secret-related thing a definition may contain.
- **Input sensitivity declaration**: New. An activity author's statement that an input is sensitive, and optionally that it is a credential accepting only secret references.
- **Effective value policy**: Existing. Now the stricter of declaration and authored choice, and now enforced for encryption.
- **Withheld marker**: New. What persisted and inspected state records in place of a secret-derived value: the reference, and the fact that a value was withheld.
- **Secret-literal rule**: New. The validation rule, with one stable identifier, refusing literals on guarded inputs at every entry point.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A workflow author can bind a stored secret to an activity input and run the workflow successfully with no literal credential anywhere in the definition.
- **SC-002**: Rotating a secret takes effect on the next activity execution in 100% of tested runs, with zero republishes.
- **SC-003**: A literal or expression on a guarded input of a cataloged activity is refused at 7 of 7 definition entry points (FR-008), with the same rule identifier each time; on an activity the catalog does not know, it is refused at publish.
- **SC-004**: The canary value appears 0 times across the 8 surfaces in FR-013, over the 4 run shapes.
- **SC-005**: Each protection named in FR-014, when disabled alone, causes the canary test to fail. Zero protections are unguarded.
- **SC-006**: Every resolution failure code produces a fault that names the reference and code and contains zero secret material.

## Assumptions

- Elsa 4 is unreleased. No migration or compatibility path is provided for existing definitions carrying literals on newly guarded inputs.
- Single-tenant hosts use the tenant identifier the Secrets module already uses for them. Phase 0 introduces no new tenant concept.
- Phase 0 does not add caching of resolved values. Each point of use resolves; caching belongs with external stores (phase 3).
- Masking in FR-012 covers text the runtime persists or emits. Text an activity writes directly to its own logger, outside the runtime's capture, is not covered in phase 0; phase 1's redacting value type addresses it.
- The existing Studio secret picker and its opaque reference format are reused unchanged.
- Connections, authentication schemes, OAuth, and external secret stores are out of scope (later phases of the design doc).
