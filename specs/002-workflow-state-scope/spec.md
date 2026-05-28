# Feature Specification: WorkflowDefinitionState Scope Policy

**Feature Branch**: `002-workflow-state-scope`
**Created**: 2026-05-28
**Status**: Draft
**Input**: User description (Unit C — Elsa entity-design refactor): "WorkflowDefinitionState scope policy — codify what the canonical authored document MUST contain and what it MUST NOT absorb, ratify the architectural triplet around it, and fold in the supporting refinements (designer-metadata sibling entity, NodeId rename, activity catalog reference via ActivityVersionId)."

---

## Context

`WorkflowDefinitionState` is persisted as the `StateSource` shadow JSON on `WorkflowDefinitionVersion` (immutable) and `WorkflowDefinitionDraft` (mutable) inside the `Workflows.Design` sub-domain. It is the canonical authored document of a workflow.

Sipke's 2026-05-26 entity-design review (item 2) flagged a god-object risk: as the entity-design Units B–G crystallize, `WorkflowDefinitionState` will be the natural dumping ground for any workflow-related concern unless its scope is pinned now. This feature pins it.

Constitutional substrate: Elsa v2.0.0 (draft) + framework v2.0.0 (draft), folded 2026-05-27 in Unit A. Unit C is parallel-safe with Units B (activity catalog identity) and D (parent vs version field allocation).

The audience for this spec is the architecture group (Joey + Sipke + Frans) and the AI agents implementing against the constitution. Stakeholder framing is architect-grade, not end-user.

---

## Clarifications

### Session 2026-05-28

- Q: NodeId stability across Draft → Version promotion — do nodes retain their NodeIds across promotion, are NodeIds freshly minted, or are they stable across the whole Version chain? → A: NodeIds carry across Draft → Version promotion; uniqueness is scoped to the owning Version/Draft instance, not enforced across the Version chain. Promotion is a straight copy of State and of `WorkflowDefinitionDesignMetaData` rows. Cross-Version NodeId equality is incidental (because promotion copies), not a constitutional invariant.
- Q: Design-metadata immutability when parent is immutable — do `WorkflowDefinitionDesignMetaData` rows mirror their parent's mutability rules? → A: Yes. Version-tied rows are immutable (enforced consistently with the Version's `[Immutable]` regime); Draft-tied rows remain mutable. Re-laying out an already-promoted Version requires minting a new Version. Designer layout is treated as part of authoring, not as a mutable side-channel.
- Q: Unit B coupling — does Unit C wait for, or proceed independently of, Unit B's catalog-side `ActivityVersionId`? → A: Independent. Unit C declares `ActivityNode.ActivityVersionId : string` now; the value written into it follows Unit B's emerging format convention (per Joey 2026-05-28: stable). No constitutional coupling between units; small mechanical handshake on the string content. No shared contract type is introduced as part of Unit C.
- Q: Dual-parent FK shape on the new design-metadata entity — one entity with two nullable FKs (XOR), two distinct entity types, or a TPH/TPT hierarchy? → A: Two distinct entity types — `WorkflowDefinitionVersionDesignMetaData` (immutable, FK to `WorkflowDefinitionVersion`) and `WorkflowDefinitionDraftDesignMetaData` (mutable, FK to `WorkflowDefinitionDraft`), sharing the read contract `IWorkflowDefinitionDesignMetaData` in `Elsa.Workflows.Design.Core`. Each entity carries its own `[Immutable]` regime mirroring its parent; no XOR invariant or discriminator column is introduced.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Codify the scope and protect it with a test (Priority: P1)

The architecture group ratifies *what `WorkflowDefinitionState` is for* and *what it is not for*. The rule is recorded in the Elsa constitution; the `WorkflowDefinitionState` record carries a documentation header pointing at the rule; an automated test fails if a forbidden content category surfaces inside `WorkflowDefinitionState`.

**Why this priority**: The constitutional codification is the load-bearing deliverable of Unit C. Without it, Units D/E/F/G have no shared substrate for "what stays in State vs. what splits off." Stories 2 and 3 deliver structural support but make no sense without the rule they support.

**Independent Test**: Land the constitution amendment, add the documentation header on the `WorkflowDefinitionState` record, add the scope-policy unit test; verify the test passes against today's State (which appears clean against the policy) and fails when any forbidden-category type is injected into the State graph.

