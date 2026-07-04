# Elsa Constitution Draft History

Status: extracted from `.specify/memory/constitution.md` during Constitution Thinning v1.

This file preserves raw Elsa constitution provenance. It is historical report material, not the enforceable gate surface. Use [constitution-draft-history.md](constitution-draft-history.md) as the curated entrypoint.

~~~markdown
<!--
Sync Impact Report — Elsa Workflow Engine Constitution
========================================================

Version change: 3.1.0 (draft) → 3.2.0 (draft)
Date: 2026-07-04
Unit: W14 naming pass (Elsa 4 architecture review 2026-07, review-naming.md
  findings NM-1..NM-14, rules R1–R8).
  SemVer: MINOR — new section §E6 added; no existing rule removed or redefined
  backward-incompatibly.

§E6 (new) — Type-naming rules. Codifies the review's mechanical type-name
  style guide (R1–R8) as an enforceable Elsa specialization of framework §2.2:
  - R1 component budget (≤4, hard cap 5); R2 no namespace repetition in type
    names; R3 banned vague words (Manager/Helper/Util/Info/Data/Object/
    Service/Processor) with the external-contract exception; R4 codified role
    suffixes (Source/Contributor/Pre-/PostProcessor/Validator/Store/Provider/
    Factory/Resolver/Registry/Executor/Runner/Router/Dispatcher/Orchestrator/
    Coordinator/Handler); R5 concrete nouns over infra metaphors unless
    glossary-documented; R6 one concept one head-noun; R7 default-impl prefixes
    are fixed and good; R8 reserve `Agent` for the AI domain.
  - Protected-name list (NM-14) and protect-these strengths (NM-13) recorded.
  - Wire/persisted-identifier carve-out made explicit: behavior preservation
    outranks naming; literal wire values are preserved and commented, never
    renamed to satisfy R1–R8.

Applied by W14 rename families A–E (behavior-preserving type/file renames):
  A drain/dispatch verbs (Coordinator→Orchestrator, CommandProcessor→Executor,
  SchedulerCommandProcessor→CommandRouter, StartDispatcher); B …State facets
  (OperationalState→ExecutionLivenessState, ControlPlaneState→WorkflowHoldState);
  D vague words (AuthenticationProviderManager→Resolver, *VersionInfo→Summary,
  AgentStepInfo→Descriptor, LogExceptionInfo→Details); E Agent→Actor; C
  opportunistic exception shortenings. WorkflowSchedulerDrainer,
  ParentCompletionSchedulerWorkHandler, and all persisted wire strings
  deliberately preserved. ISecretManager rename deferred to W18 (target name
  collision — see docs/program-goals/elsa-4-review-remediation.md).

Ratification status unchanged: draft, pending Joey Barten, Sipke Schoorstra,
Frans van Ek.
-->
~~~

~~~markdown
<!--
Sync Impact Report — Elsa Workflow Engine Constitution
========================================================

Version change: 3.0.0 (draft) → 3.1.0 (draft)
Date: 2026-07-02
Unit: W6 repo hygiene (Elsa 4 architecture review 2026-07, findings MD-2 + MD-4).
  SemVer: MINOR — pinned domain tree materially refreshed; a documented
  exception added to the §E2.2 hard rule. No rule removed or redefined
  backward-incompatibly.

MD-4 — §E2.1 domain tree refreshed to match the shipped tree:
  - Removed domains with no code: `Elsa.Scheduling`, `Elsa.Messaging`,
    `Elsa.Notifications` (the latter shipped as `Elsa.Events` under the
    framework §2.6 unified event model).
  - Added real domains previously absent: `Elsa.Activities`, `Elsa.Agent`,
    `Elsa.Caching`, `Elsa.Diagnostics`, `Elsa.Events`, `Elsa.Foundation`,
    `Elsa.Mediator`, `Elsa.Pipelines`, `Elsa.Primitives`, `Elsa.Secrets`,
    `Elsa.Workflows.Publishing`, `Elsa.Workflows.Primitives`, `Elsa3`.
  - Corrected stale surface-package listings (e.g. `Elsa.Workflows.Runtime.Core`
    no longer "(stub)"; `Elsa.Workflows.Runtime.StorageDrivers` does not exist).
  - Table now points to the generated docs/maps/domain-map.md as the
    always-fresh enumeration.
  - §E2.1 naming example `Elsa.Scheduling.Quartz` replaced with the real
    `Elsa.Locking.FileSystem`.

