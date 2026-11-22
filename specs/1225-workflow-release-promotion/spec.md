# Feature Specification: Workflow releases and environment promotion

**Feature Branch**: `agent/codex/3590-release-contracts`

**Created**: 2026-11-05

**Status**: In progress — product decisions confirmed; delivery is being resubmitted in reviewable slices after #3911 closed without merge. This slice contains contracts and planning only.

**Input**: Deliver [#3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590) end to end under the [owner authorization](https://github.com/elsa-workflows/elsa-foundation/issues/3590#issuecomment-6671598586).

## User Scenarios & Testing

### User Story 1 — Create a release that preserves tested content (Priority: P1)

A workflow author selects exact workflow versions, inspects their required dependencies and creates an immutable release. The author can download it, retain it in source control, transfer it offline and attach results from testing that exact content.

**Why this priority**: A promotion is trustworthy only when its content is identifiable and complete.

**Independent Test**: Create the same release twice from unchanged selections, compare identities, export/import it into an empty catalog and verify every workflow and supported dependency retains its exact identity and content.

**Acceptance Scenarios**:

1. **Given** selected workflows sharing reusable definitions, **when** a release is created, **then** its manifest contains the complete supported transitive closure once per exact artifact and identifies every prerequisite.
2. **Given** unchanged exact selections, **when** the manifest is reproduced or transferred offline, **then** its content identity remains unchanged despite creation time, label, transport or target bindings.
3. **Given** a missing, corrupt, cyclic or unsupported required dependency, **when** creation or import is requested, **then** the operation reports the dependency path and makes no incomplete release eligible for activation.
4. **Given** attached test evidence, **when** the release is inspected, **then** the actual tested candidate, checks, mode, environment and binding observations remain visible. Controlled evidence does not claim real integration verification.

### User Story 2 — Preview and apply to a chosen environment (Priority: P1)

An operator chooses a target environment, maps the release's logical resource requirements to authorized target resources, reviews changes and explicitly applies them. The same immutable release can be applied to another environment with different bindings.

**Why this priority**: Operators need to understand the actual target changes without editing the tested workflows.

**Independent Test**: Promote one downloaded release to two separate owned environments using different connection mappings; compare all immutable content identities and inspect both target-specific attempts.

**Acceptance Scenarios**:

1. **Given** compatible targets with different resources, **when** the same release is previewed, **then** each preview separates immutable content additions/conflicts from binding changes and current serving-version changes.
2. **Given** missing capability, package/provider, connection, secret reference, permission or incompatible identity, **when** readiness is checked, **then** the blocker is explicit and apply cannot bypass it.
3. **Given** a preview followed by target drift, **when** apply is requested, **then** actual state is validated again and an obsolete plan is refused or reported as superseded; it never silently substitutes a new plan.
4. **Given** a retry after an uncertain response, **when** the same idempotency key and payload are submitted, **then** the existing attempt is reconciled without duplicate activation. Reusing a key for different intent is refused.
5. **Given** two overlapping applies or another publication writer, **when** they race, **then** existing owners enforce revision/ownership checks and each attempt reports actual effects and conflicts accurately.

### User Story 3 — Recover activation and roll back future starts (Priority: P1)

An operator follows a durable attempt through staging, activation and interruption recovery. The operator can explicitly select a retained previous release after a fresh preview. Running work keeps its exact content and resource mapping choice.

**Why this priority**: Partial activation and rollback are ordinary operating cases, not exceptional bookkeeping.

**Independent Test**: Keep real executions waiting while activating a new release, interrupt the activating host, recover it, roll back, then resume old and new executions and verify their original pins, dependencies and mapping choices. Subsequent starts follow the selected serving versions.

**Acceptance Scenarios**:

1. **Given** several workflow slots, **when** activation progresses, **then** each owner switches its own slot. A brief mixed interval is allowed and visible; no group transaction is promised.
2. **Given** interruption before or after an owner commits, **when** recovery runs, **then** it reads the authoritative outcome, resumes forward when safe and never mistakes an unknown result for failure or success.
3. **Given** a permanent blocker or another writer's intervening change, **when** recovery cannot finish the original intent, **then** the attempt identifies applied, pending, failed and superseded members and the next operator action. It does not automatically undo serving changes.
4. **Given** running executions, **when** activation, rollback or rebinding occurs, **then** their exact workflow/dependency pins and chosen resource mappings remain unchanged. Normal credential rotation and revocation remain effective.
5. **Given** a retained prior release, **when** rollback is requested, **then** current resources, permissions, trigger conflicts and prerequisites are validated before selecting it for new starts. It never promises to undo schema changes or external business effects.

### User Story 4 — Operate through Studio and pipelines (Priority: P1)

Authors and operators use Studio for release creation, inspection, mapping, preview, apply and history. Integrators use the API and packaged CLI for the same operations and receive machine-readable results.

**Why this priority**: Teams need one deployment contract across interactive and automated delivery.

**Independent Test**: Perform the same target operations through the composed Studio host and packaged CLI against real target APIs; compare preview and outcome semantics, including deliberate failure cases.

**Acceptance Scenarios**:

1. **Given** the same release, target and mappings, **when** Studio and CLI request preview, **then** they receive equivalent changes, readiness findings and stale-plan behavior.
2. **Given** an attempt in any outcome, **when** its page is reopened or its pipeline result retrieved, **then** actor, target/tenant, immutable identity, binding capture, evidence association and member outcomes remain understandable.
3. **Given** denied access or a changed backend/tenant identity, **when** an operation is attempted, **then** it is refused without forwarding source credentials or exposing another tenant's records.
4. **Given** keyboard, narrow-layout and relevant theme use, **when** the complete composed journey is exercised, **then** primary actions, focus, validation and recovery remain usable and saved state survives reopening.

### Edge Cases

- The same identity exists with different bytes, or a label matches while an exact version/template differs.
- A dependency is available on the source only as inlined runtime content; source/catalog provenance cannot be reconstructed safely.
- Offline input has unknown format versions, duplicate identities, excessive size/depth or unsafe file paths.
- A mapped resource is deleted/recreated under the same name, revoked or changed after preview.
- Only the target mappings change while workflow content already serves.
- The caller loses permission or changes tenant between preview, apply and recovery.
- A process dies between a domain commit and the attempt-history write.
- A worker loses its lease while an owner operation is in flight; an old request must not create an untracked late effect.
- A different publisher changes one slot while another member of the release has already switched.
- A rollback artifact is retained, but a package/resource/trigger prerequisite is no longer available.
- Test evidence is failed, skipped, cancelled, unavailable, incomplete, stale, or refers to other content/bindings.
- An independently managed trigger or unsupported module artifact is required; promotion must not take ownership silently.

## Requirements

### Functional Requirements

- **FR-001**: Users MUST create a release from explicit immutable workflow selections and inspect the complete supported required dependency closure before activation.
- **FR-002**: The release MUST have a reproducible content identity covering exact workflow/dependency identities, canonical content, dependency edges and runtime/provider prerequisites. Mutable labels, target mappings, test results and attempt history MUST NOT change that identity.
- **FR-003**: Export/import MUST preserve source definition/version identities and owner-declared version values (including ETL integer versions and workflow semantic versions), artifact/template/model identities and hashes without recompilation, renumbering, rewriting or silent substitution. Selected-version associations and a deduplicated artifact's producer identity MUST remain distinct. Different target content under the same immutable identity MUST block apply.
- **FR-004**: The first complete delivery MUST support native, BPMN and ETL workflows and their complete supported reusable/required dependency closure through each model's owning contracts. Required unsupported kinds, missing/corrupt dependencies and cycles MUST fail visibly with their dependency path; unsupported content MUST NOT be silently omitted. Native-only delivery does not complete this feature.
- **FR-005**: Runtime packages, providers, consumer/schema versions and resource kinds MUST be declared and validated as target prerequisites. Apply MUST NOT install packages, upgrade servers or migrate schemas.
- **FR-006**: Users MUST export/import a portable release for offline transfer and source-control storage without embedding credentials or changing its immutable identity.
- **FR-007**: Each target MUST have a stable authority/environment identity and an explicit authenticated tenant scope. URLs and display environment labels alone MUST NOT establish identity.
- **FR-008**: Studio MUST reuse existing backend profiles and independently authenticated target sessions; CLI MUST address the same target API. Source credentials MUST NOT be forwarded as target credentials.
- **FR-009**: Target resource mappings MUST reference authorized resources with compatible kinds and stable incarnation identities. Requirement keys MUST retain the exact defining member plus authored occurrence/input or model-owned scope; equal technical names MUST NOT implicitly merge requirements. Explicit sharing is permitted only after compatible requirements are deliberately grouped. Preview MUST capture observable revisions; apply MUST revalidate them.
- **FR-010**: Each deployment MUST retain an immutable non-secret mapping capture. New starts MUST capture its identity; running executions MUST retain their mapping choice across later deployments and rollback.
- **FR-011**: Mapping capture MUST preserve normal target-owned resource configuration changes and secret rotation/revocation policies. It MUST NOT promise historical connection settings or retain secret values.
- **FR-012**: Secret values and credential-bearing connection settings MUST remain outside bundles, mappings, previews, evidence, logs and history. Permission and redaction rules MUST follow the owning resource domain.
- **FR-013**: Preview MUST distinguish content additions/already-present/conflicting content, serving changes, binding changes, trigger conflicts, prerequisites and evidence status. Preview MUST NOT reserve readiness.
- **FR-014**: Apply MUST validate current authorization, scope, capabilities, resources, immutable identities and owner revisions again. A stale preview MUST NOT be silently refreshed into a different apply.
- **FR-015**: Apply MUST use a target/tenant-scoped idempotency key bound to immutable intent. Identical retry MUST observe/recover the existing attempt; different intent with the same key MUST be refused.
- **FR-016**: All dependencies MUST be validated and durably staged before selected workflow roots become eligible for new starts. Dependency-only workflows MUST NOT acquire independent start eligibility, routes or schedules merely through import, and existing serving slots MUST remain unchanged unless explicitly selected in the reviewed plan. Exact retained dependency invocation MUST remain available through normal authority. Staging success MUST NOT imply activation success.
- **FR-017**: Publication/start authority MUST remain with existing owners under accepted ADR 0043. Release records and history MUST NOT independently serve traffic or start workflows.
- **FR-018**: Group activation MUST coordinate deterministic per-owner changes with explicit member outcomes. Mixed serving versions during the switch interval are permitted; no cross-domain/group atomicity is claimed.
- **FR-019**: Overlapping applies and other publication writers MUST be detected through owner-enforced concurrency checks. A release-only lease MUST NOT be treated as a fence against unrelated writers.
- **FR-020**: Durable recovery MUST reconcile uncertain owner outcomes before retrying, continue forward when safe and stop visibly on conflicts. Rollback MUST remain an explicit action; recovery MUST NOT automatically restore previous serving versions.
- **FR-021**: Interrupted and superseded attempts MUST account for dispatched operations and late commits. An unresolved owner outcome MUST remain unknown/recovering rather than terminal success/failure.
- **FR-022**: A binding-only deployment MUST be applied as a real change when its mapping capture differs, even when the immutable artifact already serves. No-op MUST mean all intended content, mappings and serving state already match.
- **FR-023**: Rollback MUST be a fresh validated application of selected retained content/mappings through the same publication owners, subject to current permissions, resources, prerequisites and trigger conflicts.
- **FR-024**: Active executions' exact workflow, model and reusable dependency pins MUST remain protected across activation and rollback. Supported rollback content and unresolved attempts MUST have explicit retention roots and release conditions. Release creation MUST durably freeze the complete manifest/blobs before exposing a downloadable release; source GC or later catalog changes MUST NOT break that retained release.
- **FR-025**: History MUST retain actor, target/tenant, release identity, binding identity, request/attempt identity, observed owner changes, evidence associations, outcome and useful diagnostics. Audit records MUST distinguish denied and unsuccessful attempts without leaking another tenant's state.
- **FR-026**: History MUST distinguish staged, applying, active, no-op, failed, partial, recovering and superseded outcomes; member-level uncertainty and residual serving effects MUST remain visible.
- **FR-027**: Test evidence MUST identify the actual frozen candidate/closure, frozen suite/case revision identities and content digests where applicable, mode, verdict, target/bindings and prerequisites. Coverage MUST identify the workflows or configured activity occurrences actually tested; subset evidence MUST NOT imply whole-release coverage. Attaching evidence MUST NOT imply it passed, tested this content or verified another integration environment.
- **FR-028**: Matching exact-candidate results MUST be associable before the complete #3588/#3892 saved-suite product ships. Optional target policies MAY require evidence; this feature MUST NOT add a universal approval/publication gate.
- **FR-029**: Independently managed triggers and module resources MUST retain their owning lifecycle. Unsupported dependencies MUST be explicit; #3582 delivery MUST NOT be presumed.
- **FR-030**: Studio and API/CLI MUST share target validation and application semantics, including mappings, stale previews, retries, rollback, recovery, history and error outcomes.
- **FR-031**: Packaged CLI output MUST be machine-readable with documented exit statuses that distinguish success/no-op from denied, refused, failed and incomplete/recovering operations.
- **FR-032**: Source-owned/read-only content MUST retain its provenance and ordinary inspection rules. Promotion MUST NOT create a competing editable catalog.
- **FR-033**: Documentation and runnable examples MUST explain creation, offline transfer, target preparation, preview/apply, evidence limits, recovery and rollback using owned test environments.
- **FR-034**: Supported root selection MUST cover ordinary native/BPMN workflow publication, visible direct-BPMN publication and standalone ETL pipeline versions, alongside their use as dependencies. A model-owned root with no independent start selector becomes available through its existing catalog/invocation contract; promotion MUST NOT invent an automatic start for it. Direct-BPMN serving selection MUST be mediated by its owning Runtime publication authority, never the release journal.
- **FR-035**: Target-local source-reference/storage-incarnation locators embedded as source provenance MUST be resolved through a captured owner-validated deployment context while preserving exact tested model content, hashes and logical pin identities. Copying a physical source incarnation or rewriting tested node metadata is forbidden. Missing or ambiguous associations MUST refuse before execution.

### Key Entities

- **Release**: Immutable selected roots, exact supported content/closure, prerequisites and content identity, with separate display metadata.
- **Target**: Stable environment authority, authenticated tenant scope and discovered versioned capabilities, selected through existing backend mechanisms.
- **Binding capture**: Immutable mapping from authored logical requirements to target resource identities/incarnations, with non-secret revision observations and its own digest.
- **Preview**: Target-specific observed plan, owner/resource revisions, blockers, evidence status and intended changes; an observation, not a reservation.
- **Deployment attempt**: Durable target-local application intent, idempotency key, member operations, observed effects and recovery state. Activation and rollback use the same semantics.
- **Evidence association**: Reference to a result's exact subject, mode, revisions and verdict; separate from immutable release content.
- **Retention root**: Explicit protection of content/mapping captures needed by serving slots, pinned executions, unresolved attempts or supported rollback.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Reproducing/exporting/importing releases containing native, BPMN and ETL roots, including at least two workflows sharing a transitive reusable dependency, preserves every selected version, artifact/template/model identity, dependency edge and content hash.
- **SC-002**: That same release is promoted through actual Studio and packaged CLI calling real target APIs to two separate owned environments with different bindings, with unchanged immutable release content. Direct API parity has a separate witness.
- **SC-003**: Missing/incompatible dependencies, mappings and prerequisites; denied/cross-tenant access; identity conflicts; stale/concurrent apply; no-op/retry and target drift each have an executed positive or refusal witness with explicit outcome.
- **SC-004**: Real composed hosts recover interruption at each material staging/activation boundary, including partial activation and an owner commit whose response/history write is lost. No incomplete attempt is reported active.
- **SC-005**: Real long-running native, BPMN and ETL executions before, during and after activation/rollback complete using their captured exact pins/dependencies/mapping choices; subsequent starts use the intended serving versions. Cross-model dependencies exercise the owning model's actual semantics.
- **SC-006**: Retained prior content can be selected after fresh readiness checks, and deliberately unavailable resources/trigger conflicts produce actionable rollback refusal without changing running executions.
- **SC-007**: Retrieved history and pipeline results distinguish all claimed outcomes, identify the exact actor/target/content/bindings and preserve failure/uncertainty rather than masking it as success.
- **SC-008**: Current-commit composed Studio journeys, saved-state/reopen, keyboard/focus, relevant themes/narrow/access/validation/recovery states have downloadable inspected artifacts and independent UX acceptance. Failed/skipped/unavailable cases remain visible.
- **SC-009**: Relevant package/consumer boundaries, targeted regression/bite controls, required repository checks, reviewed merge and resulting-main qualification pass for the delivered scope. A specification, prototype, staged bundle or opened PR alone does not satisfy completion.

## Assumptions

- Scoped delivery is authorized beside current priorities, preserving consolidation's priority one and all unrelated owner-directed pauses. The readiness cap means no new ready labels or issue hierarchy are added now; #3590 owns milestones/checklists.
- #3590 has no Initiative type, parent or `Part of` linkage to an Initiative at intake. Normal scoped design applies; any future dependency on initiative-owned contracts must follow their actual agreement gates.
- Accepted ADR 0043 governs publication/start authority. ADR 0034 and ADR 0043's October convergence amendment remain proposed design context. This spec does not ratify them or provisional constitution sections.
- Mapping choice means the target resource identity/incarnation, not a frozen secret or historical connection settings. Rotation, revocation and supported target configuration changes retain ordinary resource-owner semantics.
- Candidate/evidence conventions are coordinated on [#3588](https://github.com/elsa-workflows/elsa-foundation/issues/3588#issuecomment-6671608363), including #3892. No unshipped runner or model capability is claimed.
- Actual deployment exercises use disposable owned test hosts/stores. Shared/production deployment, package publication, infrastructure provisioning, server/schema upgrades, running-instance migration and external-effect reversal are outside this authorization.

## Confirmed decisions and remaining agreement

The three initial #3590 owner decisions plus exact-content promotion remain selected. On 5 November 2026 the owner also selected: coordinated per-owner switches with visible recovery/mixed interval; per-run mapping choices with normal credential rotation; forward recovery with explicit rollback.

The owner subsequently selected native, BPMN and ETL support in the first complete delivery. Detailed identity, staging, binding/start capture, owner receipt/fencing, retention and recovery contracts require a reviewed design before consequential implementation. Source-grounded research and the staged critical path accompany this spec; they are proposals rather than acceptance evidence.

The owner also selected publication-slot enforcement for direct BPMN, including refusal of replaced exact-version fresh
starts and explicit activation for older unslotted publications. Running models and already accepted work retain their pins.