**Acceptance Scenarios**:

1. **Given** the Elsa constitution at v2.0.0 (draft), **When** Unit C's amendment is folded, **Then** a new §E2.X exists that codifies the in-State / out-of-State scope and ratifies the architectural triplet (`WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`), with cross-references from §E2.2 (Design ↔ Runtime split) and §E2.6 (artifact-only runtime).
2. **Given** the codified scope, **When** a future change adds a forbidden-category type (instance-state, executable-metadata, publication-state, read-model projection, design-metadata record) into the transitive shape of `WorkflowDefinitionState`, **Then** the scope-policy unit test fails with a diagnostic naming the offending type and the forbidden category.
3. **Given** the codified scope, **When** the `WorkflowDefinitionState` record is read by any future contributor, **Then** they find a documentation header that quotes the in-State / out-of-State boundary and points at the constitution §E2.X.

---

### User Story 2 — Extract designer layout into a sibling entity (Priority: P2)

Designer layout (positions, sizes, canvas metadata) is removed from any path that could land inside `WorkflowDefinitionState`, and is owned by two normalized sibling entities — `WorkflowDefinitionVersionDesignMetaData` (FK to Version, immutable) and `WorkflowDefinitionDraftDesignMetaData` (FK to Draft, mutable) — each holding a normalized list of design-metadata records keyed by NodeId. A unified getter-only read contract `IWorkflowDefinitionDesignMetaData` lives in `Elsa.Workflows.Design.Core`; both entities live in `Elsa.Workflows.Design.Persistence.Core`; EF Core mappings live in the existing EFCore project.

**Why this priority**: Designer layout is the single largest concrete chunk of "could-be-creep" content Sipke called out as conditional ("when genuinely part of authoring"). Joey's 2026-05-19 pre-meeting position (entity-design follow-up Section D, Q4) and today's confirmation lock the sibling-entity approach. Without this, Story 1's policy is correct in the abstract but immediately violated the moment designer layout is implemented.

**Independent Test**: Independently from Story 1, designer layout can be authored against the sibling entity, persisted, retrieved, and rendered by the designer — without `WorkflowDefinitionState` itself growing any layout-typed members. The sibling entity's normalized list of metadata records keyed by NodeId can be inspected for any given activity node without traversing `WorkflowDefinitionState`.

**Acceptance Scenarios**:

1. **Given** a `WorkflowDefinitionVersion` is persisted, **When** a designer client writes layout metadata for its placed activity nodes, **Then** the metadata lands in a `WorkflowDefinitionVersionDesignMetaData` row FK'd to that Version (or, for Drafts, a `WorkflowDefinitionDraftDesignMetaData` row FK'd to the Draft), holding a normalized list of design-metadata records each keyed by a NodeId belonging to the parent's activity graph.
2. **Given** the sibling entity is in place, **When** a runtime or projection consumer loads only `WorkflowDefinitionState`, **Then** no design-metadata records are reachable through it (the runtime and read-side consumers stay layout-oblivious).
3. **Given** the read contract `IWorkflowDefinitionDesignMetaData` lives in `*.Design.Core`, **When** a design-time consumer references only `*.Design.Core`, **Then** it can read design metadata via the contract without depending on `*.Persistence.Core`.

---

### User Story 3 — Unify NodeId terminology and collapse the catalog reference (Priority: P2)

`ActivityNode.ReferenceKey` is renamed to `NodeId`, and `ActivityPortConnection.ActivityReferenceKey` is renamed to a NodeId-named property (final name `NodeId` or `ActivityNodeId` settled at plan stage), so the join key used by connections, by the new `WorkflowDefinitionDesignMetaData`, and by any future per-node sibling is consistently named. Separately, today's `(activityDefinitionId : string, version : int)` pair carried on `ActivityNode` is collapsed into a single `ActivityVersionId : string` — the stable catalog reference owned by Unit B.

Argument-level `ReferenceKey` (on `ArgumentDefinition`, `InputDefinition`, `OutputDefinition`) and `VariableDefinition.ReferenceKey` are different semantics and stay untouched.

