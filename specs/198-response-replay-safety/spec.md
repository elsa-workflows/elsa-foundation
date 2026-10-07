# Feature Specification: Response Replay Safety

**Feature Branch**: `claude/runtime-db-response-2400`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "Establish whether `WriteHttpResponse` can safely use the existing ReplaySafe classification, preserving its complete supported input contract and proving real publication plus ungraceful crash/replay behavior before any classification change. Keep Immediate as the default, HttpEndpoint External, existing pinned artifacts unchanged, and report measured claim/flush/dispatch/database-command deltas honestly, including zero gain or a no-change disposition."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish and run a response workflow safely (Priority: P1)

A workflow author publishes and runs the established synchronous HTTP scenario: `Sequence[HttpEndpoint, deterministic SetVariable producing “Alice Smith”, WriteHttpResponse]`. A valid REST-launched companion exercises the same response behavior through the normal workflow API. The published artifact and the committed response must agree with the author’s configured status, headers, content type, and body.

**Why this priority**: The candidate classification is part of a published contract. Source-only or hand-authored executable tests do not show that normal publication resolves, fingerprints, and pins the intended profile.

**Independent Test**: Publish the primary scenario through the real design/compiler/publication path on a normal durable host, execute it through synchronous HTTP, and compare its committed response instruction with the returned HTTP status, headers, content type, and body. Separately launch the REST companion through the supported API using the existing fixed `WorkflowRequest.content` shape, validate that API's admission response contract, and inspect the committed instruction and terminal result. Do not treat the REST admission envelope as the activity's HTTP response.

**Acceptance Scenarios**:

1. **Given** the primary workflow is designed and published through the supported publication path, **When** it runs with Immediate cadence, **Then** the synchronous HTTP status, headers, content type, body, committed response instruction, and terminal workflow output preserve existing behavior.
2. **Given** the candidate classification is accepted by the complete-contract and replay proof, **When** a newly published artifact is inspected, **Then** its resolved profile and changed fingerprint/hash reflect that classification.
3. **Given** a workflow is launched through the valid REST companion with the existing fixed `WorkflowRequest.content` shape, **When** it completes, **Then** the API admission response matches its supported contract and the committed instruction and terminal output contain the deterministic `Alice Smith` result; other accepted `WriteHttpResponse` binding families remain supported.
4. **Given** an artifact was published before any accepted classification change, **When** it is executed later, **Then** its pinned profile and hash retain their prior interpretation.

### User Story 2 - Recover the response after an ungraceful interruption (Priority: P1)

An operator must be able to restart a durable workflow host after process loss in the defined response-workflow window and recover the same committed response instruction and terminal output. A disconnected HTTP client is not promised a resurrected response or exactly-once transport delivery.

**Why this priority**: Replay safety is the condition that permits a response activity’s attempt claim to be treated as replayable. Graceful shutdown does not establish behavior after actual process loss.

**Independent Test**: With Coalesced cadence enabled for the proof case, establish the durable trigger/request boundary, execute through response computation and result staging, then terminate the process ungracefully before the containing segment flushes. Restart against the same durable state, allow replay, and compare the recovered committed response instruction and terminal output with an uninterrupted control. Exclude attempt and timestamp identities from value equivalence.

**Acceptance Scenarios**:

1. **Given** the durable request boundary exists and the response result has been staged in an unflushed Coalesced segment, **When** the process is terminated without graceful disposal and the host restarts, **Then** replay reaches a committed terminal state with the same response instruction values and terminal output as the control run.
2. **Given** the same workflow runs with Immediate cadence, **When** it completes without interruption, **Then** its existing claim, commit, response, and terminal behavior remains intact.
3. **Given** the original HTTP connection was interrupted by process loss, **When** recovery completes, **Then** the committed workflow result remains inspectable and durable, without an exactly-once or reconnect-delivery promise to that client.

### User Story 3 - Make a bounded, evidence-backed classification decision (Priority: P2)

The runtime team can decide whether the candidate is justified from evidence for the complete supported input contract and the selected normal-host workflow. If the safety proof fails, or if the measured checkpoint boundaries yield no supported reduction, the team can retain the current classification and record a no-change outcome without inventing a gain.

**Why this priority**: The objective is a scoped decision, not a requirement to ship an optimization regardless of evidence.