MD-2 — §E2.2 hard rule (no Runtime→Design dependency) now records the single
  tracked allow-list exception `Elsa.Workflows.Runtime.JavaScript →
  Elsa.Workflows.Design.Core` (ArchitectureGuardTests.DeferredRuntimeDesignReferences;
  runtime-execution-seam program goal) and requires the same treatment for any
  future exception. §E2.2.2's flat "Runtime does not reference Design.Core"
  statement qualified accordingly; its stale stub package list refreshed.

Ratification status unchanged: draft, pending Joey Barten, Sipke Schoorstra,
Frans van Ek.
-->
~~~

~~~markdown
<!--
Sync Impact Report — Elsa Workflow Engine Constitution
========================================================

Version change: 2.0.0 (draft, never ratified) → 3.0.0 (draft)
Derives from: framework constitution v3.0.0 (was v2.0.0).
  SemVer: MAJOR.
  Rationale: pairs with framework v3.0.0 MAJOR (Unit 1 — three in-process
  pub/sub concepts collapsed into one `IEvent`; exception-shielding-by-default
  reversed; event handling moved to its own `Elsa.Events.Core` / `Elsa.Events`
  library family). Elsa §E3.x worked examples realigned: §E3.3
  (`OnJsonPayloadConvertersInitializing` now `IEvent`, published Sequential),
  §E3.7 (JS declaration/binding events as Sequential contribution), §E3.9
  (`IEventPublisher.Publish` in the sync-over-async note), §E3.10 (validators).
  Any plan/spec/Elsa-section citing `IDomainEvent` / `INotification` /
  `ILifecycleEvent` must rewrite to `IEvent`.

  Unit 1 addendum (2026-06-02, folded into 3.0.0 draft): pairs with framework
  §2.6.1's contributor-interface + single-handler revert. §E3.3 rewritten
  (`IJsonConverterSource` returns; one `RegisterJsonConverters` handler
  aggregates; event exposes a directly-accessible `ICollection<JsonConverter>`).
  §E3.7 rewritten: the declarations cluster keeps the `Contributor` kind
  (`IJavaScriptDeclarationContributor` receives the rich
  `IJavaScriptDeclarationsContributionContext` — renamed from the old
  `IJavaScriptRenderingContext` — and adds declarations; one
  `BuildDeclarationsDocument` handler). The script-evaluation cluster moves to
  the **PreProcessor / PostProcessor** kind because `OnEvaluatingScript` /
  `OnScriptEvaluated` are a before/after pair: `IScriptPreProcessor` runs at
  `OnEvaluatingScript` (one `PreProcessScript` handler) and `IScriptPostProcessor`
  runs at `OnScriptEvaluated` (one `PostProcessScript` handler; the existing
  variable copy-back is the canonical post-processor). The Jint evaluator now
  publishes `OnScriptEvaluated` after evaluation. §E3.8 + §E3.10 rewritten
  (validators are `IDraftValidator` contributor impls, not per-feature
  `IEventHandler<OnDraftValidating>`; the single `ExecuteValidations` handler
  aggregates). "Source" (returns) vs "Contributor" (receives context + acts) vs
  "PreProcessor/PostProcessor" (acts on a lifecycle context, bound to an
  OnXxxing/OnXxxed pair) naming pinned framework-side.

  [Prior bump] 1.0.0 → 2.0.0: paired with framework v2.0.0 (§2.6 family
  restructured; §E3.3 reshaped to the Registry + StartUp Task sub-pattern).