**Why this priority**: Both refinements are terminology / shape changes that *support* Story 1's scope policy and Story 2's sibling entity — without them, the sibling entity cannot cleanly key into State's nodes, and the catalog-reference shape would still imply Unit B's prior pair-encoding. They are sequenced after Story 1 (which establishes the policy) and concurrent with Story 2 (which depends on the NodeId).

**Independent Test**: Story 3 is verifiable as a rename + collapse refactor. After the change, no occurrence of `ActivityNode.ReferenceKey`, `ActivityPortConnection.ActivityReferenceKey`, or the old `(activityDefinitionId, version)` pair remains in `Elsa.Workflows.Design.Core` and adjacent design models; argument/variable `ReferenceKey` identifiers are unchanged; existing tests on the affected paths continue to succeed (framework §2.21.1, golden rule of refactoring).

**Acceptance Scenarios**:

1. **Given** today's codebase, **When** the rename lands, **Then** `ActivityNode.ReferenceKey` is `NodeId`, `ActivityPortConnection.ActivityReferenceKey` is renamed to a NodeId-named property, all direct consumers (mappers, JSON converters, mediator handlers) are updated, and argument/variable-level `ReferenceKey` identifiers are unchanged.
2. **Given** today's `ActivityNode` carries `(activityDefinitionId, version)`, **When** the collapse lands, **Then** a single `ActivityVersionId : string` replaces the pair on `ActivityNode` and on every adjacent design-side model that referenced the pair; the concrete identifier value is supplied by Unit B's catalog identity rules.
3. **Given** existing unit tests on the renamed/collapsed paths, **When** the refactor is complete, **Then** those tests continue to succeed unchanged in subject and objective per framework §2.21.1; setup/wiring may have changed but no test is deleted without recorded architect approval.

---

### Edge Cases