**Independent Test**: Review one bounded comparison between otherwise identical Coalesced runs of the same published workflow and provider: the existing External artifact and the newly published ReplaySafe candidate artifact, with cadence, fusion, host, and other settings held constant. Keep the Immediate run as a separate correctness/default control, not as the performance comparator. Review this comparison with the complete-contract, publication, and crash/replay evidence, distinguishing observations from source-based expectations and hypotheses.

**Acceptance Scenarios**:

1. **Given** the matched Coalesced External and ReplaySafe candidate runs complete, **When** their bounded diagnostics are reviewed, **Then** actual activity-claim, checkpoint-flush, dispatch, and database-command deltas are reported separately, including failed or repeated commands where observed, and a zero delta is reported as zero. The separate Immediate run establishes the default/correctness control only.
2. **Given** any supported binding, snapshot, secret-refusal, or replay condition does not meet the stated contract, **When** the decision is recorded, **Then** `WriteHttpResponse` remains External and the unmet condition is stated without weakening the input contract.
3. **Given** the safety proof passes but the bounded comparison establishes no reduction at the selected claim/flush/dispatch/command boundaries, **When** the decision is recorded, **Then** no performance gain is claimed and the candidate may be retired as no-change.

### Edge Cases

- A nonpositive response status continues to resolve to HTTP 200; omitted response fields retain their existing defaults.
- Header dictionaries and their string arrays are mutable before completion. The committed instruction and later delivery must represent the values captured for that completion and must not change when the original mutable inputs are changed afterward.
- The complete accepted binding families include literal/object/default values, workflow-request values, variable reads, causally available activity results, and canonical expressions that satisfy the existing pure-evaluation contract. A proof must not narrow these accepted forms.
- For request or activity-result values represented by stable external payload references, restart replay must resolve the same pinned value. Evidence must use the selected supported provider/reference; a hypothetical unsupported provider is not a contract counterexample.
- Existing secret-binding refusals remain in force for every response input shape. The classification must not admit a secret shape that publication currently rejects.
- HttpEndpoint remains External. Terminal, suspend, fault, cancellation, incident, bookmark, and other required checkpoint boundaries retain their existing flush behavior.
- If a proof attempt terminates before reaching the defined crash window or disposes the host gracefully, it does not count as crash/replay evidence.
- A database-command delta or timing result from a historical pre-pagination capture does not establish a current post-pagination reduction for this workflow.
- Replaceable HTTP content factories remain downstream of the committed response instruction. Their transport behavior is exercised through the normally composed host but does not become an activity input or an exactly-once replay guarantee.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST preserve the existing `WriteHttpResponse` input names, supported binding families, defaults, header behavior, and response value semantics.
- **FR-002**: A candidate ReplaySafe classification MUST be accepted only after evidence covers the complete supported input contract, including the existing pure-evaluator requirement and existing secret-binding refusals.
- **FR-003**: For a published response completion, the committed response instruction MUST retain the status, headers, content type, and body values captured for that completion, independent of later mutation to caller-owned header collections.
- **FR-004**: When a supported request or activity-result binding uses an external payload reference, replay MUST resolve the same pinned input value after restart.
- **FR-005**: The classification decision MUST be proven through real workflow publication. Evidence MUST identify the newly published artifact’s resolved profile and content identity; source declarations alone are insufficient.
- **FR-006**: Existing pinned artifacts MUST keep their published profile and identity. A new classification MUST NOT reinterpret an artifact that was already published.
- **FR-007**: HttpEndpoint MUST remain External, Immediate MUST remain the host default, and the feature MUST introduce no user-facing classification option or new durability policy.
- **FR-008**: The response workflow MUST recover from ungraceful process loss after its response is computed and staged but before its Coalesced segment flush, provided the durable trigger/request boundary already exists. Recovery MUST commit an equivalent response instruction and terminal output.
- **FR-009**: Recovery requirements MUST preserve every existing mandatory checkpoint boundary and its flush behavior; the candidate classification MUST NOT weaken terminal, suspension, failure, cancellation, incident, bookmark, or other required boundaries.
- **FR-010**: Acceptance evidence MUST include the primary synchronous HTTP sequence and a valid REST-launched companion using the existing fixed `WorkflowRequest.content` shape. The synchronous HTTP run MUST compare committed response values with the externally observed HTTP status, headers, content type, and body. The REST companion MUST validate its supported API admission response and inspect its committed instruction and terminal output; it MUST NOT assume that the admission envelope is the activity's HTTP response or restrict other accepted activity-binding families.
- **FR-011**: A response connection interrupted by process loss MUST NOT be represented as resurrectable, and acceptance MUST NOT promise exactly-once transport delivery.
- **FR-012**: A bounded classification comparison MUST hold the workflow, durable provider, Coalesced cadence, fusion configuration, host, and other settings constant while comparing the existing External artifact with the newly published ReplaySafe candidate artifact. It MUST report observed activity-claim, checkpoint-flush, dispatch, and database-command deltas separately, including failed or repeated commands where observed. The Immediate run MUST remain a separate correctness/default control and MUST NOT be used to attribute savings to the classification. A zero reduction MUST be recorded as zero; timing CI, global performance infrastructure, and unsupported historical extrapolation are out of scope.
- **FR-013**: If the complete-contract proof or ungraceful replay proof fails, the candidate MUST remain External and the specific failed condition MUST be documented. If proof passes but the bounded comparison supports no reduction, the outcome MUST permit a no-change disposition without a gain claim.
- **FR-014**: Replaceable response-content dependencies MUST remain post-commit delivery concerns and MUST NOT be included in the `WriteHttpResponse` activity input classification.