v2.0.0 provenance — consolidated fold of:
  1. The v1.1.0 amendment plan drafted 2026-05-19 (never folded as v1.1.0).
  2. Sipke's 2026-05-26 architectural-clarification items 4, 5, 12 (from
     `2026-05-26_ENTITY_DESIGN_RESPONSE_SIPKE.md`). Items 1, 2, 3, 6, 7, 8, 9,
     10, 11, 13 are entity-design substance, deferred to Units B–G.
  3. Matured candidate rules: Rule A (executable-always-runs) + Rule B
     (artifact-only runtime) → §E2.6; CR-COMPAT (reframed as Elsa 3
     import-only via `Elsa3.Workflows.Import` adapter) → §E2.7.

Unit B amendment (2026-05-28, draft pending ratification):
  - §E2.8 NEW — "Activity catalog is the single source of truth for picker
    visibility." Codifies Sipke item 7. Pins: picker presence = catalog
    presence; no live-provider enumeration; `IsBrowsable` field removed;
    context-aware visibility (tenant/role/feature-flag) deferred to a
    separate policy layer. Read-contract surface pinned by reflection test
    in `Elsa.Activities.Design.Tests/Unit/ReadContractSurfaceTests.cs`.
    *(Originally pinned with a `¬RemovedAt` clause + operational sibling;
    superseded same-day by Unit C's Model X cascade — see "Unit C Model X
    cascade" entry below.)*
  - §E3.9 + framework §2.6.5 already landed (worked example for the sync
    contributor pattern); Unit B verified wording on 2026-05-28. No changes
    to either section.

Unit C Model X cascade (2026-05-28, draft pending 2026-06-01 ratification):
  - §E2.8 REVISED — "Removed surface" paragraph: `Visibility = catalog
    presence ∧ ¬reconciliation-state.RemovedAt` simplified to
    `Visibility = catalog presence`. "Operational state lives on a sibling"
    paragraph replaced by a "Reconciliation policy — Model X" paragraph
    that codifies: reconciliation is one-shot at creation; no operational
    sibling; immutable `ProvisioningHash` on `IActivityDefinitionVersion`;
    skip-or-throw duplicate path with hash safety net (mismatch → throw
    `ActivityVersionHashMismatchException`); source disappearance not
    tracked; versions never deleted. Status: provisional pending the
    2026-06-01 architecture review (agenda Items 1 + 2 in
    `2026-06-01_AGENDA_review_meeting.md`).
  - Cascade to Unit B's already-landed code (Joey's instruction: don't
    rewrite Unit B's spec; rewrite Unit B's code so both reconciliation
    surfaces are aligned under Model X):
      - Deleted: `IActivityDefinitionReconciliationState` read contract,
        `ActivityDefinitionReconciliationState` entity, its EF Core
        configuration, the DbSet on `ActivitiesDesignDbContext`, and
        the related stale-removal scaffolding.
      - Added: `ProvisioningHash` immutable property on
        `ActivityDefinitionVersion` (and on `IActivityDefinitionVersion`
        read contract); `ActivityVersionHashMismatchException` in
        `Elsa.Activities.Design.Reconciliation.Core`.
      - Rewritten: `ActivityVersionReconciler.cs` to Model X semantics;
        `ActivityDefinitionLookup.cs` query simplified to catalog
        membership (no LEFT JOIN, no removal filter).
      - Tests updated: `ReadContractSurfaceTests` (negative pin for the
        per-pass mutating field names: `LastSeenAt`, `LastProvisionedAt`,
        `LastProvisionedBy`, `SourceVersion`, `IsStale`, `RemovedAt`;
        positive pin for `ProvisioningHash` on `IActivityDefinitionVersion`);
        `PickerVisibilityTests` (removed `RemovedAt` scenario);
        `ReconciliationStateTests.cs` deleted (subject removed per
        framework §2.21.1 — recorded approval: Joey, as Unit C clarify
        session 2026-05-28); `FeatureRegistrationTests` + `CrossContextLifecycleTests`
        trimmed of sibling references.
      - SQLite migration regenerated fresh per Unit B's "no preserved
        production data" convention.