- **A design-metadata record references a NodeId that no longer exists in `WorkflowDefinitionState`.** Orphaned records are tolerated transiently during edits on the Draft-side entity; eventual cleanup is a draft-save-side concern. The Version-side entity is immutable, so this case does not arise post-promotion. The runtime/projection side is never affected because it does not load design metadata.
- **A design-metadata row exists but its parent `WorkflowDefinitionVersion`/`WorkflowDefinitionDraft` was deleted.** Treated as orphan; cleanup is owned by the persistence-side cascade configuration on each of the two entities, not by `WorkflowDefinitionState`.
- **A future contributor tries to nest layout-style fields back into `ActivityNode` (e.g. position, size).** Caught by the scope-policy test as a forbidden-category surface inside `WorkflowDefinitionState`; rejected at PR time, not at runtime.
- **A consumer that today reads `(activityDefinitionId, version)` from `ActivityNode` must keep working through the collapse.** Covered by framework §2.21.1: the existing tests' subjects/objectives are preserved across the rename; consumer-side mapping changes accordingly.
- **A property newly proposed for `WorkflowDefinitionState` whose category is genuinely ambiguous between "authored content" and "projection / catalog / build metadata".** Surfaces as an architecture-meeting escalation; resolution is constitutional (amend §E2.X), not silent.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Elsa constitution MUST gain a new §E2.X codifying (a) the in-State / out-of-State scope of `WorkflowDefinitionState`, and (b) the architectural triplet `WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`. The new section MUST be cross-referenced from §E2.2 (Design ↔ Runtime split) and §E2.6 (artifact-only runtime).
- **FR-002**: The new §E2.X MUST land either as a v2.1.0 amendment against the v2.0.0 (draft) substrate, or as an in-flight addition folded into v2.0.0 ratification, depending on the Unit A ratification timeline.
- **FR-003**: `WorkflowDefinitionState` MUST carry a documentation header (XML doc on the record) that quotes the in-State / out-of-State boundary and references the constitution §E2.X.
- **FR-004**: A unit test on `WorkflowDefinitionState` MUST fail if a forbidden content category surfaces inside its transitive type graph. Forbidden categories include: instance/runtime/operational state, executable/build metadata, publication/deployment state, search/listing-projection types, security/ownership types, and design-metadata records (which belong on the sibling entity per FR-006).
- **FR-005**: The audit MUST confirm that today's `WorkflowDefinitionState` (carrying `Variables`, `ActivityConnections`, `Activities`, `Inputs`, `Outputs`, `WorkflowActivityOptions`, `StrategyOptions`) is clean against the policy. Any creep discovered MUST be extracted to its proper home and recorded in the Unit C follow-up.
- **FR-006**: Two new persistence entities MUST live in `Elsa.Workflows.Design.Persistence.Core` — `WorkflowDefinitionVersionDesignMetaData` (FK to `WorkflowDefinitionVersion`) and `WorkflowDefinitionDraftDesignMetaData` (FK to `WorkflowDefinitionDraft`). Each entity MUST carry a normalized list of design-metadata records keyed by NodeId. Design metadata MUST NOT be nested inside `ActivityNode` and MUST NOT be reachable through `WorkflowDefinitionState`.
- **FR-006a**: `WorkflowDefinitionVersionDesignMetaData` MUST be immutable (enforced consistently with `WorkflowDefinitionVersion`'s `[Immutable]` regime — `PropertySaveBehavior.Throw` plus the `SaveChangesAsync` guard). `WorkflowDefinitionDraftDesignMetaData` MUST remain mutable. Re-laying out an already-promoted Version's canvas MUST require minting a new Version; layout is part of authoring, not a mutable side-channel. No XOR invariant or discriminator column is introduced — the two entity types make the regime statically distinguishable.
- **FR-007**: A getter-only read contract `IWorkflowDefinitionDesignMetaData` MUST live in `Elsa.Workflows.Design.Core` as the unified design-time read surface over both persistence entities, allowing design-time consumers to read design metadata without depending on `*.Persistence.Core` and without branching on which parent type they're working against (matches today's Tier-1 read-contract pattern per `2026-05-24_ENTITY_DESIGN_SUMMARY_JOEY.md` §3.5).
- **FR-008**: EF Core mappings for both `WorkflowDefinitionVersionDesignMetaData` and `WorkflowDefinitionDraftDesignMetaData` MUST live in `Elsa.Workflows.Design.Persistence.EFCore`, following the existing entity-handler / loading-handler conventions for the design domain.
- **FR-009**: `ActivityNode.ReferenceKey` MUST be renamed to `NodeId`. `ActivityPortConnection.ActivityReferenceKey` MUST be renamed to a NodeId-named property (final name `NodeId` or `ActivityNodeId` resolved at plan stage). All direct consumers (mappers, JSON converters, mediator handlers, EF configurations) MUST be updated.
- **FR-009a**: `NodeId` uniqueness MUST be scoped to the owning `WorkflowDefinitionVersion` or `WorkflowDefinitionDraft` instance. NodeIds MUST carry across Draft → Version promotion (promotion is a straight copy of `WorkflowDefinitionState` and of the associated `WorkflowDefinitionDesignMetaData` rows). Cross-Version NodeId equality is a consequence of copy-based promotion, not a constitutional invariant; NodeIds across separate Versions (or across separate Definitions) MUST NOT be assumed equal by any consumer.
- **FR-010**: Argument-level `ReferenceKey` identifiers (on `ArgumentDefinition`, `InputDefinition`, `OutputDefinition`) and `VariableDefinition.ReferenceKey` MUST NOT be renamed in this unit. They carry distinct semantics (join key for filled-in argument state back to argument definitions) and are outside Unit C's scope.
- **FR-011**: Today's `(activityDefinitionId : string, version : int)` pair carried by `ActivityNode` (and adjacent design-side models) MUST be collapsed into a single `ActivityVersionId : string` — the stable catalog reference owned by Unit B. The concrete identifier value and shape are defined by Unit B's `ActivityDefinitionVersion` identity rules.
- **FR-011a**: Unit C MUST declare and roll out the `ActivityVersionId : string` field independently of Unit B's branch landing. The string value written into the field at serialization time MUST follow Unit B's emerging format convention; format alignment is a small mechanical handshake, not a structural dependency. No shared contract type (value object, marker interface) MUST be introduced into `Elsa.Activities.Design.Core` as part of Unit C to mediate the reference; the `string` typing is the seam.
- **FR-012**: The refactor MUST observe framework §2.21.1 (golden rule of refactoring): existing tests on the affected implementations continue to succeed; subjects/objectives are preserved; test deletion requires recorded architect approval per §2.21.1.
- **FR-013**: A Unit C follow-up file MUST be registered at `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_unitC_workflow_definition_state_scope.md` per meta-repo `CLAUDE.md` §5, with the required sections (Context, Priority, Status, Scope, Open questions, Pre-thinking input, Linked actions) plus constitution / code / doc checklists.
- **FR-014**: `PERSONAL_TODO.md` MUST be updated to reflect Unit C's status as it progresses.

