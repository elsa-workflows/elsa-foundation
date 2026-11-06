# Feature Specification: Durable Runtime Preparation Retention

**Feature Branch**: `agent/codex/2892-preparation-retention`

**Created**: 2026-11-06

**Status**: Approved — approved to implement under delegated #2892 authority after clean requirements, plan R3 and held API reviews; implementation has not started and no T066 acceptance is claimed.

**Input**: Provide the Runtime prerequisite requested by Program #3519 / T066: retain exact reviewed native, model and reusable-template material durably without granting permission to run or export it, then transfer or release retention using canonical publication authority.

**Program goal**: [Unified Execution](../../docs/program-goals/unified-execution.md), owned by [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892); dependent consumer: [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519), [T066](../1179-specialist-agents/tasks.md).

**Requirements authority**: [Original request](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6641754588), [Runtime intake](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6641948798), [current source handoff](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6676494892), and [spec reservation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6676842316).

**Verified baseline**: main `fd0fdb21eacabe690183ff761432746e9c7e680c` and assessed #3904 head `7cf4e425143a2320141ea81c980506d75d515610` provide only [Published and TestRun references](https://github.com/elsa-workflows/elsa-foundation/blob/fd0fdb21eacabe690183ff761432746e9c7e680c/Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableSourceReference.cs#L288). Callback-scoped write protection and typed post-commit completion, including `3e99652898eb9682c627fe1de8fdd363a2295597`, do not supply the durable preparation root required by [collection](https://github.com/elsa-workflows/elsa-foundation/blob/fd0fdb21eacabe690183ff761432746e9c7e680c/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableReferenceGarbageCollector.cs#L512).