Unit C Phase-3 cascade (2026-05-28, draft pending 2026-06-01 ratification):
  [SUPERSEDED by the Unit 1 addendum 2026-06-02 — the intent-revealing-methods
  sub-rule was withdrawn; §E3.3 now uses `IJsonConverterSource` + the single
  `RegisterJsonConverters` handler + a directly-accessible `ICollection`.]
  - §E3.3 REWRITTEN under the new framework §2.6.1 intent-revealing-methods
    sub-rule. `OnJsonPayloadConvertersInitializing` is now a `sealed class`
    with `AddConverter(JsonConverter)` + `public IReadOnlyList<JsonConverter>
    Converters`. Earlier "payload carries `List<JsonConverter>`" wording
    superseded. Cross-references framework §2.6.1's new sub-pattern.

Unit C Phase-7 cascade (2026-05-28, draft pending 2026-06-01 ratification):
  - §E3.10 NEW worked example — "Three-segment secondary-domain naming with
    phase split — `Elsa.Http.Activities.<Phase>`." Sibling to §E3.8 (the
    two-segment `Elsa.Http.JavaScript` walkthrough). Documents the case where
    a model-owning domain (HTTP, Email, Slack, …) contributes activities to
    a consumer domain (Activities) that itself has a Design/Runtime phase
    split. The structure: `Elsa.<Model>.Activities.Design` for design-time
    contributions (+ co-located activity-specific validators per Unit C
    session-3 Q2) and `Elsa.<Model>.Activities.Runtime` for runtime
    activity execution. Section is additive — no existing rule conflicts;
    framework §2.2 (model-owning domain wins prefix) + §2.20 (no empty
    umbrella) + §E2.2 (Workflows-Design ↔ Runtime hard rule) already cover
    the underlying mechanics. Cross-referenced from Unit C Spec 002 FR-034.
    Status: provisional pending the 2026-06-01 architecture review meeting
    (agenda Item 6).

Unit 3 — Activity semantic versioning (2026-06-04, draft pending ratification):
  - §E2.8 REVISED — new "Activity versioning" sub-block. The activity version
    is an author-controlled **string semantic version (SemVer 2.0.0)**, no
    longer an engine-assigned `int`. The version is sourced from the declaring
    assembly's version and may be overridden per-activity by a `[Version("…")]`
    attribute (author owns it). A CLR assembly-scanning
    `IActivityReconciliationSource` (`Elsa.Activities.Design.Reconciliation.Clr`)
    reads the attribute / assembly version and supplies it as the version when
    the reconciler calls the source — fitting the Unit B DI-source pattern
    (framework §2.6.1). The CLR scanner reads no UI metadata; the only
    author-intent attribute it honours beyond `[Version]` is `[Required]` on
    inputs (→ `InputDefinition.IsRequired`).
  - §E2.8 reconciliation-policy paragraph reworded: the `(DefinitionId, Version)`
    lookup is **build-metadata-insensitive** — it matches on the normalised
    SemVer sort key (`SemVer.ToSortKey`, a zero-padded comparable form that
    excludes build metadata), so `1.0.0` and `1.0.0+build` are the same logical
    version. "Latest version" ordering sorts by the same sort key descending
    (release above prerelease). The integer-equality / integer-ordering wording
    is retired.
  - Term tidy: the immutable content hash is `ReconcilliationHash` (the entity's
    actual member; the older `ProvisioningHash` name is retired throughout §E2.8).
  - Module decomposition (framework §2.20 + §E3.10): the activity design domain
    is `Elsa.Activities.Design.*` and the runtime domain is
    `Elsa.Activities.Runtime.*`; the `[Version]` attribute and version-resolution
    contract live in the zero-dep `Elsa.Activities.*.Core` so authors annotate
    without taking a heavy dependency. Extends Unit B; prerequisite for Unit 4
    (workflow-as-activity version pinning). Migration: existing `int`-versioned
    rows are not preserved (SQLite regenerated fresh, per Unit B convention).