### Key Entities

- **`WorkflowDefinitionState`** (existing record, `Elsa.Workflows.Design.Core.Models`): the canonical authored document. After Unit C: a documented, scope-asserted record. Members unchanged unless audit (FR-005) extracts creep.
- **`WorkflowDefinitionVersionDesignMetaData`** (new entity, `Elsa.Workflows.Design.Persistence.Core`): sibling of `WorkflowDefinitionVersion`. Owns a normalized list of design-metadata records keyed by NodeId. FK to the owning Version. Immutable, mirroring its parent's `[Immutable]` regime. Not nested inside, and not reachable through, `WorkflowDefinitionState`.
- **`WorkflowDefinitionDraftDesignMetaData`** (new entity, `Elsa.Workflows.Design.Persistence.Core`): sibling of `WorkflowDefinitionDraft`. Owns a normalized list of design-metadata records keyed by NodeId. FK to the owning Draft. Mutable, mirroring its parent. Not nested inside, and not reachable through, `WorkflowDefinitionState`.
- **`IWorkflowDefinitionDesignMetaData`** (new read contract, `Elsa.Workflows.Design.Core`): getter-only design-time read contract — the unified read surface over both persistence entities. Lets design-time consumers read design metadata without depending on `*.Persistence.Core` and without branching on the parent type.
- **`ActivityNode`** (existing, `Elsa.Workflows.Design.Core.Models`): the placed-activity node on the design canvas. Carries the workflow-internal identity `NodeId` (renamed from `ReferenceKey`; unique within the owning Version/Draft; carries across Draft → Version promotion as a copy) and a single `ActivityVersionId : string` (collapsed from the prior `activityDefinitionId + version` pair). Configured-properties and per-node argument state remain on or under `ActivityNode` per existing shape.
- **`ActivityPortConnection`** (existing, `Elsa.Workflows.Design.Core.Models`): the edge in the activity graph. Renames the join key carrying the source/target activity-node identifier to a NodeId-named property.
- **`WorkflowExecutable`** (named in the triplet; substance owned by Units E/G; not produced by Unit C): the compiled runtime artifact derived from an immutable workflow version. Named here so the constitutional triplet is complete; its concrete shape is downstream.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After Unit C lands, the Elsa constitution has a new §E2.X codifying the `WorkflowDefinitionState` scope policy and the architectural triplet; the section is ratified (or queued as `(draft)` against the next ratification) by Joey + Sipke + Frans.
- **SC-002**: `WorkflowDefinitionState` carries an in-source documentation header that quotes the scope policy; a new contributor reading the record can identify the in-State / out-of-State boundary without leaving the file.
- **SC-003**: A scope-policy unit test exists. Injecting an exemplar forbidden-category type into the transitive shape of `WorkflowDefinitionState` causes the test to fail with a diagnostic naming the offending type and the violated category. Removing the injection restores the green build.
- **SC-004**: Designer layout for every activity node on a `WorkflowDefinitionVersion` is reachable via `WorkflowDefinitionVersionDesignMetaData`'s normalized list keyed by NodeId; for a `WorkflowDefinitionDraft`, via `WorkflowDefinitionDraftDesignMetaData`'s. Loading `WorkflowDefinitionState` alone (from either parent) returns zero design-metadata records.
- **SC-005**: After the rename, zero occurrences of `ActivityNode.ReferenceKey` or `ActivityPortConnection.ActivityReferenceKey` remain in the Workflows.Design tree; argument-level and variable-level `ReferenceKey` identifiers are unchanged.
- **SC-006**: After the collapse, zero occurrences of the old `(activityDefinitionId, version)` pair remain on `ActivityNode` and adjacent design-side models; a single `ActivityVersionId : string` replaces the pair.
- **SC-007**: All existing unit tests on the affected paths pass after the refactor without subject/objective change (framework §2.21.1). Any test deletion is justified and recorded per §2.21.1.
- **SC-008**: Unit C follow-up file exists at the documented path with the required sections plus constitution / code / doc checklists; `PERSONAL_TODO.md` reflects Unit C's status.

---

