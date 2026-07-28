# Feature Specification: Workflow Version Override

**Feature Branch**: `codex/workflow-version-override`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "Allow an authorized workflow author to request an exact forward semantic version when promoting a draft, while preserving automatic next-major promotion, authoritative uniqueness and monotonicity checks, idempotency, and explicit capability discovery for compatible Studio clients."

**Program Goal**: `none/free-flow`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Promote with an exact forward version (Priority: P1)

As an authorized workflow author, I can request an exact semantic version when promoting a draft so that a release can carry the version required by my delivery process.

**Why this priority**: Exact-version publication is the user-visible capability this work enables.

**Independent Test**: Promote drafts with valid forward releases and prereleases and verify the immutable versions carry the requested labels.

**Acceptance Scenarios**:

1. **Given** the latest version is `2.0.0`, **When** an author promotes a valid draft as `2.1.0`, **Then** the resulting immutable version is labeled `2.1.0`.
2. **Given** the latest version is `2.0.0`, **When** an author promotes a valid draft as `2.1.0-rc.1`, **Then** the forward prerelease is accepted.
3. **Given** no exact version is requested, **When** a draft is promoted, **Then** the existing automatic next-major policy remains unchanged.

---

### User Story 2 - Reject unsafe or ambiguous version requests (Priority: P1)

As a workflow author, I receive an authoritative explanation before an unsafe exact version can create an immutable record.

**Why this priority**: Immutable version identity must remain unique and monotonically forward within a workflow definition.

**Independent Test**: Attempt malformed, equal, lower, duplicate, and build-metadata-equivalent requests and verify that no version is created and each outcome is classified correctly.

**Acceptance Scenarios**:

1. **Given** a malformed version label, **When** the author requests a promotion preflight, **Then** the response explains that the label is invalid and no version is created or reserved.
2. **Given** the requested version is equal to or lower than the latest version by semantic precedence, **When** the author requests a promotion preflight, **Then** it is reported as not ready and no version is created or reserved.
3. **Given** the requested version collides with an existing semantic identity, including a build-metadata-only variant, **When** the author requests a promotion preflight, **Then** it is reported as a conflict and no version is created or reserved.
4. **Given** the latest version changes after a ready preflight, **When** promotion is requested, **Then** the server revalidates the selection atomically and refuses an outdated exact version.
5. **Given** two requests race for the same exact version, **When** both reach persistence, **Then** at most one immutable version is committed.

---

### User Story 3 - Discover support and replay safely (Priority: P2)

As a management client, I can discover exact-version support and safely repeat an uncertain promotion request without changing its meaning.

**Why this priority**: Older clients and hosts must continue to interoperate, and network retries must not mint or relabel versions.

**Independent Test**: Inspect capability discovery and replay promotion with matching and mismatching idempotency material.

**Acceptance Scenarios**:

1. **Given** a host implements exact-version promotion, **When** its workflow-design capabilities are queried, **Then** a stable relation advertises the supported promotion operation.
2. **Given** an older host lacks the relation, **When** a compatible client prepares publication, **Then** it continues with automatic promotion and does not offer an unusable override.
3. **Given** a successful exact-version promotion whose response is lost, **When** the same operation key and version request are repeated, **Then** the committed version is returned without creating another version.
4. **Given** an operation key was committed with one requested version, **When** it is replayed with a different version, **Then** the replay is rejected rather than returning a misleading result.

### Edge Cases

