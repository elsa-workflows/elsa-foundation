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
    `src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md`. The `IGlobalEntitySavingHandler` (runs
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

# Elsa Workflow Engine Constitution

**Version:** 3.0.0 (draft)
**Status:** Draft for ratification by Joey Barten, Sipke Schoorstra, Frans van Ek.
**Layer:** Elsa-specific specialization of the [Modular Software Design Framework Constitution](constitution-framework.md).
**Derives from:** framework constitution **v3.0.0**.

---

## Table of Contents

- [Derivation](#derivation)
- [Glossary — Elsa specializations](#glossary--elsa-specializations)
- [§E1 Worked case study — the elsa-core baseline](#e1-worked-case-study--the-elsa-core-baseline)
- [§E2 Elsa domain decomposition](#e2-elsa-domain-decomposition)
  - [§E2.1 The Elsa domain tree](#e21-the-elsa-domain-tree)
  - [§E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split](#e22-workflowsdesign--workflowsruntime-bounded-context-split)
    - [§E2.2.1 Design sub-domain](#e221-design-sub-domain--the-designed-contract) · [§E2.2.2 Runtime sub-domain](#e222-runtime-sub-domain--the-runtime-representation) · [§E2.2.3 Three deployment shapes](#e223-why-the-split--three-deployment-shapes) · [§E2.2.4 Naming history](#e224-naming-history)
  - [§E2.3 `Elsa.Primitives` charter](#e23-elsaprimitives-charter)
  - [§E2.4 Elsa foundation repo composition](#e24-elsa-foundation-repo-composition)
  - [§E2.5 `ElsaDbContextBase` — opt-in capability](#e25-elsadbcontextbase--opt-in-capability-not-requirement)
  - [§E2.6 Runtime contract — executable-always-runs and artifact-only design](#e26-runtime-contract--executable-always-runs-and-artifact-only-design) · [§E2.6.1 Executable-always-runs](#e261-executable-always-runs) · [§E2.6.2 Artifact-only runtime](#e262-artifact-only-runtime)
  - [§E2.7 Elsa 3 backward compatibility — import-only](#e27-elsa-3-backward-compatibility--import-only)
- [§E3 Elsa-specific worked examples](#e3-elsa-specific-worked-examples)
  - [§E3.1 Cross-`.Core` composition](#e31-cross-core-composition-framework-21)
  - [§E3.2 Adapter pattern](#e32-adapter-pattern-framework-27--220)
  - [§E3.3 Domain-event contribution with sync access](#e33-domain-event-contribution-with-sync-access--jsonconverter-registry-framework-261)
  - [§E3.4 Feature inheritance](#e34-feature-inheritance-framework-25)
  - [§E3.5 Dual-integration smell](#e35-dual-integration-smell--elsahttp--elsaexpressionsjavascript)
  - [§E3.6 Adapter — `IJavaScriptExecutionContext` over Jint](#e36-adapter-pattern--ijavascriptexecutioncontext-over-jint-framework-27)
  - [§E3.7 Design-time vs runtime split — JS declarations vs functions](#e37-design-time-vs-runtime-contract-split--js-function-declarations-vs-functions-framework-264)
  - [§E3.8 `Elsa.Http.JavaScript` naming walkthrough](#e38-elsahttpjavascript--secondary-domain-naming-walkthrough-framework-22)
  - [§E3.9 Sync contributor pattern — `IEntityModelCreatingHandler`](#e39-sync-contributor-pattern--ientitymodelcreatinghandler-framework-265)
  - [§E3.10 Three-segment secondary-domain naming with phase split — `Elsa.Http.Activities.<Phase>`](#e310-three-segment-secondary-domain-naming-with-phase-split--elsahttpactivitiesphase-framework-22--e22)
- [§E4 Elsa configuration — \[DEFERRED\]](#e4-elsa-configuration--deferred)
- [§E5 Elsa packaging snapshot](#e5-elsa-packaging-snapshot)
- [Governance](#governance)

---

## Derivation

This document is the **Elsa-specific** layer of a two-layer constitution. It is read alongside `constitution-framework.md`, which carries the framework-neutral rules.

**Rules of derivation.**

- All rules in the framework constitution apply to Elsa by reference.
- Where Elsa specializes or overrides a framework rule, it does so explicitly with the convention **`framework §X — Elsa specialization: …`**.
- Where this document is silent, the framework constitution applies.
- This document pins Elsa's **root domain name**, **concrete domain decomposition**, **foundation repo composition**, and Elsa-specific architectural rules that have no framework-level analog (notably the Workflows.Design ↔ Workflows.Runtime bounded-context split, §E2.2).

**Pinned root.**

- `<App>` = `Elsa`. Every framework-level token of the form `<App>.<Domain>` resolves to `Elsa.<Domain>` in this constitution.
- Application instance = `Elsa.Server` (the host project).
- Foundation repo = `github.com/elsa-workflows/elsa-foundation` (created 2026-05-08).

---

## Glossary — Elsa specializations

The framework glossary terms (Host, Module, Feature, Domain, Application, `.Core`, Thin implementation, Heavy dependency, Foundation repo, Multiple-features-per-module) apply unchanged. Elsa-specific bindings:

| Framework term | Elsa binding |
|---|---|
| Host | The `Elsa.Server` ASP.NET Core application. |
| Application | Elsa — composed of the domain tree in §E2.1. |
| Foundation repo | `elsa-foundation` (this repository). Contents are described in §E2.4. |
| `<App>.Primitives` | `Elsa.Primitives` — replaces the historical `Elsa.Common` as of 2026-05-10. Charter in §E2.3. |

---

## §E1 Worked case study — the elsa-core baseline

The framework was distilled from a structural analysis of the **elsa-core** codebase (`github.com/elsa-workflows/elsa-core`). elsa-core is preserved here as a worked case study — a real-world example of the structural problems the framework is designed to prevent (framework §1).

elsa-core exhibited every anti-pattern in framework §1 at once:

1. **God packages.** `Elsa.Workflows.Core` accumulated contracts and implementations across runtime, design, persistence, and serialization concerns.
2. **Framework leakage into domain code.** ASP.NET Core types, expression engines, and HTTP-specific abstractions surfaced inside packages that should have been transport-agnostic.
3. **Forced heavy dependencies.** Distributed locking (Medallion), expression engines (Jint, Fluid), EF Core providers, message-broker SDKs, and HTTP clients were all transitively reachable from the consumable contract layer. Every consumer pulled the whole tree whether they needed it or not.
4. **Infrastructure locked into the lowest layer.** Persistence base contexts, specific lock implementations, and HTTP framework choices baked into the contracts.
5. **Inverted dependency direction.** Domain code referencing infrastructure; consumer code reaching into provider internals.
6. **Silent DI resolution.** `Elsa.Common` was the vector through which `IronCompress`, `DistributedLock.Core`, and configuration types bled into every consumer; multiple registrations against the same contract overwrote each other without diagnostic.
7. **No naming convention.** `Elsa.Features.*`, `Elsa.Modules.*`, `Elsa.Core.Common`, `Elsa.Core.Serialization.Contracts` — layer-marker buckets that communicated nothing the domain hierarchy did not already say.

The Elsa refactor (this constitution's scope) replaces those failure modes with the rules in framework §2 and the Elsa-specific decomposition in §E2.

**Refactor work in this constitution's scope is governed by framework §2.21.1** — the golden rule of refactoring. Existing tests on the implementations being refactored MUST continue to succeed across the reorganization; the *subject under test* and *objective* are preserved even when test setup, dependencies, or location change. Removing a test requires explicit recorded approval from at least one architect (unanimity reserved for constitutional amendments).

---

## §E2 Elsa domain decomposition

### §E2.1 The Elsa domain tree

Applying framework §2.18's methodology to Elsa, the root-level domains are:

| Domain | Purpose (one verb-led sentence) | Surface package(s) |
|---|---|---|
| `Elsa.Workflows.Design` | Designs workflow definitions: contracts, models, and design-time persistence. | `Elsa.Workflows.Design.Core`, `Elsa.Workflows.Design.Persistence.{Core,EFCore,EFCore.Sqlite}` |
| `Elsa.Workflows.Runtime` | Executes workflows: instances, execution log, bookmarks, runtime persistence. | `Elsa.Workflows.Runtime.Core` *(stub)*, `Elsa.Workflows.Runtime.StorageDrivers` *(stub)* |
| `Elsa.Tasks` | Schedules background work inside the host. | `Elsa.Tasks.Core`, `Elsa.Tasks.Schedules` (helper) |
| `Elsa.Scheduling` | Schedules workflow activations on time/event triggers. | `Elsa.Scheduling.Core`, `Elsa.Scheduling.<Provider>` |
| `Elsa.Serialization` | Serialises payloads and workflow models. | `Elsa.Serialization.Core`, `Elsa.Serialization.Newtonsoft`, `Elsa.Serialization.SystemText` |
| `Elsa.Persistence` | Persists application state (generic CQS-style commands and queries). | `Elsa.Persistence.Core` |
| `Elsa.Locking` | Provides distributed locking. | `Elsa.Locking.Core`, `Elsa.Locking.FileSystem`, `Elsa.Locking.<Provider>` |
| `Elsa.Modularity` | Discovers, describes, enables, validates, and composes modules and features. | `Elsa.Modularity.Core`, `Elsa.Modularity.Nuplane` |
| `Elsa.Expressions` | Evaluates expressions inside workflow steps. | `Elsa.Expressions.Core`, `Elsa.Expressions.JavaScript`, `Elsa.Expressions.Liquid` |
| `Elsa.Messaging` | Integrates with external message brokers. | `Elsa.Messaging.Core`, `Elsa.Messaging.MassTransit` |
| `Elsa.Http` | Exposes and consumes HTTP. | `Elsa.Http`, `Elsa.Http.Activities` |
| `Elsa.Notifications` | In-process pub/sub. | `Elsa.Notifications` *(charter pending — see §E2.3)* |

Sub-domain decomposition follows framework §2.1's naming convention. Variation suffixes are added only when a domain hosts more than one implementation (e.g. `Elsa.Serialization.Newtonsoft` vs `Elsa.Serialization.SystemText`) or when a single implementation already implies a variation choice (e.g. `Elsa.Scheduling.Quartz`).

### §E2.2 Workflows.Design ↔ Workflows.Runtime bounded-context split

**framework §2.18 — Elsa specialization:** `Elsa.Workflows.*` is split into **two dedicated sub-domains with separate persistence layers**: `.Design.*` (designs and persists workflow definitions) and `.Runtime.*` (executes workflows and persists runtime state). The asymmetry is load-bearing for Elsa's deployment shapes (§E2.2.3) and is the agreed boundary.

**Hard rule.** There **must be no direct dependency from `Elsa.Workflows.Runtime.*` to `Elsa.Workflows.Design.*`.** The two sub-domains are co-equal — neither owns the other; the dependency direction is enforced (or at least audited) in CI via project references.

**The seam between Design and Runtime is deferred.** The mechanism by which a workflow flows from Design into Runtime for execution — the carrier type, the activity-contract surfacing, the role of publication, the implications for an `ActivityRegistry` — is **not pinned by this constitution**. It is scheduled for the [Workflow execution seam follow-up](../../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-11_workflow_execution_seam.md) and resurfaces when the Runtime refactor begins.

#### §E2.2.1 Design sub-domain — the designed contract

Design owns the *designed contract* of a workflow: input/output definitions, activity tree, expression bindings, plus the persistence layer that stores them.

Packages:

- `Elsa.Workflows.Design.Core` — contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, etc.
- `Elsa.Workflows.Design.Persistence.Core` — design-time persistence contracts.
- `Elsa.Workflows.Design.Persistence.EFCore` — EF Core implementation.
- `Elsa.Workflows.Design.Persistence.EFCore.Sqlite` — SQLite provider for the EF Core implementation.

#### §E2.2.2 Runtime sub-domain — the runtime representation

Runtime owns the *runtime representation* of workflow execution and its own dedicated persistence layer, separate from Design.

Packages (currently stubs; the specific runtime contracts and entities are deferred to the [Workflow execution seam follow-up](../../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-11_workflow_execution_seam.md)):

- `Elsa.Workflows.Runtime.Core` — runtime contracts.
- `Elsa.Workflows.Runtime.StorageDrivers` — runtime persistence.

Runtime does **not** reference `Elsa.Workflows.Design.Core`.

#### §E2.2.3 Why the split — three deployment shapes

The Workflows.Design ↔ Workflows.Runtime asymmetry is what enables three deployable distribution shapes Elsa supports:

| Distribution | Dependencies | Purpose |
|---|---|---|
| **WorkflowDesigner** | Design only | Build, edit, persist workflow definitions. No execution. |
| **WorkflowExecutor** | Currently both Design and Runtime | Execute workflows. The long-term goal is Runtime-only via the seam between the two sub-domains; the seam mechanism is deferred (see follow-up `2026-05-11_workflow_execution_seam.md`). |
| **RuntimeMonitorService** | Runtime only | Report on execution (instance state, execution log, runtime persistence). |

The naming convention makes the split visible at the project boundary so the dependency direction can be enforced — or at least audited — in CI.

#### §E2.2.4 Naming history

Rejected names for the sub-domains:

- `Elsa.Workflows.Management.*` — rejected as too broad; "Management" could equally cover Runtime concerns.
- `Elsa.Workflows.Definitions.*` — rejected as ambiguous; both Design and Runtime ultimately concern workflow definitions in different forms.
- **`Elsa.Workflows.Design.*` (current)** — names the activity (designing workflows), not the artefact, which makes the asymmetry with Runtime clearer.

**See also §E2.9** — the `WorkflowDefinitionState` scope policy and the architectural triplet `WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable` formalise the Design-side artefacts and name the seam by which they reach Runtime.

### §E2.3 `Elsa.Primitives` charter

**framework §2.3 — Elsa specialization.** The historical `Elsa.Common` package was the leakage vector through which `IronCompress`, `DistributedLock.Core`, and configuration types bled into every consumer in elsa-core (§E1, anti-pattern 6).

The 2026-05-10 first move renamed `Elsa.Common` → `Elsa.Primitives` (per framework §2.3 default outcome). The rename was mechanical: csproj folder + namespace + usings across 55 `.cs` files; deepest-chain consumer builds cleanly.

**Current charter:**

- `Elsa.Primitives` carries only truly domainless building blocks: `Result<T>`, `Page<T>`, base entity abstractions, guard helpers.
- Zero external NuGet dependencies. Without exception.
- Three-repetition rule applies.

**Anticipated further decomposition.** As code reviews land, additional concerns are split out per framework §2.3:

- `Elsa.Serialization` — already present.
- `Elsa.Events.Core` / `Elsa.Events.Strategies` / `Elsa.Events` — the single in-process event concept (`IEvent` / `IEventHandler<T>` / `IEventPublisher`), landed by Unit 1 (2026-06-02) over the shared `Elsa.Pipelines.Core` engine. Supersedes the previously-pending `Elsa.Notifications` charter — notifications are no longer a separate concept (framework §2.6.6). `Elsa.Events.Strategies` is the **helper** library (framework §2.1 thin-impl layer): the three baseline `IEventPublishingStrategy` implementations (Sequential / Parallel / Background) plus the `EventPublishingStrategy` static accessor, referencing only `Elsa.Events.Core` — the same Core-contract-plus-helper shape as `Elsa.Tasks.Core` / `Elsa.Tasks.Schedules` (Unit 2 extraction, 2026-06-03).
- `Elsa.Mediator.Core` / `Elsa.Mediator` — command + request dispatch only (an API concern), trimmed of event handling by Unit 1. Shares `Elsa.Pipelines.Core` with `Elsa.Events.Core`; the two do not reference each other.

**`Elsa.Foundation.Core` is held back.** Elsa does not eagerly create a framework-foundation `.Core` package. If a coherent set of framework-foundation contracts emerges that does not fit in existing packages, the package can be introduced at that point. 

### §E2.4 Elsa foundation repo composition

**framework §2.15 — Elsa specialization.** Elsa's foundation repo is this repository (`elsa-foundation`). Its composition is a snapshot, revisable as evidence accrues.

**In the foundation repo (snapshot 2026-05-11):**

| In the foundation repo | Rationale |
|---|---|
| `Elsa.Server` host | The application entry point. |
| `Elsa.Primitives` | Domainless primitives — used by every other module. |
| Workflow execution runtime + `.Core` | Without execution, the application does nothing locally. |
| Workflow design `.Core` + a default implementation | Required to seed and update workflow definitions during local development. |
| Persistence `.Core` + a default implementation (SQLite EF Core) | Local development without a default persistence implementation is impractical. |
| Expression abstractions | Activities need an expression engine to be useful. |
| Activity abstractions | Workflows need activities to be useful. |
| `Elsa.Serialization.Core` + a default implementation | Most modules depend on payload serialization. |

**Published as standalone features (snapshot 2026-05-11):**

| Standalone | Rationale |
|---|---|
| EF Core providers (Postgres, SQL Server, MySQL) | Heavy provider-specific dependencies. SQLite is the in-repo default. |
| `Elsa.Expressions.JavaScript` (Jint) | Script engine — heavy dependency, optional. |
| `Elsa.Expressions.Liquid` (Fluid) | Same. |
| `Elsa.Messaging.MassTransit` | Message broker SDK — heavy. |
| `Elsa.Locking.<Provider>` for non-FileSystem | FileSystem stays in foundation; others published per provider. |
| Drive integrations, Redis, third-party SaaS connectors | Heavy provider-specific dependencies. |
| Serialization variations beyond the default | Optional. |

**Persistence shipping — row 14 pragmatic stance.** EF Core specific persistence features live in the foundation repo for the time being. A purist split (move EF Core to extensions) was initially preferred, but in practice that split impeded development of other features that depend on persistence. **Open invitation:** if a cleaner approach surfaces that does not impede development, revisit. The decision is pragmatic, not dogmatic.

### §E2.5 `ElsaDbContextBase` — opt-in capability, not requirement

**framework §2.9 — Elsa specialization.** Framework §2.9 forbids the constitution from mandating a base `DbContext` type. Elsa documents an **opt-in** `ElsaDbContextBase` pattern that consumers may inherit from to receive Elsa's global entity save/load hooks (`IEntitySavingHandler`, `IEntityLoadingHandler`). The save hooks are invoked before `SaveChangesAsync` reaches EF Core; the load hooks fire on the read path through the query service (`EFCoreQueries`) as entities are materialised. Both are useful for shadow properties, custom deserializers, and similar cross-cutting concerns. Each legacy hook now coexists with a `§2.6.1` domain event mirror — `OnEntitySaving` (Sequential, from `ElsaDbContextBase`) and `OnEntityLoading` (Sequential, from `EFCoreQueries`) — that features may migrate onto; the legacy interfaces keep running until a feature migrates.

**`ElsaDbContextBase` is shared EF-Core infrastructure, not a model/entity-design requirement.** The persistence invariants Elsa enforces (immutability of Version entities, audit timestamps, etc. — see framework §2.9's "Persistence invariants are defined independently of the persistence provider") are defined independently of EF Core. An EF-Core-backed application MAY enforce those invariants through `ElsaDbContextBase`; another persistence provider MAY enforce the same invariants through interceptors, mappings, store logic, or whatever its native mechanism is. Inheriting from `ElsaDbContextBase` is one integration path, not the only one.

**Hard rules per framework §2.9:**

- The base context is **opt-in only**. Consumer-owned `DbContext` types remain first-class.
- The framework's only constraint at the EF Core contract layer is `where TDbContext : DbContext`. Never `where TDbContext : ElsaDbContextBase` or `where TDbContext : IElsaDbContext`.
- Consumers must be able to install Elsa's entity mappings and contracts **without** inheriting from `ElsaDbContextBase`.

The save/load handler hooks are documented as an opt-in feature in the relevant module's README. They are not a constitutional requirement.

### §E2.6 Runtime contract — executable-always-runs and artifact-only design

Elsa imposes two coupled invariants on its runtime contract. Together they make the Runtime sub-domain self-sufficient and predictable: given a published runnable artifact, the runtime always runs it; given that artifact, the runtime needs nothing else.

#### §E2.6.1 Executable-always-runs

If an artifact is published as a runnable representation of a workflow, the runtime MUST be able to load and execute it. **No condition internal to the runtime system** — missing activity types, missing module installation, in-memory registry drift, version misconfiguration of runtime infrastructure — may break this contract.

**Whether** an artifact is allowed to run in a given context — per tenant, per environment, per role, per workflow-business state — is a **domain/business gate**, implemented in domain code. The runtime's ability to load and run is a **storage/system contract** that is not subject to those gates.

The separation:

- Domain gates may deny execution; they may not destroy executability.
- System failures to execute (missing types, broken loaders, infrastructure errors) are bugs, not features. They violate the contract.

The runtime artifact format carries enough information to be executed independently of any non-runtime concern. The specific artifact name and shape are settled in the entity-design pass (follow-up `2026-05-08_entity_design.md`).

#### §E2.6.2 Artifact-only runtime

The Runtime sub-domain depends on **only** two things:

1. The **runnable artifact** (the entity carrying the structured runtime-oriented representation produced by Build/Compile).
2. The **configured runtime features** that interpret that artifact's format.

Source artefacts (the design-time workflow definition the artifact was built from), authoring history, draft revisions, designer layout metadata, and visualisation projections live in the Design sub-domain and adjacent application-layer projections. They are reachable from the runtime artifact via foreign keys, **but the runtime does not require them to execute**.

Visualisation of an executed instance happens at the application layer, traversing the FK chain from the executed-instance entity → runnable artifact → source-design entities. The runtime sub-domain is not aware of, and does not load, the source side.

**Hard rule.** A runtime that needs to load design-side data to execute is a §E2.2 hard-rule violation. The seam between Design and Runtime is the runnable artifact; nothing else crosses it at execution time.

**See also §E2.9** — `WorkflowExecutable` is named in the architectural triplet `WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`. State is the source; `WorkflowExecutable` is the derived runnable form the Runtime sub-domain consumes per this section's artifact-only contract.

### §E2.7 Elsa 3 backward compatibility — import-only

Elsa 4's compatibility with Elsa 3 is bounded to **import**. A dedicated adapter module — `Elsa3.Workflows.Import` (and analogous siblings as needed for activities, instances, or other Elsa 3 artefacts) — maps Elsa 3 workflow definitions, activity descriptors, and persistence schemas into the Elsa 4 entity model. Once imported, Elsa 4 runs them natively through its own runtime.

**In scope:**

- One-way one-time mapping: read Elsa 3 source, produce Elsa 4 entities, persist.
- Adapter modules named `Elsa3.<Domain>.Import` per the Elsa-3-side concern they map.

**Out of scope:**

- **Dual-run support.** Elsa 3 and Elsa 4 do not run side-by-side from the same process. A migrating consumer imports, then switches.
- **Ongoing viewmodel mapping** for Elsa-3-shaped endpoints in `elsa-studio`. The Elsa 4 API surface is the API; elsa-studio adapts to it, not the other way around.
- **Round-trip translation** back to Elsa 3 entity shapes after import. Imports are terminal.

The compatibility surface is **"one-way, one-time"** by design. This deliberate scoping simplifies the migration story and avoids accumulating long-lived translation infrastructure inside the Elsa 4 codebase.

Mapping decisions per Elsa-3-entity → Elsa-4-entity pair are tracked in [`follow-up-items/2026-05-11_elsa3_compatibility_migration_strategy.md`](../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-11_elsa3_compatibility_migration_strategy.md) and refined as the entity design lands in Units B–G.

---

### §E2.8 Activity catalog is the single source of truth for picker visibility

**Rule.** If an activity is visible in the design-time picker, it has a persisted catalog entry. The picker / design-time API surface MUST query the catalog store; it MUST NOT enumerate live providers, scan loaded assemblies, or otherwise produce picker entries that have no corresponding `ActivityDefinition` row.

**Cross-references:**

- Framework §2.6.4 (design-time vs runtime contract split): the picker reads design-time contracts; runtime construction happens elsewhere.
- Sipke item 7 (2026-05-26): "the catalog is the source-of-truth for picker visibility" — adopted verbatim.

**In scope of this rule (must follow):**

- Every entry the picker can return is a `IActivityDefinition` row, with provenance fields populated (`SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`).
- Activities contributed from a CLR module, a workflow definition, a JSON file, a script source, etc. all reach the picker through the catalog (the reconciler-with-source-modules pattern; Unit B implementation).
- Non-CLR activities (Workflow descriptors, script descriptors) are first-class catalog citizens — the descriptor's `Kind` discriminator on each version row is the runtime resolver lookup key (Unit B §E2.6.1-style domain-failure path on unknown kinds).

**Out of scope (deferred to a separate policy layer):**

- **Context-aware visibility filtering** — tenant scoping, role-based access, feature flags, licensing gates, instance-level overrides. These are visibility refinements over the catalog; they reduce the catalog's output for a given context. They do NOT generate picker entries themselves.

**Why the rule matters.**

The Elsa 3 baseline (see §E1) enumerates loaded `IActivity` implementations at picker time. That makes the picker a function of the runtime's loaded-assembly state — implicit, untraceable, and impossible to scale to non-CLR activities. The catalog-as-source-of-truth rule makes the picker a function of an explicit, queryable, provenance-bearing dataset. The picker becomes auditable; the catalog becomes the integration point for any source (CLR, JSON, workflow, script).

**Removed surface:**

- `IsBrowsable` on `ActivityDefinition` is **not** the visibility mechanism. It does not exist. Visibility = catalog presence. The "should this row appear in the picker?" question has no per-row toggle; it is structurally derived from catalog membership.

**Reconciliation policy — Model X *(Unit C 2026-05-28; pending 2026-06-01 architecture review)*.** The activity catalog is reconciled from trusted sources at creation time only. There is **no operational sibling entity**, no `LastSeenAt` heartbeat, no `IsStale` drift flag, no `RemovedAt` source-disappearance tracking. The immutable content hash for a version lives directly on `IActivityDefinitionVersion.ProvisioningHash` and is the basis of the duplicate-detection path:

- Lookup by `(DefinitionId, Version)`. If absent → create with immutable provenance.
- If present and hash differs → throw `ActivityVersionHashMismatchException` (the source is broken — same identity, different content).
- If present and hash matches → skip or throw per the reconciliation source's duplicate-handling configuration.

Source disappearance is intentionally not tracked at the entity layer; versions are never deleted. Context-aware visibility (tenant / role / feature-flag) is a separate policy layer that filters the catalog for a given context; it is not a reconciliation concern. This codification is **provisional** pending the 2026-06-01 review meeting (agenda Item 1 — Definition of Reconciliation; Item 2 — Model X mechanism); if the review revises, this section revises with it.

This section codifies the rule for the activity catalog. The same shape generalises to other catalogs as Elsa accrues them (workflow catalog, script catalog, expression-evaluator catalog); each will get its own catalog-as-source-of-truth section as that catalog matures.

---

### §E2.9 `WorkflowDefinitionState` scope policy + architectural triplet *(Unit C 2026-05-28; pending 2026-06-01 architecture review)*

`WorkflowDefinitionState` is persisted as the `StateSource` shadow JSON on `WorkflowDefinitionVersion` (immutable) and `WorkflowDefinitionDraft` (mutable) inside the `Elsa.Workflows.Design` sub-domain. It is **the canonical authored document of a workflow definition** — the structured shape an author produces and the system promotes through Draft → Version. Pinning its scope explicitly prevents the god-object failure mode flagged in Sipke's 2026-05-26 entity-design review (item 2): as Units D–G crystallize, `WorkflowDefinitionState` is the natural dumping ground for any workflow-related concern unless its boundary is constitutional.

#### §E2.9.1 In scope of `WorkflowDefinitionState`

Members of State carry **authored content** — the structured representation of what the author drew, declared, and configured:

- Variables (the workflow's variable declarations).
- The activity graph: `Activities` (placed activity nodes) + `ActivityConnections` (edges).
- Workflow-level input/output declarations (`Inputs`, `Outputs`).
- Workflow-level authored options (`WorkflowActivityOptions`, `StrategyOptions`).

Today's State carries exactly these members. The 2026-05-28 audit (Unit C FR-005) confirms they are clean against the policy.

#### §E2.9.2 Out of scope of `WorkflowDefinitionState`

Members that are NOT authored content live elsewhere. Categories explicitly excluded:

- **Instance / runtime / operational state.** Workflow instances, execution log, current activity-execution state, runtime variable bindings, scheduled activations. Owned by the Runtime sub-domain per §E2.2 + §E2.6.
- **Executable / build metadata.** Compiled runtime artifact, build pipeline outputs, materialised executables. Owned by `WorkflowExecutable` (see §E2.9.3) — Units E/G's territory.
- **Publication / deployment state.** Publication status, deployment target, environment-specific configuration overlays. A separate concern with its own entity surface; never folded into State.
- **Search / listing-projection types.** Listing views, dashboard projections, full-text indexes. Derived read models (see §E2.9.3); never fields on State.
- **Security / ownership types.** Tenant ownership, permission grants, audit-of-author identifiers. Carried by ambient `TenantEntity` columns and a separate security model; not authored content.
- **Designer layout metadata.** Canvas positions, sizes, visual node grouping, designer-only annotations. Owned by the sibling entities `WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout` (Unit C FR-006), unified by `IWorkflowDefinitionLayout` (FR-007); never nested into `ActivityNode` and never reachable through `WorkflowDefinitionState`.
- **Validation errors.** Owned by the sibling entity `WorkflowDefinitionDraftValidation` (Unit C FR-021) — derived from State, not part of it.

A property newly proposed for `WorkflowDefinitionState` whose category is genuinely ambiguous between authored content and one of these out-of-State categories surfaces as an architecture-meeting escalation; resolution is constitutional (amend this section), not silent.

#### §E2.9.3 Architectural triplet

`WorkflowDefinitionState` participates in an irreducible triplet that names the three artefacts every workflow definition produces in the system:

1. **`WorkflowDefinitionState`** — the canonical authored document (above).
2. **Read models / projections** — derived views over State for listing, search, dashboarding, and any non-authoring read need. These live in `Elsa.Workflows.Design.Api` or downstream domains' query layers; they are projection-shaped, not authoring-shaped. They are never persisted back into State.
3. **`WorkflowExecutable`** — the compiled runtime artifact (substance owned by Units E/G; named here so the triplet is complete). Build/Compile produces it from an immutable `WorkflowDefinitionVersion.State`; the Runtime sub-domain executes it per §E2.6's artifact-only contract. State is the source; `WorkflowExecutable` is the derived runnable form.

The three sit at separate scopes — **authoring**, **reading**, **executing** — and **must not be merged**. Conflating authoring and projection collapses the read side back into State and creates the god-object Sipke flagged. Conflating authoring and executable conflates source with output, breaking §E2.6's artifact-only rule. The triplet is the load-bearing structural rule that lets Design and Runtime stay separable per §E2.2.

#### §E2.9.4 Enforcement

The in-State / out-of-State boundary is enforced by:

1. **The XML documentation header** on the `WorkflowDefinitionState` record (Unit C FR-003), quoting the scope and pointing at this section.
2. **PR review discipline** against this constitutional rule — reviewers reject creep.

Automated compile-/build-time enforcement (scope-policy static analyser) is **deferred to a future *Code Analysers* epic** that approaches the platform's static analysis as a unified bundle, rather than shipping ad-hoc per-rule micro-validators. The list of categories in §E2.9.2 will inform the eventual analyser when that epic opens (registered in [`follow-up-items/2026-05-28_future_epic_code_analysers.md`](../../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-05-28_future_epic_code_analysers.md)).

#### §E2.9.5 Reconciliation policy applies here too

`WorkflowDefinition` / `WorkflowDefinitionVersion` / `WorkflowDefinitionDraft` reconciliation follows **Model X** — the same immutable-provenance, skip-or-throw-with-hash-safety-net policy codified for the activity catalog at the end of §E2.8. No per-pass mutating fields (no `LastSeenAt`, no `IsStale`, no `RemovedAt`) live on any workflow-design entity; reconciliation is transactional at creation time only. Where the provenance fields (`SourceKind` / `SourceId` / `SourceVersion` / `ProvisioningHash` / `ProvisionedAt` / `ProvisionedBy`) ultimately live on workflow-design entities is Unit D's allocation pass; Unit C codifies the policy and leaves the field allocation to Unit D.

#### §E2.9.6 Status

**Provisional pending 2026-06-01 architecture-review ratification.** Joey's adopted position, recorded as a draft sub-section per Unit C FR-016c / FR-020 / FR-024a pattern (constitutional drafts adopted ahead of review per working-loop §5). The 2026-06-01 agenda Items 1, 2, 3, 4, 4b, 5, 6 cover the surrounding provisional sub-rules; if any are revised at the review, this section revises in tandem.

**Cross-references:** §E2.2 (Design ↔ Runtime split — the triplet operates within Design and seams into Runtime via `WorkflowExecutable`); §E2.6 (artifact-only runtime — the seam terminates at `WorkflowExecutable`, not at State); §E2.8 (Model X reconciliation policy, applies symmetrically per §E2.9.5).

#### §E2.9.7 Draft-mutation command surface *(Unit 2 2026-06-03; provisional, pending architecture-review ratification)*

The canonical command surface for **mutating** a `WorkflowDefinitionDraft` is a **single coarse, diff-based command** — `IUpdateDraftCommand` — not a family of granular per-concept mutation commands.

- **One mutation command.** `IUpdateDraftCommand.Execute(UpdateDraftRequest)` accepts the **complete desired** `WorkflowDefinitionState` (+ its layout sibling, carried beside State per §E2.9.2 — never inside it). Full-state-always: there is no patch API. Inside the per-Draft distributed lock (`workflow-draft:{DraftId}`) it loads the stored state, wholesale-assigns the desired state (last-writer-wins — no version check), **diffs** stored vs desired per concept (Variables/Inputs/Outputs by `ReferenceKey`, Activities and layout by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple), runs the in-lock validation gate, persists atomically, then publishes **one event per detected difference**.
- **The event surface is preserved, not collapsed.** The diff emits the same 20 per-concept mutation events the former granular commands published (catalogued in the Events section of `Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`); their *types* and catalog headings are unchanged — only the publication site moved onto `IUpdateDraftCommand`. This keeps the event-sourcing seam open for a later event-sourcing unit (Unit H): subscribers observe the per-diff stream regardless of whether the mutation arrived via 20 commands or one.
- **Lifecycle commands remain distinct.** `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, and `IPromoteDraftToVersionCommand` are **not** mutations of an existing Draft's content and stay as separate commands with their own lifecycle events (`OnDraftCreated`, `OnDraftDiscarded`). `IUpdateDraftCommand` emits none of these.
- **One origination event, not two.** A cloned Draft and a fresh Draft share the single origination event `OnDraftCreated`; there is **no** separate `OnDraftClonedFromVersion`. `ICloneDraftFromVersionCommand` delegates to `ICreateDraftCommand` (the single origination path), and clone-vs-fresh is distinguished solely by the immutable optional `WorkflowDefinitionDraft.SourceVersionId` — a plain provenance column (no navigation property) surfaced on `OnDraftCreated.SourceVersionId` (`null` for a fresh Draft).
- **Reads route through the query service.** Commands that only read (no change tracking) — e.g. `ICloneDraftFromVersionCommand` loading the source Version + layout — use `IQueries<T>` rather than a hand-rolled `DbContextFactory` + loading-handler loop. The query service runs the read-side hydration pipeline (legacy `IEntityLoadingHandler` + the `OnEntityLoading` Sequential event, the read-side mirror of `OnEntitySaving`) and disposes its own short-lived context. A command opens its own tracked context only when it queries, mutates, and saves the *same* entity.
- **Validation pair unchanged.** The `OnDraftValidating` (Sequential, in-lock gate) / `OnDraftValidated` (Background, outcome) pair is published by the command exactly as before.

This supersedes Unit C's Phase-7 granular-command surface for Draft mutation. The generic CQS command-per-operation guidance elsewhere in this constitution (and the framework's `Elsa.Persistence` CQS row) is unaffected — this rule narrows only the **Draft-mutation** surface within the Design domain.

**Provisional pending architecture-review ratification**, consistent with §E2.9.6 — recorded as a draft sub-section per the working-loop adopt-ahead-of-review pattern. Tracked in [`follow-up-items/2026-06-02_unit_single_update_command.md`](../../../../elsa-foundation-project-management/epic1-elsa-refactor-constitution/follow-up-items/2026-06-02_unit_single_update_command.md).

**Cross-references:** §E2.9.1/§E2.9.2 (what State carries — the diff operates over exactly those in-scope fields, layout stays beside State); §E2.6.6 (Sequential vs Background delivery strategies the command uses for the gate vs the per-diff stream).

---

## §E3 Elsa-specific worked examples

The framework constitution is written with synthetic examples. The Elsa-specific examples below instantiate framework rules using concrete `Elsa.*` names. Each example cites the framework rule it instantiates.

### §E3.1 Cross-`.Core` composition (framework §2.1)

**There is no shared `Elsa.Workflows.Core` parent package.** Design and Runtime are *independent sub-domain Cores* — each stands on its own, consistent with §E2.2's bounded-context split. Cross-`.Core` composition still happens (and framework §2.1 applies), but through unrelated top-level Cores that both sub-domains may consume.

Top-level domain Cores in play:

- `Elsa.Persistence.Core` — generic persistence contracts (e.g. `IAddCommand<T>`, `IQuery<T>`).
- `Elsa.Serialization.Core` — serialization contracts.

Workflows sub-domain Cores (no shared parent):

- `Elsa.Workflows.Design.Core` — design-time contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, etc.
- `Elsa.Workflows.Runtime.Core` — runtime contracts (specifics deferred — see follow-up `2026-05-11_workflow_execution_seam.md`). **Does not reference `Elsa.Workflows.Design.Core`** (§E2.2 hard rule).

The **observable cross-`.Core` reference today** is in Design's sub-sub-domain Cores:

- `Elsa.Workflows.Design.Persistence.Core` — references `Elsa.Workflows.Design.Core` and *may* reference `Elsa.Persistence.Core` as an explicit design choice when this would make sense.

Implementations:

- `Elsa.Workflows.Design.Persistence.EFCore` — EF Core implementation of the design-persistence sub-sub-domain.

**Impl-to-impl carve-out (framework §2.1, row 7).** Implementations across **unrelated** sub-domains never reference each other — e.g. `Elsa.Workflows.Design.Persistence.EFCore` and any future `Elsa.Workflows.Runtime.StorageDrivers.*` provider must not reference each other. Implementations **within the same provider family** *may* — e.g. an `Elsa.Workflows.Design.Persistence.EFCore.SqlServer` provider package extending an `Elsa.Workflows.Design.Persistence.EFCore` base implementation. This is directional, intentional, and reflected in the package naming and dependency graph.

### §E3.2 Adapter pattern (framework §2.7 + §2.20)

`Elsa.Locking` follows framework §2.20 (provider module decomposition):

- `Elsa.Locking.Core` — defines `IDistributedLockProvider`. Zero external dependencies.
- `Elsa.Locking.FileSystem` — registers a `DistributedLockProviderAdaptor` that wraps `Medallion.Threading.FileSystem`. The Medallion package is not visible to any consumer of `Elsa.Locking.Core`.

Replacing file-system locks with Redis means shipping `Elsa.Locking.Redis` as a separate module — no changes anywhere else.

**§2.20 application.** When Elsa.Locking only had a FileSystem provider, the umbrella `Elsa.Locking` (without provider suffix) was retired and everything consolidated into `Elsa.Locking.FileSystem` (validated 2026-05-10). The empty stub was eliminated. When a second provider (e.g. Redis) arrives and *real* shared adapter logic emerges, a `Elsa.Locking.Medallion` provider-family package may be extracted under framework §2.1's impl-to-impl carve-out.

Additionally, **`DistributedLock 2.8.1`** (the meta-package fronting eleven `DistributedLock.<Provider>` sub-packages) was replaced with a direct `DistributedLock.FileSystem` reference. The MongoDB sub-package's transitive dependencies (`Snappier`, `SharpCompress`) had known CVEs, none of which Elsa.Locking actually used. This is the §2.20 Rule 2 application.

### §E3.3 Event contribution with sync access — `JsonConverter` registry (framework §2.6.1)

The `JsonPayloadSerializer` runs `System.Text.Json` `JsonConverter` callbacks synchronously and cannot await async dispatch at converter resolution time. Per framework §2.6.1, the contribution still flows through the event pipeline — the access is sync because the population happened earlier, via the **Registry + StartUp Task sub-pattern**, and the event itself follows §2.6.1's **contributor-interface + single-handler** sub-pattern: features implement a return-style `IJsonConverterSource` and one `RegisterJsonConverters` handler aggregates. The event is published **Sequential** (the default) so the StartUp task can read the contributed converters back (Unit C Phase-3 amendment 2026-05-28; unified-event naming Unit 1 2026-06-02; contributor-interface revert Unit 1 addendum 2026-06-02):

1. **`Elsa.Serialization.Core`** defines:
   - `JsonPayloadConverterRegistry` — with `Register(JsonConverter)` and accessor methods.
   - `OnJsonPayloadConvertersInitializing` — a `sealed class` event (`IEvent`, in `Elsa.Events.Core.Contracts`) exposing a **directly-accessible `ICollection<JsonConverter> Converters`** that the single handler writes into.
   - `IJsonConverterSource` — the return-style contributor interface (a *Source*: it yields converters, it does not act on a shared object).

   ```csharp
   public sealed class OnJsonPayloadConvertersInitializing : IEvent
   {
       public ICollection<JsonConverter> Converters { get; } = [];
   }

   public interface IJsonConverterSource
   {
       IEnumerable<JsonConverter> GetConverters();
   }
   ```

2. **`Elsa.Serialization.<Provider>`** (the feature implementing the serialization domain) registers the StartUp task **and the single `RegisterJsonConverters` handler** — the only `IEventHandler<OnJsonPayloadConvertersInitializing>`, which injects `IEnumerable<IJsonConverterSource>` and aggregates:

   ```csharp
   // single handler
   public sealed class RegisterJsonConverters(IEnumerable<IJsonConverterSource> sources)
       : IEventHandler<OnJsonPayloadConvertersInitializing>
   {
       public Task Handle(OnJsonPayloadConvertersInitializing e, CancellationToken ct)
       {
           foreach (var source in sources)
               foreach (var converter in source.GetConverters())
                   e.Converters.Add(converter);
           return Task.CompletedTask;
       }
   }

   // startup task
   var @event = new OnJsonPayloadConvertersInitializing();
   await eventPublisher.Publish(@event);   // default Sequential — contributions read back
   registry.RegisterAll(@event.Converters);
   ```

3. **`Elsa.Expressions`** (and any other contributing feature) extends serialization by **implementing `IJsonConverterSource`** and registering it via DI (`services.AddScoped<IJsonConverterSource, ExpressionsJsonConverterSource>()`) — it does NOT register its own event handler. Neither feature references the other.

4. **At runtime**, the `JsonPayloadSerializer`'s sync code accesses the populated `JsonPayloadConverterRegistry` directly. No async dispatch at the read site.

The mechanism is identical to a cross-domain contribution; only the access pattern differs (registry-mediated). This is the canonical worked example of the Registry + StartUp Task sub-pattern from framework §2.6.1, refactored under the §2.6.1 contributor-interface + single-handler sub-rule.

*Further worked examples of the contributor-interface + single-aggregating-handler shape.* The EF Core persistence save/load seam ships the same shape with action-named contributor suffixes (`IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>` dispatched by the single `ApplyEntitySavingHandlers` / `ApplyEntityLoadingHandlers` aggregators) — see [`src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../../src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md) for that domain's overridable contracts, contributor interfaces, and Events section, and the repo-root [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index for every extension point across the codebase (framework §2.22.1, §2.22.2).

*Legacy state.* The historical implementation used `IPayloadSerializerConverterProvider` (provider-pattern, `IEnumerable<T>` resolution at the read site). The migration to the pattern above is a code item tracked in the Unit A follow-up. When the migration lands, it adopts the source + single-handler shape shown here.

### §E3.4 Feature inheritance (framework §2.5)

Elsa's persistence stack inherits across three levels:

```
PersistenceShellFeatureBase<TDbContext>
    └── EFCoreWorkflowsPersistenceFeatureBase
            └── SqliteWorkflowDefinitionPersistenceShellFeature
```

Each level adds to or specialises the level above it through compile-time inheritance, never through peer references. The leaf (`SqliteWorkflowDefinitionPersistenceShellFeature`) is the activated feature; the intermediate levels are abstract.

### §E3.5 Dual-integration smell — `Elsa.Http` ↔ `Elsa.Expressions.JavaScript`

**framework §2.14 — Elsa specialization (real example).** Today's Elsa HTTP module directly brings in JavaScript-engine dependencies because some HTTP functionality exposes JavaScript functions that belong to the HTTP domain.

This violates framework §2.14: a consumption-shape that depends on two external systems (HTTP framework + Jint script engine) is a boundary smell. The JS-functions-in-HTTP code must be its own consumption-shape module:

- `Elsa.Http` — HTTP integration. Depends on the HTTP framework.
- `Elsa.Expressions.JavaScript` — JavaScript expression integration. Depends on Jint.
- `Elsa.Http.JavaScript` (or under a fresh orchestration domain) — the consumption-shape that exposes HTTP-specific functions to JavaScript. Depends on both `Elsa.Http` and `Elsa.Expressions.JavaScript`. Package name signals the combined dependency.

Consumers who want HTTP without JavaScript reference only `Elsa.Http`.

**Status.** Resolved. The untangling was performed in the 2026-05-19 refactor session: the cross-cutting module was extracted as **`Elsa.Http.JavaScript`** under the framework §2.2 secondary-domain naming rule (HTTP is the model-owning domain; JavaScript is the consumer contributing functions/declarations against HTTP models). The naming-decision walkthrough lives in §E3.8; the adapter that isolates Jint from `.Core` consumers lives in §E3.6; the design-time vs runtime contract split that drove the contribution shape lives in §E3.7. This section now serves as the dual-integration smell record and the cross-reference hub for the three resolution worked examples.

### §E3.6 Adapter pattern — `IJavaScriptExecutionContext` over Jint (framework §2.7)

Second worked example for framework §2.7's adapter pattern. The first (`Elsa.Locking.FileSystem` in §E3.2) wrapped an infrastructure library; this one wraps a script engine — same pattern, different domain, evidence that §2.7 generalises.

**The seam.** `IJavaScriptExecutionContext` is defined in `Elsa.Expressions.JavaScript.Core` with **zero Jint reference**. Consumers of the JavaScript expression domain (e.g. activities that evaluate JS, the rendering domain that asks for declarations) depend only on `IJavaScriptExecutionContext` — they never see Jint types.

**The adapter.** `Elsa.Expressions.JavaScript.Jint` (the implementation feature) holds a `JintJavaScriptExecutionContext` that wraps Jint's `Engine`, options, and runtime types. The adapter exposes the `IJavaScriptExecutionContext` surface (register function, register object, register type, evaluate); Jint stays entirely inside the implementation package.

**Consequence.** Replacing Jint with a different JS engine means shipping a new feature module that supplies a different `IJavaScriptExecutionContext` adapter — no changes anywhere in the rest of Elsa. Consumers that want JS evaluation but want to choose the engine reference only `Elsa.Expressions.JavaScript.Core` plus the chosen `Elsa.Expressions.JavaScript.<Engine>` feature.

### §E3.7 Design-time vs runtime contract split — JS function declarations vs functions (framework §2.6.4)

Worked example for framework §2.6.4. The JS expression domain has two distinct consumers of "contributed function" data:

- **Design-time consumer.** The rendering / intellisense layer generates a declarations document so the editor knows what functions exist, what they accept, and what they return. It cares about *shape*, not *binding*.
- **Runtime consumer.** The evaluator (Jint, via §E3.6's adapter) needs the actual function bindings — a delegate that, when called, executes the contributed function. It cares about *binding*, not *shape*.

A unified provider would force every contributing feature to satisfy both consumers even when only one is relevant. The split:

| Phase | Event | Contributor interface (`.Core`) | Kind | Single handler | Where impls live |
|---|---|---|---|---|---|
| Design-time | `OnDeclarationsDocumentGenerating` (in `Elsa.Expressions.JavaScript.Rendering.Core`) | `IJavaScriptDeclarationContributor` | Contributor | `BuildDeclarationsDocument` | `Elsa.Workflows.Design.JavaScript`, `Elsa.Http.JavaScript`, and other design-time contributors |
| Runtime — before | `OnEvaluatingScript` (in `Elsa.Expressions.JavaScript.Core`) | `IScriptPreProcessor` | PreProcessor | `PreProcessScript` | `Elsa.Expressions.JavaScript`, `Elsa.Workflows.Runtime.JavaScript`, `Elsa.Expressions.JavaScript.Libraries`, and other runtime contributors |
| Runtime — after | `OnScriptEvaluated` (in `Elsa.Expressions.JavaScript.Core`) | `IScriptPostProcessor` | PostProcessor | `PostProcessScript` | `Elsa.Workflows.Runtime.JavaScript` (variable copy-back) |

Both phases may carry a shared `.Core` data record describing the *shape* of a contributed function (name, parameter types, return type, documentation). Each event binds to its own consumer; all are published Sequential (contribution — the publisher reads the contributed declarations / bindings back) through the framework's event pipeline (framework §2.6.1).

**The declarations cluster uses the `Contributor` kind** (framework §2.6.1 sub-pattern). The sink is a **rich mutable context**, not a flat collection: the declarations context — `IJavaScriptDeclarationsContributionContext` (renamed from the old `IJavaScriptRenderingContext` so the name states its purpose: a context contributors add declarations to) — exposes `AddVariable(...)` / `AddType(...)` / `AddFunction(...)`. A contributor **receives the context and acts on it** — `ValueTask Contribute(IJavaScriptDeclarationsContributionContext, CancellationToken)` returning void — rather than returning values. The one `BuildDeclarationsDocument` handler injects `IEnumerable<IJavaScriptDeclarationContributor>` and hands the context to each in turn.

**The script-evaluation cluster uses the `PreProcessor` / `PostProcessor` kind** because `OnEvaluatingScript` / `OnScriptEvaluated` are a before/after pair (framework §2.6.1 — pre/post kind). Both act on the live `IJavaScriptExecutionContext` (which exposes `RegisterFunction(...)`, value get/set, etc.), so the contract is "act on the lifecycle context", not "return items":

- `IScriptPreProcessor.PreProcess(script, executionContext, expressionContext, options, ct)` runs at `OnEvaluatingScript` (before the script executes) to register functions, types, and values. The one `PreProcessScript` handler aggregates every registered pre-processor.
- `IScriptPostProcessor.PostProcess(executionContext, expressionContext, options, ct)` runs at `OnScriptEvaluated` (after the script executes) to act on the result. The canonical post-processor copies engine variables back into the workflow context (`CopyVariablesToWorkflowContext`). The one `PostProcessScript` handler aggregates every registered post-processor.

The Jint adapter (§E3.6) publishes `OnEvaluatingScript` before `Evaluate(...)` and `OnScriptEvaluated` after, so both single handlers fire around every evaluation.

**A single feature MAY implement several of these interfaces** — e.g. `Elsa.Http.JavaScript` ships an `IJavaScriptDeclarationContributor` (HTTP-type declarations, design-time) and an `IScriptPreProcessor` (HTTP-type bindings, registered before evaluation), registering each via DI. It MAY implement only one — e.g. a feature that only needs intellisense implements just the declaration contributor.

**Generalisation.** The contract-level split mirrors the Elsa §E2.2 sub-domain split (Workflows.Design ↔ Workflows.Runtime) at finer granularity. The framework rule (§2.6.4) is independent of any specific Elsa-side sub-domain split, but the JS case is its cleanest worked example: same data, two consumers, two events, two handler audiences.

### §E3.8 `Elsa.Http.JavaScript` — secondary-domain naming walkthrough (framework §2.2)

Worked example for framework §2.2's secondary-domain naming sub-rule. The decision: name the cross-cutting module `Elsa.Http.JavaScript` rather than `Elsa.JavaScript.Http`.

**The test: where do the models come from?**

The cross-cutting module contributes function declarations and function bindings *for HTTP-domain concepts* — `HttpRequest`, headers, route values, body accessors, query data. Those are HTTP models. The JS side ships only the **consumer machinery**:

- An `IJavaScriptDeclarationContributor` that adds HTTP-type declarations (so intellisense sees `httpRequest.headers`, `httpRequest.body`, etc.).
- An `IScriptPreProcessor` that adds HTTP-type bindings before evaluation (so the evaluator can resolve `httpRequest` to the current HTTP context).

Neither contributor defines a new HTTP model. They expose HTTP's existing models to a different consumer (a JS evaluator).

**The model-owning domain wins the prefix.** HTTP is the model-owning domain; JavaScript is the consumer. Therefore: **`Elsa.Http.JavaScript`**.

**What the reverse form would produce.** `Elsa.JavaScript.Http` would force `Elsa.JavaScript` to grow one sub-branch per model-owning domain it exposes to JS: `Elsa.JavaScript.Http`, `Elsa.JavaScript.Workflows`, `Elsa.JavaScript.Activities`, … — a namespace that holds unrelated model branches glued together only by their shared script-engine consumer. The framework calls this out as a junk-drawer anti-pattern in §2.2; the test prevents it.

**The pattern for future cross-cutting modules.** A cross-cutting `Elsa.<ModelDomain>.<ConsumerDomain>` is created whenever a consumer domain (JavaScript, Liquid, GraphQL, OpenAPI, …) needs to expose models from another domain. The naming decision takes 30 seconds: identify the model-owning domain; prepend it.

### §E3.9 Sync contributor pattern — `IEntityModelCreatingHandler` (framework §2.6.5)

Worked example for framework §2.6.5's rare-exception sync contributor pattern. The case: EF Core's `OnModelCreating` lifecycle hook needs to invoke contributing handlers synchronously at the moment EF Core builds the model. Async event dispatch via `IEventPublisher.Publish` cannot apply because `OnModelCreating` is intrinsically sync in EF Core's contract. The Registry + StartUp Task sub-pattern does not apply because what's being contributed is *behaviour* (each handler customises the shared `ModelBuilder`), not *data* — and that behaviour is bound to the specific lifecycle moment EF Core invokes.

**The mechanism in Elsa.**

- `Elsa.Persistence.EFCore` declares `IEntityModelCreatingHandler` with `void Handle(ElsaDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType)`.
- Features that need to customise the EF model (e.g. `Elsa.Activities.Design.Persistence.EFCore` for activity-catalog mappings, `Elsa.Workflows.Design.Persistence.EFCore` for workflow-design mappings, the SQLite provider feature for shadow-column conventions) register their `IEntityModelCreatingHandler` implementations via DI.
- `ElsaDbContextBase.ApplyEntityModelCreatingHandlers` (invoked inside `OnModelCreating`) resolves `IEnumerable<IEntityModelCreatingHandler>` from a fresh DI scope and invokes each handler sync per registered entity type.

**Why §2.6.5 applies — the three criteria.**

1. **Intrinsically sync dispatch site.** EF Core's `OnModelCreating(ModelBuilder)` is sync. There is no async equivalent. Forcing async event dispatch (`IEventPublisher.Publish(...).GetAwaiter().GetResult()`) would be sync-over-async, with no benefit.
2. **Behaviour, not data.** Each handler MUTATES the shared `ModelBuilder` — it doesn't return data the caller collects. Contribution events excel at "contribute items to a carried list" (Registry + StartUp Task); the model-creating case is "act on the shared lifecycle target."
3. **Registry + StartUp Task doesn't apply.** The `ModelBuilder` instance doesn't exist at application startup — it's constructed by EF Core when the first `DbContext` is instantiated. Even if we pre-registered "behaviours to run later", we'd still need to invoke them at the lifecycle moment — adding indirection without removing the structural sync requirement.

**What this case is NOT.** It is NOT a license to use sync contributor interfaces for ANY contribution flow. The framework's §2.6.5 head explicitly demands that reviewers challenge every §2.6.5 invocation: "could the contribution be reshaped to fit §2.6.1 or Registry + StartUp Task?" If yes, §2.6.5 does not apply.

**Cross-references.** Cross-references to this example land in plan-stage Constitution Check gates (Elsa-side plan-template G21 entry) so future plans aren't surprised by the legacy interface remaining in the codebase. The activity-catalog Unit B fold (Spec 001) is the first concrete plan that codifies this exemption.

### §E3.10 Three-segment secondary-domain naming with phase split — `Elsa.Http.Activities.<Phase>` (framework §2.2 + §E2.2)

*New worked example (Unit C clarify session 3, 2026-05-28; pending 2026-06-01 architecture review).*

§E3.8 walked through two-segment secondary-domain naming (`Elsa.Http.JavaScript`) — a model-owning domain (HTTP) contributing to a consumer domain (JavaScript). This worked example extends that pattern to a **three-segment** case where the consumer domain itself has a phase split (Design ↔ Runtime), so the contributing modules need to express *both* the consumer domain *and* the phase.

The canonical case is HTTP contributing activities. Activities have both a design-time variant (descriptors, picker entries, input/output schema, validators) and a runtime variant (the executable handler that runs HTTP). Per the §E2.2 hard rule, Design and Runtime cannot live in the same implementation module without breaking the dependency direction the rule enforces.

#### The structure

```
Elsa.Activities.Design.Core           ← contracts for design-time activities (IActivityDefinition, InputDefinition, etc.)
Elsa.Activities.Runtime.Core          ← contracts for runtime activity execution

Elsa.Http.Activities.Design           ← HTTP-specific design-time activities + their validators
  references: Elsa.Activities.Design.Core
            + Elsa.Workflows.Design.Validations.Core   (for the IDraftValidator contributor interface)
            + Elsa.Http                                (HTTP models)

Elsa.Http.Activities.Runtime          ← HTTP-specific runtime activity execution
  references: Elsa.Activities.Runtime.Core
            + Elsa.Http                                (HTTP models)
```

The same pattern generalises to every model-owning domain contributing activities: `Elsa.Email.Activities.Design` + `Elsa.Email.Activities.Runtime`, `Elsa.Slack.Activities.Design` + `Elsa.Slack.Activities.Runtime`, etc.

#### Why this shape

**Model-owning domain wins the prefix (framework §2.2).** HTTP brings HttpRequest, headers, route values, body accessors — its own models, exposed through an activity surface. Activities is the consumer domain — it consumes contributed activity-shaped things. Per §2.2's secondary-domain rule, HTTP wins the prefix.

**The reverse form is a junk drawer (framework §2.2's named anti-pattern).** Naming `Elsa.Activities.Design.Http` would force `Elsa.Activities.Design` to grow one sub-branch per upstream model-owner: `.Http`, `.Email`, `.Slack`, `.Database`, `.Files`, … — a namespace of unrelated model branches glued together only by their shared consumer surface. That is exactly the "namespace as a junk drawer" anti-pattern §2.2 calls out.

**Three segments express the consumer-domain + phase pair atomically.** `Elsa.Http.Activities.Design` reads as "HTTP-contributing-design-time-activities" — a coherent purpose. `Elsa.Http.Activities.Runtime` reads as "HTTP-contributing-runtime-activities" — also coherent. The two modules are siblings under the model-owning domain.

**§E2.2 hard rule preserved at the implementation level.** A consumer that wants only HTTP-runtime activities references `Elsa.Http.Activities.Runtime` directly; that package does not reference `Elsa.Http.Activities.Design`. The Design/Runtime dependency direction is enforced by the package boundary, not just by namespace convention.

**§2.20 Rule 1 preserved — no empty `Elsa.Http.Activities` umbrella** unless real shared code emerges between Design and Runtime. When that shared code arrives, an `Elsa.Http.Activities` umbrella (or a `.Core` for the shared shape) can be extracted then per §2.1's impl-to-impl carve-out.

**§2.6.4 satisfied — the design-time contract surface (`OnDraftValidating`) is consumed by the design-time module; the runtime contract surface is consumed by the runtime module.** The two sub-domain `.Core`s never bleed into each other.

#### Where validators land

Activity-specific validators co-locate with their **activity's design-time module**, not in a separate `Elsa.Workflows.Design.Validations.<Domain>` sub-module under Validations. The reason: HTTP-activity-validators read HTTP-activity-specific properties (auth policies, URL formats, etc.) — the validator and the activity definition share intimate knowledge of the HTTP-activity shape. Co-locating them keeps that knowledge in one module.

The validator is just an `IDraftValidator` (the return-style contributor interface) registered via DI by the `Elsa.Http.Activities.Design` feature — NOT its own `IEventHandler<OnDraftValidating>`. The single `ExecuteValidations` handler (in `Elsa.Workflows.Design.Validations`) resolves every `IDraftValidator` and aggregates their returned errors. The HTTP feature references `Elsa.Workflows.Design.Validations.Core` for the `IDraftValidator` interface + `ValidationError` contract. No new module is needed per activity — the validators are a small body of work alongside the definitions.

#### What this case is NOT

It is NOT a license to over-elaborate the namespace for *every* cross-domain contribution. The §2.2 secondary-domain test still applies: where do the models come from? If a feature brings no new models and contributes against existing ones, secondary-domain naming applies; otherwise, the feature lives in its own domain. The three-segment composition activates only when the consumer-domain ALSO has an internal axis (Design/Runtime) that must show up at the package boundary.

For example, JavaScript exposing HTTP models (§E3.8) is two-segment because JavaScript itself is a consumer surface without a Design/Runtime split inside its consumption — there's `OnDeclarationsDocumentGenerating` (design-time) and `OnEvaluatingScript` (runtime) per §E3.7, but the JS package that contributes HTTP-typed bindings is a single module that implements both contributor interfaces (`IJavaScriptDeclarationContributor` + `IScriptPreProcessor`). The Activities case is different — the runtime side genuinely runs code (executable handlers), the design side genuinely composes catalog data, and the §E2.2 hard rule says those cannot live in the same implementation module.

#### Cross-references

- Framework §2.2 (model-owning domain wins prefix; junk-drawer anti-pattern named).
- Elsa §E2.2 (Workflows-Design ↔ Workflows-Runtime hard rule; Activities follows the same principle in its own variant).
- Elsa §E3.8 (the two-segment precedent — `Elsa.Http.JavaScript`).
- Spec 002 (Unit C) FR-034 (validator co-location rule cites this section).
- Future units shipping HTTP activities (and Email, Slack, etc. activity contributions) follow this pattern.

#### Status

Provisional pending 2026-06-01 architecture review meeting (agenda Item 6). The pattern is purely additive — no existing rule conflicts; §2.2 + §2.20 + §E2.2 already cover it; this section is the worked example, not a new rule.

---

## §E4 Elsa configuration — [DEFERRED]

The Configuration & Settings classification (framework §2.12) is deferred to the **Configuration & Infrastructure follow-up meeting**. Pending Elsa-specific items:

- `appsettings.json` schema conventions for feature-bound options.
- Secrets resolution from Key Vault / managed identity / per-tenant.
- Per-feature vs application-wide implementations of the same contract (Elsa side).
- Helm chart conventions for deploying `Elsa.Server`.

The meeting opens *after* the FastEndpoints / API first refactor pass yields working API to test configuration questions against.

This section will be revised when the follow-up meeting closes.

---

## §E5 Elsa packaging snapshot

**framework §2.13 — Elsa specialization.** Elsa's current packaging is the snapshot in §E2.4 above. The framework rule (packaging cohesion follows dependency cohesion; packaging is application-level and revisable) governs.

**Reversibility.** If, e.g., `Elsa.Serialization.Newtonsoft` and `Elsa.Serialization.SystemText` become demanded by applications outside Elsa, they could graduate into separately published features that Elsa's other features pull in via NuGet. The packaging is reversible per framework §2.16 (refactor-cost test) — preserving NuGet identity insulates consumers from the restructuring.

**Nuplane strategy.** Elsa adopts **Strategy B** per framework §3: the host (`Elsa.Server`) pins `.Core` libraries; Nuplane focuses on dynamically loading Layer-3 implementations, helper libraries, and optional features. Strategy A is not adopted as Elsa's default, but is not hard-excluded for specific deployment contexts.

---

## Governance

### Amendment process

This constitution is amended together with the framework constitution where the change affects both layers. Elsa-only amendments follow framework Governance > Amendment process:

1. **Propose** as a numbered decision in `ARCHITECTURE_v2.md` (or its successor `DECISIONS.md`) in the meta-repo.
2. **Discuss** with Sipke + Frans.
3. **Ratify** by consensus; fold into this document with the next version bump.
4. **Propagate** to speckit templates (`plan-template.md`, `spec-template.md`, `tasks-template.md`) and any runtime guidance.

### Sync rule with framework constitution

This document declares the framework constitution version it derives from in the header (currently **v2.0.0**). When the framework constitution bumps:

- **PATCH** — re-pin the version; review for clarification impact; no Elsa SemVer bump unless wording downstream of an Elsa specialization is affected.
- **MINOR** — re-pin the version; review every Elsa specialization for compatibility with new framework guidance.
- **MAJOR** — re-pin the version; full review pass; Elsa constitution typically bumps MAJOR in sync.

### SemVer of this constitution

Same rules as framework §4.2 applied to constitutional content:

- **PATCH** — clarifications, wording, typo fixes.
- **MINOR** — new section added or materially expanded Elsa-specific guidance.
- **MAJOR** — backward-incompatible removals or redefinitions of Elsa-specific rules.

### Compliance and review

- Plans and specs generated against this constitution must satisfy a Constitution Check that loads **both** this file and `constitution-framework.md`.
- CI is expected to enforce naming conventions, dependency-envelope assertions (notably the Workflows.Design ↔ Workflows.Runtime asymmetry of §E2.2), and namespace-segment forbids.
- Where AI cannot apply a rule cleanly, that is the signal to escalate — Joey + Sipke + Frans intervene, analyse, decide on a new rule. The constitution matures via this loop (Definition of Done point 2).

---

**Version:** 3.0.0 | **Ratified:** TODO(RATIFICATION_DATE) | **Last Amended:** 2026-06-03 | **Derives from framework constitution:** v3.0.0
