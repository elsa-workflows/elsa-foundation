# Feature Specification: Worker HTTP starting profile

**Feature Branch**: `codex/2326-worker-http-profile`

**Created**: 2026-10-02

**Status**: Implemented — delivery [#2326](https://github.com/elsa-workflows/elsa-foundation/issues/2326).

**Input**: Program [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), epic [#1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961), whole delivery task [#2326](https://github.com/elsa-workflows/elsa-foundation/issues/2326). Publish a reviewed Worker starting point and prove that the developer-generated candidate drives the real worker's feature selection and settings. Completed prerequisite [#2308](https://github.com/elsa-workflows/elsa-foundation/issues/2308) supplies the production bearer normalization boundary.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select a stable Worker starting point (Priority: P1)

A developer selects one Worker profile instead of maintaining a long feature list, reviews its exact expansion and prerequisites, and preserves an existing pinned composition when the catalog evolves.

**Why this priority**: This is the profile outcome promised by the composition program; a label without stable membership would make deployments unpredictable.

**Independent Test**: Resolve the Worker selection and both previously published catalog pins; compare exact members, definition identities and explanations without starting a host.

**Acceptance Scenarios**:

1. **Given** the bundled catalog, **When** a developer selects Worker, **Then** it expands to exactly the reviewed 19 features with immutable profile identity and inclusion reasons.
2. **Given** either previous catalog snapshot, **When** an existing composition resolves its exact pin, **Then** it retains the original snapshot and unchanged Embedded/diagnostics identities where present.
3. **Given** an unknown or mismatched pin, **When** it is planned, **Then** the discrepancy is unresolved and the accepted composition is not silently upgraded.

### User Story 2 - Generate and run the reviewed composition (Priority: P1)

A developer initializes, plans, accepts and generates a Worker candidate, then starts a real worker using those files. Its authenticated caller executes a durable workflow, resumes it using an external event and sees permission revocation survive a worker restart.

**Why this priority**: The profile must reach useful runtime behavior through the actual developer workflow, with configuration remaining the activation authority.

**Independent Test**: Use the real developer commands and generated candidate in the existing authenticated, persistent Worker journey, including a distinct process restart.

**Acceptance Scenarios**:

1. **Given** a fresh workspace and Worker starting selection, **When** initialization, planning, interactive acceptance and generation complete, **Then** authored, accepted and generated selections agree exactly and source files remain unchanged.
2. **Given** separately supplied identity, persistence and locking prerequisites, **When** the worker consumes that candidate, **Then** its actual enabled features equal the accepted selection and its receipt identifies the candidate files used.
3. **Given** a legitimately mapped caller, **When** it executes, waits and resumes a workflow, **Then** the real durable state reaches completion; revoked permission denies the same token on the next request and after a distinct process restart using the unchanged candidate and databases.
4. **Given** anonymous, invalid or ungranted credentials, **When** existing runtime routes are called, **Then** the established authentication/authorization refusals and absence of unauthorized effects are retained.

### User Story 3 - Make individual edits authoritative (Priority: P1)

A developer removes one optional capability and changes a local authentication setting, reviews a new candidate, and sees both edits take effect in the running worker.

**Why this priority**: Profiles must preserve granular control; a fixed activation list or parallel settings provider would conceal changes made through the builder or CLI.

**Independent Test**: Generate a second candidate with one reviewed removal and a different token audience, start a fresh worker and verify exact features plus paired authentication outcomes.

**Acceptance Scenarios**:

1. **Given** the same pinned Worker profile with an explicit ControlFlow removal, **When** the edit is accepted and generated, **Then** the actual worker enables exactly the accepted 18 features and does not restore the removed feature.
2. **Given** this candidate's alternate audience and a legitimate capabilities-read grant, **When** tokens for the old and new audiences call the real capabilities endpoint, **Then** the old audience is denied before mapping or runtime effects, while the new audience succeeds with one mapping lookup and no workflow state.
3. **Given** a worker implementation retaining the previous static selection or old audience, **When** the respective consumption control runs, **Then** it fails; restored behavior passes.

### Edge Cases

- Unknown/mismatched pins remain unresolved; publication never changes old bytes or replaces accepted intent.
- Overlapping membership and explicit removals retain the existing planner semantics; a missing required dependency remains unresolved.
- Selection metadata does not prove package availability, checked persistence, activation readiness or external identity-provider interoperability.
- Feature settings already present in source files survive generation. The portable authored document does not become a secret store or a bypass for refused settings.
- Shared Runtime persistence does not enroll the host-owned IAM store. Identity tenant selection remains host-owned and agrees with the ordinary persistence context.
- Restart reuses the same candidate, issuer/key/token and databases, but runs in a different OS process.
- The capabilities-only removal control keeps startup prerequisites and does not claim workflow execution with the modified selection.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Publish one immutable Worker starting profile with the reviewed 19-member selection, reasons and explicit host prerequisites; configuration values MUST remain separate from membership.
- **FR-002**: Preserve both old catalog snapshots and exact-pin resolution, including unchanged existing profile/group identities. Unknown or changed-content pins MUST remain unresolved.
- **FR-003**: The real developer initialization, planning, interactive acceptance and generation workflow MUST preserve profile/catalog identity and exact feature selection through its produced artifacts.
- **FR-004**: Generated candidate files MUST be the sole feature/settings selection authority for the acceptance worker. Observed enabled features and consumed-file identity MUST match accepted intent; discovery is not selection.
- **FR-005**: Source-local identity/store/locking settings MUST survive generation and bind in the real worker without a parallel feature/settings configuration source.
- **FR-006**: The ordinary host-selected static tenant, separate IAM target and named Runtime resource MUST retain existing ownership and isolation; request tokens MUST NOT choose persistence scope.
- **FR-007**: Preserve actual validated-bearer execute/event/resume/completion, invalid/ungranted credential controls, persisted revocation and fresh-process restart through the primary exact19 candidate.
- **FR-008**: The secondary accepted18 candidate MUST demonstrate the removal and alternate audience through actual activation and paired capabilities-read outcomes; no runtime state may be created by this read control.
- **FR-009**: Deterministic cleanup MUST stop owned children and issuer and remove isolated files on success and failure; sensitive startup values MUST remain outside command arguments and retained receipts.
- **FR-010**: Documentation MUST distinguish selection, generated candidate, observed local activation and deployment readiness, including host-owned prerequisites and local-issuer limitations.

### Key Entities

- **Worker profile**: Immutable starting selection and reviewed rationale; pinned independently from host configuration.
- **Authored/accepted composition**: User intent and reviewed exact membership, with optional explicit additions/removals.
- **Generated candidate**: Fresh output directory containing the reviewed configuration for one shell/environment, identified by consumed-file hashes.
- **Host prerequisites**: Identity namespace/audience, ordinary tenant, database targets, locks and signing inputs supplied independently of catalog membership.
- **Actor receipt**: Bounded evidence of process/artifact/candidate identity, actual enabled features, authentication wiring and durable effects.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Selecting Worker once yields exactly the reviewed 19 features; both existing pinned catalogs retain their original identity and membership.
- **SC-002**: Every primary artifact and the actual running worker agree on selection; the restarted worker reports the same candidate and artifact identity from a distinct process.
- **SC-003**: One authorized durable execution completes after event resumption; persisted revocation denies the unchanged token before and after restart. All retained invalid/ungranted controls pass.
- **SC-004**: The explicitly edited candidate activates exactly 18 features. The old audience is denied with zero mapping reads, and the candidate audience succeeds with exactly one mapping read; neither read creates workflow state.
- **SC-005**: Both adverse configuration-consumption controls detect their corresponding bypass before restored final verification passes.

## Assumptions

- Scope is one local single-host, static-provider/tenant Worker composition; existing permission and bearer-normalization contracts remain authoritative.
- Prior catalog publication, file-bridge semantics and production normalization are delivered prerequisites. No new public command, identity platform, store enrollment, suite, provider matrix or CI cadence is needed.
- The exact primary membership and real artifact/activation proof are specified in [the contract](contracts/worker-profile.md). Implementation mechanics belong to [plan.md](plan.md).
- External IdP deployment interoperability, distributed locking, dynamic tenancy, Authoring publication, real-human builder evaluation and recoverable deployment remain separate program outcomes.