**Architecture status**: This draft specifies outcomes, not a ratified public contract or schema. The [constitution boundary](../../AGENTS.md#constitution-boundary) remains: framework §2.24, Elsa §E2.8 activity versioning/Model X, and §E2.9 retain their draft or provisional status. This work does not ratify or amend them. Existing meanings remain in the [Elsa glossary](../../docs/glossary/elsa.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep reviewed material available (Priority: P1)

As a publication owner, I need the exact material selected for review to remain available while the publication is pending, so a restart or collector cannot invalidate the review or silently replace its candidate.

**Why this priority**: A review can outlive the artifact staging grace. Losing any selected dependency prevents safe publication of the reviewed candidate.

**Independent Test**: Retain native, model and reusable-template dependencies without other retention; cold-restart and collect beyond staging grace, compare every identity/content hash, then attempt start and runnable export with zero resulting effects.

**Acceptance Scenarios**:

1. **Given** an authorized preparation with a complete exact selection, **When** retention is accepted, **Then** its selected roots and all required transitive material remain protected after the accepting call ends, across cold restart and collection beyond staging grace.
2. **Given** the same preparation identity, owner generation and selection, **When** acceptance is repeated after a lost reply, **Then** the owner recovers the same accepted retention without adding roots, changing material or extending authority.
3. **Given** a missing, contradictory, replaced or unprotected closure member, or an unproved provider boundary, **When** acceptance races collection or ownership changes, **Then** acceptance refuses or remains explicitly unknown; it never reports a complete retained candidate with an unprotected member.
4. **Given** an accepted preparation generation, **When** its identity is reused with changed material, scope or unproven ownership, **Then** the request is refused without changing existing retention.
5. **Given** preparation-only authority, **When** direct, published, test-run, trigger, child/model dispatch or runnable export/import is attempted, **Then** refusal creates zero executions, queued work, activations or runnable exports/publications.
6. **Given** shared material also has legitimate independent published, test-run or execution authority, **When** that authority is used, **Then** its existing behavior remains unchanged; preparation alone still grants no runnable authority.

### User Story 2 - Hand retention over after publication (Priority: P1)

As a publication owner, I need preparation retention to overlap canonical publication acceptance, so material remains protected through commit failures and lost acknowledgments.

**Why this priority**: Releasing preparation before a verified publication creates a collection gap; treating an unknown outcome as success can lose the reviewed material.

**Independent Test**: Interrupt publication before commit and after commit but before acknowledgment, restart the owner, and reconcile using the canonical publication receipt before releasing preparation.

**Acceptance Scenarios**:

1. **Given** retained preparation, **When** canonical publication has not committed or its outcome cannot be verified, **Then** preparation remains retained and is not reported as transferred.
2. **Given** a canonical receipt proving durable publication of the exact reviewed selection in the correct scope, **When** handover completes, **Then** the publication's canonical retention is established before preparation retention is released, with no collection gap.
3. **Given** publication committed but its reply or the later release reply was lost, **When** recovery repeats the operation, **Then** it uses the same canonical receipt and converges without republishing different material, duplicating publication effects or removing unrelated roots.

### User Story 3 - Release rejected preparations safely (Priority: P1)

As a publication owner, I need to release a terminally rejected preparation and recover interrupted cleanup, so abandoned material becomes collectible without losing other owners' material or reviving a closed preparation.

**Why this priority**: Safe retention requires both durable rejection authority and cleanup that survives process failure.

**Independent Test**: Record terminal rejection, interrupt release, restart, repeat release and collect unreferenced material; then replay an old creation or release request against that preparation and a newer generation.

**Acceptance Scenarios**:

1. **Given** durable terminal rejection for the exact preparation and owner generation, **When** release completes, **Then** only that preparation's retention ends; material with no remaining independent root becomes eligible for ordinary collection.
2. **Given** rejection or cleanup has an unknown outcome, **When** the owner restarts, **Then** recovery distinguishes the known terminal decision from unresolved work, retains uncertain material and resumes only cleanup authorized by that decision.
3. **Given** release already completed, including after ordinary collection, **When** the same authorized release is repeated, **Then** it is idempotent and does not recreate retention.
4. **Given** a released preparation, a superseded owner or a newer preparation generation, **When** a stale creation, handover or release request is replayed, **Then** it cannot resurrect the closed preparation or change another generation's roots.

### Edge Cases

- A collector has selected a dependency before preparation creation begins, or deletion wins while a writer waits for authority.
- A native/model artifact or template is removed and recreated under the same logical identity but a different physical incarnation.
- The selection includes several roots, shared dependencies, nested templates, model-to-native dependencies or directly selected templates without a native wrapper.
- A selected provider, decorator or combination cannot prove complete durable fencing, or material/query state is corrupt or unavailable.
- A pending preparation outlives the staging grace, its initiating process or a temporary write grant; none is a terminal publication decision.
- Two preparations or a publication share the same material; releasing one must leave the others' retention intact.
- A late operation arrives after terminal release and collection, or after a new owner generation has taken over.
- A publication receipt names different material, a different preparation or another scope, or publication and Runtime persistence are independently unavailable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Runtime MUST provide durable, non-runnable preparation retention for an authorized publication operation, using existing canonical material, template and source-reference authority.
- **FR-002**: Preparation MUST bind its scope, owning operation/generation, reviewed snapshot and exact selected roots to their complete transitive native/model/template material. Each kind retains its own identity; a template is not a native workflow artifact.
- **FR-003**: Durable acceptance MUST verify the complete selection, actual physical incarnations and current ownership under fencing against collection. Ownership loss, expired write authority or material replacement MUST NOT produce successful partial retention.
- **FR-004**: Unsupported provider combinations, incomplete/contradictory material and unprovable fencing MUST be refused before creating preparation retention, preserving existing roots. Cross-provider atomicity or rollback MUST NOT be assumed.
- **FR-005**: Accepted preparation MUST survive the accepting call, temporary write grants, cold restart and collection beyond staging grace until authorized transfer or terminal release. Age, inactivity or process loss alone MUST NOT authorize release.
- **FR-006**: Repeating the same preparation generation and selection MUST recover its persisted outcome. Changed selection, scope or unproven ownership MUST conflict without replacing material or reviving a superseded generation.
- **FR-007**: Closure protection MUST cover selected and referenced native/model material and reusable templates, including nested template dependencies. Model/template retention MUST NOT require an execution root or synthetic native wrapper.
- **FR-008**: No start, dispatch, activation, binding, runnable export or import path may derive Published or TestRun authority from preparation retention. Explicit preparation selection and reference-less fallbacks MUST NOT bypass refusal, including through replay or indirect child/model/trigger paths. Refusal MUST precede effects that create runnable work or a runnable export.
- **FR-009**: Independently valid published, test-run and retained-execution authority over shared material MUST remain effective. Refusing preparation authority MUST NOT make that shared material globally unrunnable or unexportable.
- **FR-010**: Preparation retention MUST overlap canonical publication retention. Successful handover MUST verify the canonical committed receipt and protection of the exact reviewed selection in the correct scope before releasing preparation; local success alone is insufficient.
- **FR-011**: Failed, unavailable or unknown publication/handover outcomes MUST keep preparation retained. Recovery MUST reconcile the same operation and canonical receipt, without inventing success or issuing a different publication as cleanup.
- **FR-012**: Rejection release MUST require durable terminal authority for the exact preparation, scope and owner generation. Retriable failure, elapsed time, request cancellation or missing acknowledgment alone is not terminal rejection.
- **FR-013**: Release MUST be idempotent and resumable after interruption, preserving all unrelated roots, including those sharing its material.
- **FR-014**: Stale creation, handover or release MUST NOT resurrect a terminally released preparation after collection or change a newer generation. Terminal replay protection MUST survive ordinary collection.
- **FR-015**: Collection MUST protect preparation's complete closure and verify current roots before deletion. Unprovable identity, authority or closure MUST retain material and expose uncertainty.
- **FR-016**: Observable outcomes MUST distinguish confirmed retention, refusal/conflict, unknown publication, terminal decision, incomplete cleanup and confirmed release, correlated with their operation/generation, exact selection and canonical authority.
- **FR-017**: Supported durable compositions MUST preserve these behaviors through their actual material ownership boundaries. Unsupported compositions, including mixed or decorated compositions that cannot prove the complete boundary, MUST refuse explicitly; process-local evidence cannot establish cold-restart durability.

### Key Entities *(include if feature involves data)*

- **Preparation retention**: Runtime's durable protection of one authorized operation/generation's exact reviewed selection, without execution or export authority.
- **Reviewed selection and closure**: The selected roots, exact content identities and all required transitive native, model and reusable-template material, including their actual physical incarnations at acceptance.
- **Canonical operation authority**: The scope-bound owner/generation and durable publication receipt or terminal decision authorizing handover or release; cleanup progress is a separate fact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every supported durable composition, 100% of selected native/model/template closure members retain their exact identities and content after a cold restart and two collection sweeps beyond configured staging grace, with no publication, test run or execution supplying retention. Covers FR-001–FR-007, FR-015 and FR-017; User Story 1.
- **SC-002**: Every applicable execution, dispatch, activation and runnable export/import attempt based solely on preparation authority is refused with zero runnable effects; independent valid authority over the same material still works. Covers FR-008–FR-009; User Story 1.
- **SC-003**: Publication commit/acknowledgment and handover interruptions preserve continuous retention; recovery identifies the same canonical publication with zero duplicate effects. Covers FR-010–FR-011 and FR-016; User Story 2.
- **SC-004**: Repeated terminal release and interrupted-cleanup recovery converge, leave unrelated roots unchanged, and make otherwise unreferenced material collectible. Covers FR-012–FR-013 and FR-016; User Story 3.
- **SC-005**: Stale-owner, changed-selection, wrong-scope, material-replacement and post-release replay controls change zero accepted or newer-generation preparations; no terminal preparation is resurrected after collection. Unsupported/corrupt/unavailable boundaries never report complete acceptance or false absence. Covers FR-002–FR-006 and FR-014–FR-017; User Stories 1 and 3.

## Assumptions

- This is the narrow Runtime prerequisite under #2892. Workflow and Agent publication consume its eventual verified behavior through their existing publication ownership; this spec does not deliver or accept T066, #2912, the wider program, or a new #3804 gate.
- Existing canonical [source/publication meanings](../../docs/glossary/elsa.md) and domain ownership remain authoritative. Preparation preserves the reviewed selection; it never silently substitutes a current draft or recompiles different material during recovery.
- Retention scope is the material actually selected for the candidate and its required dependencies. Provenance alone does not invent additional installed material. Existing published artifacts, model semantics and template identity remain unchanged.
- Review, compilation, canonical publication, terminal decision and retention cleanup remain distinct facts. Separate persistence owners may be independently unavailable; this feature does not assume one transaction spans them.
- Broader publication orchestration, UI, delivery-source policy, Message/C2 policy, new execution engines, another model store and deployment/reset behavior are outside this slice. An enum-only change, disguised Published/TestRun reference or increased grace period cannot satisfy it.