- The requested version contains surrounding whitespace, leading zeroes, prerelease identifiers, or build metadata.
- The workflow has no prior versions.
- The latest version is a prerelease and the requested stable version has the same numeric core.
- A build-metadata-only variant compares equal to an existing version.
- A draft becomes invalid between review and promotion.
- The latest version changes between client review and the definition-locked promotion attempt.
- A duplicate is detected by the persistence uniqueness constraint after an earlier existence check.
- An automatic promotion request is replayed after an exact-version request with the same operation key, or vice versa.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Draft promotion MUST accept an optional requested semantic-version label.
- **FR-002**: Omitting the requested label MUST preserve the existing automatic next-major assignment behavior.
- **FR-003**: An explicit label MUST conform to semantic-version syntax after the contract's defined whitespace normalization.
- **FR-004**: An explicit label MUST have greater semantic precedence than the latest immutable version for the same workflow definition.
- **FR-005**: Forward prerelease labels MUST be accepted when their semantic precedence is greater than the latest version.
- **FR-006**: Equal, lower, duplicate, malformed, or build-metadata-equivalent labels MUST create no version.
- **FR-007**: Malformed and non-forward labels MUST produce an invalid-request outcome; an existing or racing duplicate identity MUST produce a conflict outcome.
- **FR-008**: Validation of the selected version MUST occur within the existing definition-level concurrency boundary used by promotion.
- **FR-009**: Persistence MUST continue to enforce unique semantic identity independently of request-time checks.
- **FR-010**: The requested version MUST be part of the promotion operation's idempotency material.
- **FR-011**: Repeating the same operation key with identical normalized request material MUST return the original committed version.
- **FR-012**: Reusing an operation key with a different requested version or assignment mode MUST be rejected.
- **FR-013**: The promoted immutable version MUST store the exact accepted label while using normalized semantic precedence for ordering and uniqueness.
- **FR-014**: Existing draft validation MUST remain a promotion gate for automatic and exact-version requests.
- **FR-015**: Existing promotion authorization MUST govern exact-version requests; no new anonymous or weaker path may be introduced.
- **FR-016**: Workflow-design API capability discovery MUST advertise exact-version promotion through a stable additive relation.
- **FR-017**: Hosts that do not advertise the relation MUST remain compatible with automatic-promotion clients.
- **FR-018**: The management API contract MUST document the optional version request and invalid-request and conflict outcomes.
- **FR-019**: Workflow Design MUST expose a non-mutating promotion preflight that returns the authoritative automatic or exact candidate, latest-version baseline, readiness, and actionable issues without reserving a version.
- **FR-020**: Promotion preflight and exact-version promotion MUST be independently discoverable through stable additive workflow-design capability relations.
- **FR-021**: Promotion MUST repeat every version acceptance check inside the existing definition-level concurrency boundary even when a preceding preflight reported ready.

### Key Entities

- **Promotion Request**: The draft identity, operation key, and optional requested semantic version whose normalized values define one replay-safe operation.
- **Promotion Preflight Assessment**: A non-persisted, advisory view of assignment mode, requested and resolved versions, latest-version baseline, readiness, and issues.
- **Workflow Definition Version**: The immutable promoted workflow content carrying one exact semantic-version label and normalized precedence identity.
- **Workflow Design API Capability**: The versioned discovery document whose additive relation tells management clients that exact-version promotion is supported.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All valid forward release and prerelease examples in acceptance testing create exactly one version with the requested label.
- **SC-002**: All malformed, equal, lower, duplicate, and build-metadata-equivalent examples create zero versions and return the specified invalid-request or conflict outcome.
- **SC-003**: Replaying identical promotion material creates no duplicate version in 100% of retry tests.
- **SC-004**: Replaying one operation key with changed version material is rejected in 100% of mismatch tests.
- **SC-005**: Existing automatic-promotion acceptance tests continue to produce the same next-major labels.
- **SC-006**: Capability-aware clients can distinguish supporting and non-supporting hosts from discovery alone in 100% of compatibility tests.
- **SC-007**: Preflight tests create or reserve zero versions while returning the same automatic/exact candidate rules enforced by promotion.
- **SC-008**: A version made stale after preflight is rejected during promotion in 100% of concurrency-boundary tests.

## Assumptions

- The service remains authoritative for validation and for the final committed version even when an author requests the label.
- Semantic-version precedence, parsing, and build-metadata equality use the existing shared versioning model.
- Version identity remains scoped to one workflow definition and tenant.
- This capability does not introduce multi-writer Git reconciliation; it only supplies the uniqueness and monotonicity prerequisite identified by ADR 0034.
- Publication of the promoted version remains a separate operation governed by publication slots and trigger preflight.
