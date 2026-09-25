# Feature Specification: File-deployed composition activation and recovery

**Feature Branch**: `codex/2036-file-deployed-activation`

**Created**: 2026-09-25

**Status**: In progress — #2038 delivered the file-only candidate handoff (US1); #2039 found no sound exact-candidate marker in the current host path; #2041 adds safe default-shell generation/readiness observation without candidate attribution. External deployment receipt, exact-match proof and recovery (US2/US3) remain open

**Input**: [Issue #2036](https://github.com/elsa-workflows/elsa-foundation/issues/2036), the [apply/recovery boundary](../../docs/reports/runtime-composition/apply-recovery-boundary.md), and the delivered [file bridge](../176-composition-file-bridge/spec.md).

## User Scenarios & Testing

### User Story 1 - Hand off a reviewed candidate (Priority: P1)

A developer has accepted an authored composition and generated a candidate for a selected host, its default shell, and one environment. They hand the operator a reviewable identity for the complete candidate and its unresolved checks. The operator can tell exactly which reviewed files belong together, what source state the candidate came from, and what is still unverified before deployment. The handoff contains no connection value or unknown secret-bearing setting.

**Why this priority**: A safe activation attempt must start with a complete, identifiable candidate. The existing file bridge deliberately stops before deployment and cannot serve as a durable deployment revision.

**Independent Test**: Generate the existing two-shell candidate, then inspect the handoff without contacting a running host. Change each included file in turn before handoff and show that it refuses until a fresh review; verify that the opaque ID names only the reviewed artifact and labels host/package/database facts unchecked.

**Acceptance Scenarios**:

1. **Given** one accepted authored selection and an unchanged complete candidate, **when** the developer prepares a handoff, **then** it identifies the selected host, shell, environment, accepted selection and catalog pin, every included file's logical role, and one complete-bundle identity.
2. **Given** any included selected or unselected file changes before handoff, **when** the handoff is prepared, **then** it refuses the stale candidate and requires fresh review; it does not silently issue the earlier identity for new bytes.
3. **Given** the candidate contains a connection value or unknown setting, **when** the handoff is displayed or exported, **then** only safe identities, types, and unresolved findings appear; raw sensitive values stay in local host files.
4. **Given** the candidate has no authenticated observation of the selected running host, **when** it is handed off, **then** deployment and runtime readiness remain explicitly unchecked.

### User Story 2 - Verify an externally deployed composition (Priority: P2)

An operator deploys the reviewed complete bundle through an existing deployment process, then requests an authorized reload of the selected shell. The result tells them whether the reviewed candidate is deployed, whether a new shell generation became active, and whether that generation is ready. A successful file deployment never masquerades as successful runtime activation.

**Why this priority**: Operators need a trustworthy answer to “what is active?” after applying a composition, particularly when feature activation and persistence preparation can fail after files change.

**Current delivery boundary after #2041**: The host observer reports default-shell reload outcome, active generation and readiness with `candidateMatch=unverified`. It does not consume an external deployment receipt or attribute a reviewed candidate to the generation. Scenario 1's exact `candidate active` result requires a new host-produced, generation-bound marker proof. A future external receipt flow can refuse a mismatched deployment independently of such a marker.

**Independent Test**: Supply a known candidate and externally deploy it to a disposable host. Observe the selected shell before and after a successful reload, including a generation change and readiness. Repeat with the same shell but a mismatched deployment identity and verify that the result does not claim the candidate is active.

**Acceptance Scenarios**:

1. **Given** the deployment and the selected host both attest that the new ready generation used the reviewed candidate identity, **when** an authorized reload succeeds, **then** the outcome records the candidate as active with the observed shell and generation.
2. **Given** files have been deployed but no reload has succeeded, **when** the operator inspects the outcome, **then** it says deployed but not verified active, with the previous active generation shown separately if available.
3. **Given** the deployed bundle differs from the reviewed candidate, **when** verification runs, **then** it refuses to attribute the running generation to that candidate, even if the reload reports success.
4. **Given** the operator lacks host-control authority, **when** reload is requested, **then** no reload occurs and no management credential is exposed to the browser or portable handoff.
5. **Given** reload succeeds but the selected host cannot attest which bundle it loaded, **when** the outcome is presented, **then** the new active generation may be reported but its match to the candidate remains unverified.

### User Story 3 - Recover from failed or uncertain activation (Priority: P3)

An operator sees a candidate fail activation or loses the response after deployment. They can distinguish an unchanged previous generation from a newly active one, inspect the deployed bundle identity, and choose a fresh repair or source rollback. A retry uses observed current state; it never blindly repeats an old review decision. Database state is not presented as rolled back.

**Why this priority**: A configuration write or deployment can precede a reload failure. Treating the entire operation as “failed with no change” risks an unsafe replay and hides the state the operator must repair.

**Independent Test**: In a disposable host, inject a shell initializer failure after deploying a candidate. Verify the previous generation remains ready, the deployed candidate is shown as not active, and an explicit repair or rollback followed by reload reaches a clearly identified ready generation. Repeat with an interrupted/unknown reload response and require readback before retry.

**Acceptance Scenarios**:

1. **Given** a new candidate fails to initialize, **when** the operator inspects the result, **then** it reports deployment changed but activation failed, and the previous generation remains identifiable and ready.
2. **Given** the reload response is lost or times out, **when** the operator retries, **then** current deployed identity and active generation are read first; the earlier candidate is not replayed without checking them.
3. **Given** the operator repairs the candidate or restores the previous complete deployment, **when** they explicitly reload again, **then** the result identifies the resulting active generation and readiness without claiming migrations or data were undone.

### Edge Cases

- A generated candidate can contain files for another shell or unselected environment; a change in one of those included files invalidates the handoff even if the selected shell's effective values seem unchanged.
- A source bundle may combine base files, one selected overlay, and unrelated settings. Candidate identity covers the complete reviewed bundle, while runtime claims remain limited to the selected shell/environment and observed host generation.
- A process override or package change can make the running host differ from disk. A local directory scan cannot certify that the runtime used the candidate or loaded expected packages.
- Existing feature management has a narrower feature-only revision. A stale feature revision or pre-save resource refusal does not provide whole-bundle concurrency protection.
- A reload can fail after a deployment, return an error while the prior generation stays active, succeed while final reporting fails, or leave its result unknown after a timeout. Each case needs a distinct status or explicit uncertainty.
- The selected shell can be unavailable or not ready before the attempt. Do not describe a previous ready generation unless one was actually observed.
- A non-default shell has no equivalent Workbench readiness observation today and is outside the first verified-activation target; file-only generation still supports it.
- Pending migrations, an unsupported persistence target, a host-owned store, or package incompatibility can block activation. An unchecked dependency must stay unresolved, not become a green preflight result.

## Requirements

### Functional Requirements

- **FR-001**: The flow MUST require an explicit supported host, shell, environment, and accepted authored candidate; it MUST reject an incomplete or ambiguous source selection.
- **FR-002**: It MUST assign an opaque identity to the complete reviewed candidate and record safe file roles, selected source context, accepted feature-selection identity, and catalog pin. It MUST recheck every included file at handoff; a change at that boundary MUST invalidate the earlier review decision. The ID alone MUST NOT be presented as later deployment integrity proof.
- **FR-003**: It MUST keep candidate-generated, deployment-confirmed, reload-attempted, and active-and-ready states distinct. It MUST NOT label a candidate active solely because files were generated, copied, or a reload request returned a successful transport status.
- **FR-004**: For v1, an existing operator/deployment process MUST own the complete-bundle switch and source rollback. Foundation MUST NOT modify live source files as part of candidate generation or claim an atomic multi-file switch it does not own.
- **FR-005**: Activation verification MUST compare the reviewed candidate identity with a trustworthy observation of the deployed source and selected shell generation. Where that correlation cannot be established, the result MUST say unverified rather than infer a match.
- **FR-006**: Only an authorized host-control actor MAY initiate reload. Host management credentials MUST stay server-side and MUST NOT appear in a browser, portable authored document, candidate handoff, report, or log.
- **FR-007**: A successful host-observation result MUST identify the selected shell, new active generation, readiness, and the separately observed deployment identity. It MUST report candidate matching as unverified until a generation-bound marker proves the exact reviewed candidate was loaded. A per-shell reload failure MUST not be reported as activation success.
- **FR-008**: Failure results MUST distinguish refusal before deployment, candidate-only, deployed-but-not-active, active, and uncertain outcomes. They MUST provide safe reasons and an operation identity without exposing raw source values, connection values, or vendor error details.
- **FR-009**: After a timeout, interrupted response, or other uncertain result, the flow MUST read back deployed identity and active generation before retry. A stale review decision MUST NOT authorize a blind replay against a changed deployment.
- **FR-010**: A failed candidate MUST leave an existing active generation available when the host lifecycle supports it. Recovery MUST allow a fresh repair or external source rollback followed by explicit reload and verification; it MUST NOT claim database or migration rollback.
- **FR-011**: Preflight MUST distinguish supported static source checks, authenticated host observations, and unresolved package/database/migration facts. Unknown or unsupported facts MUST remain visible beside observed shell readiness and MUST block any broader compatibility or persistence-safe claim.
- **FR-012**: The v1 flow MUST leave the existing feature-only editor's revision, save, and resource refusal semantics intact; resource-aware whole-bundle editing MUST NOT be routed through that editor.
- **FR-013**: The first supported activation target MUST be one Workbench-style host's default shell and one environment. A non-default shell MUST remain file-only until its readiness observation is specified. Multi-shell transactions, arbitrary host layouts, browser apply, and a Foundation-owned deployment switch are outside this release.

### Key Entities

- **Reviewed candidate**: The accepted authored selection and complete generated local bundle, with a stable identity and unresolved findings. It is not the running runtime.
- **Deployment observation**: Evidence from the external deployer or selected host about which complete bundle is currently deployed. It has a distinct authority and time from candidate generation.
- **Active generation observation**: The selected shell's observed active generation and readiness after a reload attempt, including an error or unknown result.
- **Activation operation**: One reviewed attempt linking candidate, expected current deployment, deployment observation, reload attempt, and final/uncertain outcome without embedding secrets.
- **Recovery action**: A new repair or source rollback decision made from observed current state, followed by explicit reload and verification.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the two-shell fixture, 100% of included files are checked at handoff; changing any one before handoff refuses and requires fresh review. A later deployment relies on its own artifact-integrity evidence.
- **SC-002**: In every defined success, failure, timeout, and mismatch scenario, the operator sees separate candidate, deployed-source, and active-generation states; zero scenarios label generated-only or deployed-only output as active.
- **SC-003**: A failed candidate initializer leaves the previously observed ready generation serving in the disposable host; repair or source rollback plus explicit reload produces a newly observed ready generation. Exact candidate matching remains a separate proof gate.
- **SC-004**: Canary connection and unknown-setting values occur zero times in shareable handoff, standard output, standard error, logs, and safe error results.
- **SC-005**: The initial flow works for one supported host/shell/environment without introducing an in-process whole-bundle editor or requiring a browser-held management credential.

## Assumptions

- The [file bridge v1](../176-composition-file-bridge/contracts/file-bridge-v1.md) already provides a reviewed, fresh-directory candidate. This specification begins at its output; import and generation behavior are not redefined here.
- An operator or existing deployment process can switch a *complete* host configuration bundle and preserve a previous complete version. The specification must define what evidence that deployer returns; the repository does not yet provide a general atomic switch.
- The selected host offers authorized reload and active-generation/readiness observation. If those observations cannot correlate an exact candidate to the running generation, the first implementation must report that gap instead of claiming success.
- Workbench's current readiness observation is for its configured default shell. The first verified-activation target is therefore that shell; other shells keep the delivered file-only workflow.
- Migration prerequisites and provider/connection checks need separate evidence. [#1902](https://github.com/elsa-workflows/elsa-foundation/issues/1902), [#1895](https://github.com/elsa-workflows/elsa-foundation/issues/1895), [#1900](https://github.com/elsa-workflows/elsa-foundation/issues/1900), [#1145](https://github.com/elsa-workflows/elsa-foundation/issues/1145), and [#1951](https://github.com/elsa-workflows/elsa-foundation/issues/1951) remain separately owned.
- This is a bounded product contract under [#1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964), not a claim that the broader runtime builder UX or managed resource editor is ready.