## Assumptions

- **Unit B's `ActivityVersionId` shape is stable.** Confirmed by Joey 2026-05-28: a single `string` identifier for the catalog reference. Unit C consumes this shape as a hard input and does not invent its own. Sequencing is independent (per Clarifications 2026-05-28 Q3): Unit C lands its rollout without waiting on Unit B's branch merge; the format-of-string handshake is mechanical.
- **Unit D does not block Unit C.** Parent / version / draft field allocation (e.g. where `Name`, `Description`, `MetaData` live) is independent of State scope. Unit D may move fields between `WorkflowDefinition` and `WorkflowDefinitionVersion` without affecting Unit C's deliverables.
- **`WorkflowExecutable` is named, not built, by Unit C.** Naming the triplet is constitutional; the executable's concrete shape, build pipeline, and runtime contract are owned by Units E/G.
- **EF Core remains the in-foundation default persistence provider.** Both design-metadata entities' mappings land in `Elsa.Workflows.Design.Persistence.EFCore`. A non-EF provider would supply its own mappings for the same Persistence.Core entities; consistent with framework §2.9.
- **Designer layout granularity.** "Design metadata records keyed by NodeId" is interpreted as: at minimum, one record per placed activity node; additional record kinds (e.g. workflow-level canvas settings) may exist as additional rows or as a separate workflow-level singleton on the same entity. Concrete schema is finalised during plan.
- **Cascade behaviour for the design-metadata entities.** When a `WorkflowDefinitionVersion` is deleted (rare; "versions are never deleted" per the in-flight Q3 stance in `2026-05-08_entity_design.md`), its `WorkflowDefinitionVersionDesignMetaData` row cascades. When a `WorkflowDefinitionDraft` is deleted, its `WorkflowDefinitionDraftDesignMetaData` row cascades. When an `ActivityNode` is removed from State, the corresponding design-metadata records become orphans tolerated transiently on the Draft-side and cleaned on the next save; on the Version-side (immutable) this case does not arise.
- **The constitutional amendment lands as v2.1.0 (against ratified v2.0.0)** unless Unit A's ratification timing makes folding into v2.0.0's first ratified release strictly cheaper. The decision is operational, not architectural.

---

## Constitutional Compliance

This spec is implemented against the two-layer constitution at `.specify/memory/constitution.md` (Elsa v2.0.0 draft) and `.specify/memory/constitution-framework.md` (framework v2.0.0 draft). The full Constitution Check (gates G1–G30) is enforced at the plan stage and not duplicated here.

Constitutional concerns surfaced by this spec:

- **Originates a new constitutional rule** (FR-001, FR-002): the Elsa §E2.X scope policy + architectural triplet does not yet exist in v2.0.0; this unit is its source. Expected outcome: a v2.1.0 (or in-flight v2.0.0) amendment lands alongside the code work.
- **Reinforces existing rules**: §E2.2 (Workflows.Design ↔ Workflows.Runtime hard rule) and §E2.6 (artifact-only runtime, Rule A + Rule B promotion) are cross-referenced but not modified.
- **Refactor obligations**: FR-009, FR-011, FR-012 invoke framework §2.21.1 (golden rule of refactoring) — existing tests' subjects and objectives are preserved; deletions require architect approval. Unit-test obligations under framework §2.23 apply to any new logic-bearing classes produced (e.g. the scope-policy test itself; EF Core entity-handlers for `WorkflowDefinitionDesignMetaData`).
- **Tier separation** (framework §2.1 + entity-design summary §4.2): `IWorkflowDefinitionDesignMetaData` (Tier 1, `*.Design.Core`); `WorkflowDefinitionVersionDesignMetaData` + `WorkflowDefinitionDraftDesignMetaData` (Tier 2, `*.Design.Persistence.Core`); EF Core mappings (Tier 3, `*.Design.Persistence.EFCore`). No tier inversion.
- **No §2.5 (inheritance) or §2.6 (cross-feature composition) novelty.** The two new entities follow the existing pattern of `WorkflowDefinitionVersion` and `WorkflowDefinitionDraft` as parallel parent-tied entities sharing a `*.Design.Core` read contract.

No flags trigger an immediate architecture-meeting escalation; the work proceeds against the existing rules with the §E2.X amendment as a deliberate constitutional output.