Added Elsa sections (relative to v1.0.0):
  - §E2.5 — Reinforced opening: "`ElsaDbContextBase` is shared EF-Core
    infrastructure, not a model/entity-design requirement." Cross-references
    framework §2.9's new persistence-invariants paragraph.
  - §E2.6 — NEW: "Runtime contract — executable-always-runs and artifact-only
    design." Two coupled invariants:
      - §E2.6.1 Executable-always-runs: if an artifact is published as runnable,
        the runtime MUST be able to load and execute it. Domain gates may deny
        execution; system failures violate the contract.
      - §E2.6.2 Artifact-only runtime: Runtime depends only on the runnable
        artifact + configured runtime features. Hard rule (cross-refs §E2.2):
        runtime never loads design-side data to execute.
    Promotes Rule A + Rule B from `follow-up-items/2026-05-08_entity_design.md`
    to constitutional content. Generic-framework analogues remain candidate
    rules (not yet promoted to framework).
  - §E2.7 — NEW: "Elsa 3 backward compatibility — import-only." Reframes the
    earlier CR-COMPAT-1/2/3 trio. Scope: `Elsa3.<Domain>.Import` adapter
    modules; one-way one-time mapping. Out of scope: dual-run, ongoing
    viewmodel mapping, round-trip translation.
  - §E3.3 — REWRITTEN: title "Provider contract" → "Domain-event contribution
    with sync access — `JsonConverter` registry." Shows the Registry +
    StartUp Task + DomainEvent pattern. Legacy `IPayloadSerializerConverterProvider`
    flagged as code-side migration item in Unit A follow-up.
  - §E3.5 — Status revision: untangling resolved 2026-05-19; section now
    serves as the dual-integration smell record + cross-reference hub for
    §E3.6/§E3.7/§E3.8.
  - §E3.6 — NEW: "Adapter pattern — `IJavaScriptExecutionContext` over Jint."
    Second worked example for framework §2.7 (alongside `Elsa.Locking.FileSystem`
    in §E3.2). Jint isolated to implementation feature; `.Core` is engine-free.
  - §E3.7 — NEW: "Design-time vs runtime contract split — JS function
    declarations vs functions." Worked example for framework §2.6.4. Uses the
    existing `OnDeclarationsDocumentGenerating` (design-time) and
    `OnEvaluatingScript` (runtime) domain events.
  - §E3.8 — NEW: "`Elsa.Http.JavaScript` — secondary-domain naming walkthrough."
  - §E3.9 — NEW: "Sync contributor pattern — `IEntityModelCreatingHandler`."
    Worked example for framework §2.6.5's rare-exception sync contributor
    pattern. The canonical case: EF Core's `OnModelCreating` lifecycle hook +
    `IEntityModelCreatingHandler` provider interface. All three §2.6.5 criteria
    hold (intrinsically sync, behaviour-not-data, Registry + StartUp Task
    inapplicable). Reviewers cite this case when challenging future §2.6.5
    invocations.
    Worked example for framework §2.2 secondary-domain rule.

Renamed sections:
  - §E3.3 title — see above.

Cross-reference updates:
  - SIR Follow-up TODOs (Elsa side, original v1.0.0 footer): reference to
    "(framework §2.6.1)" updated to "(framework §2.6.2)" for replacement-
    contract enforcement; companion note added that contribution flows now
    go through domain events (framework §2.6.1, not provider/contributor
    interfaces).

Removed sections: none.

Framework derivation: re-pinned from v1.0.0 to v2.0.0. Both the header
("Derives from: framework constitution v2.0.0") and the Governance "Sync rule"
paragraph (header version reference) are updated.

Follow-up TODOs:
  - TODO(RATIFICATION_DATE) — v2.0.0 is the target ratification version.
  - Entity-design Units B–G — substance of Sipke 2026-05-26 items 1, 2, 3,
    6, 7, 8, 9, 10, 11, 13 lands there. Master thread:
    `follow-up-items/2026-05-08_entity_design.md`.
  - Compile-domain seam follow-up
    (`follow-up-items/2026-05-11_workflow_execution_seam.md`) — CR-1, CR-3,
    CR-3a, CR-4, CR-5 still open; deferred to Units B–G (compile-domain
    substrate).
  - §E4 Elsa configuration — carry-over deferral; awaiting Configuration &
    Infrastructure follow-up meeting.