### Key Entities *(include if feature involves data)*

- **Published executable artifact**: The versioned, pinned workflow representation, including the resolved activity profile and identity used at execution time.
- **Invocation input snapshot**: The values supplied to one logical response activity invocation, including any role-owned request, variable, activity-result, or external-reference values.
- **Committed response instruction**: The durable status, headers, content type, and body produced by the workflow before transport delivery.
- **Checkpoint segment**: The Coalesced unit of durable progress whose replay boundary is under evaluation.
- **Classification evidence**: The publication, contract, recovery, and bounded boundary-count results used to accept or retire the candidate.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real publication run records the candidate’s resolved profile and artifact identity, and an older pinned artifact is verified to retain its prior profile and identity.
- **SC-002**: In the defined ungraceful process-loss window, recovery reaches a committed terminal state whose response instruction and terminal output are value-equivalent to the uninterrupted control, excluding attempt and timestamp identity.
- **SC-003**: Evidence covers every currently supported response binding family, preserves existing secret refusal, verifies pure evaluation, and verifies post-completion header snapshot behavior without dropping any accepted input shape.
- **SC-004**: The matched Coalesced External/candidate comparison reports separate actual counts for activity claims, checkpoint flushes, dispatches, and database commands under identical workflow, provider, fusion, and host settings; the separate Immediate correctness/default control is not used for attribution. Unobserved values and hypotheses are labeled as such.
- **SC-005**: The decision states either the accepted classification with all required proofs, or a no-change disposition that retains External and makes no unsupported gain claim. Immediate remains the default and HttpEndpoint remains External in either outcome.
- **SC-006**: An interrupted client connection is excluded from recovery guarantees; the durable committed workflow result remains the observable recovery product.

## Assumptions

- The selected primary workflow uses synchronous HttpEndpoint startup, a deterministic SetVariable result of `Alice Smith`, and WriteHttpResponse. The REST companion uses the supported API path and existing fixed `WorkflowRequest.content` shape to exercise the same committed result; its admission response is not the activity's HTTP response, and this companion does not narrow other accepted activity bindings.
- Coalesced cadence and ReplaySafe fusion are existing opt-in behavior; this feature does not change host defaults or introduce a new policy surface.
- The proof uses a normal supported host and durable persistence configuration. It reports the selected provider and makes no provider-wide claim beyond observed evidence.
- Current program accounting after the M2 pagination reduction is the relevant boundary. Historical pre-M2 request-command totals and global poll counts are not accepted as evidence of current savings.
- A safe classification may still yield no reduction in the selected workflow. That is a valid no-change result, not a reason to weaken replay or boundary guarantees.
- This specification does not pre-approve the production classification. The classification is conditional on all contract, publication, and ungraceful replay criteria passing.

## References

- [Runtime database access program plan](../../docs/plans/runtime-db-access-program.md)
- [ReplaySafe audit](../../docs/reports/runtime-db-access/replay-safety-audit.md)
- [ADR 0020: Runtime checkpoint commit and post-commit work](../../docs/adr/0020-runtime-checkpoint-commit-post-commit-work.md)
- [ADR 0031: Sticky single-writer drain and in-process fast path](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)
- [ADR 0032: Policy-driven per-workflow checkpoint cadence](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md)
- [ADR 0045: Role-owned bindings and immutable invocation records](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
- [ADR 0073: EF Core as the first-party persistence family](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