Code-side commitments (tracked in Unit A follow-up
`follow-up-items/2026-05-27_unitA_constitution_catchup.md`; required before
ratification):
  - Migrate `IPayloadSerializerConverterProvider` and friends to Registry +
    StartUp Task + Domain Event pattern (matches §E3.3's new prose).
  - Migrate EF Core entity save/load handlers to the unified-event pattern.
    **DONE** (Unit 1 consistency fix, 2026-06-03): `OnEntitySaving` +
    `OnEntityLoading` are now each consumed by a **single aggregating**
    `IEventHandler` (`ApplyEntitySavingHandlers` / `ApplyEntityLoadingHandlers`,
    registered once by `EFCorePersistenceShellFeatureBase`) that dispatches every
    registered typed `IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>`
    contributor — the §2.6.1 contributor-interface + single-aggregating-handler
    shape (action-named `…Handler` suffix). The legacy reflection/`GetServices`
    direct-dispatch loops on `ElsaDbContextBase` / `EFCoreQueries` are removed; the
    publication sites now only publish. Catalogued in
    `src/Elsa/Persistence/EFCore/EXTENSION_POINTS.md`. The `IGlobalEntitySavingHandler` (runs
    for every entity, no per-type fan-in) and `IEntityModelCreatingHandler` (runs
    during `OnModelCreating`) remain on their own dispatch paths by design.
  - Delete `Elsa.Expressions.JavaScript.Jint3` (test scaffolding).
  - Update entity-design summary doc per Sipke 2026-05-26 items 8 and 9.

---  (v1.0.0 SIR retained below for history)

Version change: (initial) → 1.0.0
Derives from: framework constitution v1.0.0.

Initial v1 population of the Elsa-specific layer of the two-layer constitution
(decision D26, 2026-05-08 triage row 1). Elsa-specific content extracted from
ARCHITECTURE_v2.md (now drafting archive) and from the project memory listed below.

Added sections (relative to the empty speckit template):
  - Derivation — pinned root (<App> = Elsa), application = Elsa.Server, foundation
    repo = elsa-foundation; derivation contract; cross-citation convention
    "framework §X — Elsa specialization: …".
  - Glossary — Elsa specializations of framework terms.
  - §E1 Worked case study — the elsa-core baseline (carries v2 §1 elsa-core walkthrough);
    closes with a cross-reference to framework §2.21.1 (golden rule of refactoring)
    binding all refactor work in this constitution's scope.
  - §E2 Elsa domain decomposition:
      §E2.1 The Elsa domain tree (12 top-level domains; Workflows.Management removed
            on 2026-05-11 after Joey+Sipke+Frans confirmed Management → Design rename).
      §E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split:
            §E2.2.1 Design sub-domain
            §E2.2.2 Runtime sub-domain
            §E2.2.3 Three deployment shapes
            §E2.2.4 Naming history
            (Originally also contained §E2.2.3 "The seam — WorkflowExecutable" and
             specific runtime entities; both removed 2026-05-11 after Sipke meeting and
             pulled into the Workflow execution seam follow-up file.)
      §E2.3 Elsa.Primitives charter (no Elsa.Common; Elsa.Foundation.Core held back).
      §E2.4 Elsa foundation repo composition (in-repo vs standalone snapshot 2026-05-11).
      §E2.5 ElsaDbContextBase — opt-in capability, not requirement.
  - §E3 Elsa-specific worked examples (5):
      §E3.1 Cross-.Core composition.
      §E3.2 Adapter pattern (Elsa.Locking.FileSystem over Medallion).
      §E3.3 Provider contract (IPayloadSerializerConverterProvider).
      §E3.4 Feature inheritance (persistence shell three-level chain).
      §E3.5 Dual-integration smell (Elsa.Http ↔ Elsa.Expressions.JavaScript).
  - §E4 Elsa configuration [DEFERRED — Configuration & Infrastructure meeting].
  - §E5 Elsa packaging snapshot.
  - Governance — Elsa amendment process, sync rule with framework constitution,
    compliance review.

Removed sections: N/A (initial population).
Renamed sections: N/A.

Templates updated:
  See sync impact report in constitution-framework.md — both layers share the same template
  surface; updates already executed.

Navigation:
  Top-of-file Table of Contents added; uses GFM auto-anchors. Same fallback option as
  framework — explicit <a id> markers — if a renderer breaks the slugs.

Structural deviation from speckit template (justified, intentional):
  See sync impact report in constitution-framework.md. Same deviation applies here; future
  speckit-constitution runs MUST preserve the §-numbered structure.

Memory promotion executed:
  - project_workflows_bounded_context → §E2.2 (partial — split + hard rule + naming
    landed in §E2.2; the WorkflowExecutable seam + three-deployment-shapes detail
    were deferred to follow-up `2026-05-11_workflow_execution_seam.md` on 2026-05-11
    after the Joey+Sipke alignment meeting).

Post-initial-population revision 2026-05-11 (still pre-ratification, no version bump):
  - §E2.1 Workflows.Management row removed; Management → Design rename confirmed by
    Joey + Sipke + Frans.
  - §E2.2 "Hard rule" preserved (Runtime MUST NOT depend on Design); seam mechanism
    deferred.
  - §E2.2.3 "The seam — WorkflowExecutable" section deleted; concept moved to
    follow-up `2026-05-11_workflow_execution_seam.md`.
  - §E2.2.4 / §E2.2.5 renumbered to §E2.2.3 / §E2.2.4.
  - §E2.2.1: removed `IActivityDefinition` from listed Design contracts (deferred).
  - §E2.2.2: removed specific Runtime entities (deferred).
  - §E2.4 "Workflow management" → "Workflow design" in foundation-repo table.
  - §E6 Open Elsa items section deleted entirely (per Joey 2026-05-11): pure project
    record, not constitutional content. Inline [DEFERRED] markers and direct follow-up
    file links cover what mattered constitutionally; this SIR's Follow-up TODOs block
    is now the single index of deferred items.
  - Plan-template G15: WorkflowExecutable seam reference removed; hard-rule citation
    of §E2.2 preserved.
  - §E3.1 Cross-`.Core` composition worked example rewritten: the non-existent
    `Elsa.Workflows.Core` parent package removed; example reshaped around the observable
    `Elsa.Persistence.Core` cross-reference. Note added that Design and Runtime are
    independent sub-domain Cores with no shared parent. Inconsistency with §E2.2's hard
    rule resolved.

Follow-up TODOs (single index of deferred items, post-§E6-removal):
  - TODO(RATIFICATION_DATE) — awaiting Joey + Sipke + Frans formal ratification.
  - §E4 Elsa configuration — awaiting Configuration & Infrastructure meeting. (Meeting
    opens after FastEndpoints first refactor pass yields working API.)
  - §E2.3 Elsa.Notifications charter — pending.
  - §E2.3 Elsa.Mediator charter — pending, only if a mediator pattern materialises.
  - Workflow execution seam (`follow-up-items/2026-05-11_workflow_execution_seam.md`) —
    pulled from §E2.2 on 2026-05-11 (Sipke meeting). Carrier type (working names
    WorkflowBlueprint / MaterializedWorkflow / WorkflowExecutable), ActivityRegistry
    design, Publish-domain interaction, isolated activity execution. Candidate rules
    CR-1..CR-5 captured in the follow-up file. Resurfaces when Runtime refactor begins.
  - Entity Design (`follow-up-items/2026-05-08_entity_design.md`) — WorkflowDefinition
    vs WorkflowInstance separation; three API distributions (WorkflowExecutor,
    WorkflowBuilder, RuntimeMonitor); graphical/UI extraction. Overlaps with the
    Workflow execution seam follow-up; scope together when Runtime refactor opens.
  - DI Container Observability & Resolve Behaviour — replacement-contract
    enforcement (framework §2.6.2) + explicit feature-dependency graph
    (replaces the old DependsOn from framework §2.11). Contribution flows
    now go through domain events (framework §2.6.1), not provider/contributor
    interfaces.
  - Packaging & Versioning + Branching Strategy
    (`follow-up-items/2026-05-11_branching_strategy_github_flow.md`) — multi-iteration
    packaging meeting will refine §E2.4 and §E5.
-->
~~~
