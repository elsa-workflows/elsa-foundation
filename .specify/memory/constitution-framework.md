<!--
Sync Impact Report — Modular Software Design Framework Constitution
=====================================================================

Version change: 2.0.0 (draft, never ratified) → 3.0.0 (draft)
  SemVer: MAJOR.
  Rationale: Unit 1 (2026-06-02) collapsed the three in-process pub/sub
  concepts (`IDomainEvent` + `INotification` + `ILifecycleEvent`) into ONE
  `IEvent` concept (§2.6.1), removed the exception-shielding-by-default
  position (default Sequential publish CAN break the caller; resilience is the
  Background strategy's job), and moved event handling into its own library
  family (`Elsa.Events.Core` / `Elsa.Events` over shared `Elsa.Pipelines.Core`,
  separate from command/request `Elsa.Mediator.*`). §2.6.3 un-parked
  `IEventPublisher` as THE in-process mechanism; §2.6.6 restated the
  two-concept table as a single concept + delivery-strategy taxonomy. Any
  plan, spec, or citation referencing `IDomainEvent` / `INotification` /
  `ILifecycleEvent` / their senders must rewrite to `IEvent` / `IEventHandler`
  / `IEventPublisher` + a delivery strategy.

  [Prior bump] 1.0.0 → 2.0.0: §2.6 family restructured. §2.6.1 redefined from
  "Replacement vs Contribution contracts" to "Domain events — the contribution
  mechanism"; old §2.6.1 content moved to new §2.6.2 (Replacement contracts
  only). v1.0.0 was never ratified; impact confined to draft-state amendments,
  but the SemVer classification stands per §4.2.

v2.0.0 provenance — consolidated fold of:
  1. The v1.1.0 amendment plan drafted 2026-05-19 (never folded as v1.1.0).
  2. Sipke's 2026-05-26 architectural-clarification items 4, 5, 12 (from
     `2026-05-26_ENTITY_DESIGN_RESPONSE_SIPKE.md`). Items 1, 2, 3, 6, 7, 8, 9,
     10, 11, 13 are entity-design substance, deferred to Units B–G.
  3. Matured candidate rules from the entity-design follow-up (Rule A, Rule B
     → Elsa §E2.6) and the Elsa 3 compatibility follow-up (CR-COMPAT reframed
     → Elsa §E2.7).

Unit 1 amendment (2026-06-02 — "Unified event sending"):
  - §2.6 family REWRITTEN around a single event concept. The three prior
    in-process pub/sub markers (`IDomainEvent`, `INotification`,
    `ILifecycleEvent`) and their senders (`IDomainEventSender`,
    `INotificationSender`, `ILifecycleEventSender`) are DELETED, replaced by
    `IEvent` + `IEventHandler<T>` (`Task Handle`) + `IEventPublisher.Publish`
    with a pluggable `IEventPublishingStrategy`.
  - §2.6.1 retitled "Events — the in-process pub/sub + contribution mechanism."
    Default dispatch is Sequential, synchronous, awaited, and **CAN break the
    caller** — the exception-shielding-by-default position (old Unit C Phase-6)
    is REVERSED. The default path ships NO shielding middleware; resilience
    lives ONLY in the Background strategy + `BackgroundEventPublisher`. The
    contribution sub-pattern (intent-revealing `Add` methods, read-back) is
    retained, now framed as "an event published Sequential."
  - §2.6.3 retitled "Named events, not anonymous generic dispatch." `IEventPublisher`
    is un-parked and is THE in-process mechanism; what's forbidden is anonymous
    indirection where the expected handler/event is not a named type in a
    domain's `.Core`. Read-back requires Sequential + a contribution payload.
  - §2.6.6 retitled "Delivery strategies — one event concept, three dispatch
    behaviours." The two-concept table + "don't conflate" rule + `ILifecycleEvent`
    are replaced by a strategy taxonomy: Sequential (default, propagates) /
    Parallel / Background (isolated). The `OnXxxing`/`OnXxxed` hybrid is
    restated as Sequential gate + Background outcome (both `IEvent`).
  - §2.22.1 + §2.24.2 (rows 3/3a–c/4/9) reworded to the single-concept model;
    Strategy worked example is now `IEventPublishingStrategy`.
  - Library home: events live in `Elsa.Events.Core` (contracts) / `Elsa.Events`
    (impl) over shared `Elsa.Pipelines.Core`, separate from command/request
    dispatch (`Elsa.Mediator.Core` / `Elsa.Mediator`). Rationale: event handling
    is a core-domain concern, command/request dispatch is an API concern.
  - Elsa §E3.x worked examples realigned to the unified naming.

Unit 1 addendum (2026-06-02 — "Contributor interfaces + intent-method revert";
folded into the 3.0.0 draft):
  - §2.6.1 sub-pattern REPLACED. The Phase-3 "intent-revealing `AddX()` methods +
    private list + `IReadOnlyList<T>` read accessor" sub-rule is WITHDRAWN (too
    much ceremony; it grew every contribution event and folded contribution logic
    into the payload). Contribution events now expose a **directly-accessible
    `ICollection<T>`** (or a rich mutable context) written solely by ONE
    aggregating handler. Joey's call 2026-06-02: "just use ICollection that is
    directly accessible by event handlers."
  - §2.6.1 NEW sub-pattern "Contributor interface + single aggregating handler."
    Each fan-in event gets ONE action-named `IEventHandler<On<Phase>>` (e.g.
    `ExecuteValidations`, `RegisterJsonConverters`) injecting
    `IEnumerable<TContributor>`; features implement + DI-register a contributor
    interface instead of shipping their own handler. Sipke proposed 2026-06-01;
    Joey confirmed 2026-06-02. Centralises contribution logic in the pipeline,
    drops per-feature handler sprawl.
  - Naming: contributor interfaces split by method shape into THREE kinds —
    **`I<X>Source`** (the impl RETURNS its items; flat-collection sink) vs
    **`I<X>Contributor`** (the impl RECEIVES a context and ACTS on it via
    `Contribute`; rich-context sink) vs **`I<X>PreProcessor`/`I<X>PostProcessor`**
    (the impl ACTS on a lifecycle context at the *before*/*after* event of an
    OnXxxing/OnXxxed pair). "Source" is preferred over "Provider." Joey
    2026-06-02: "Sources always return something, they dont act on an object.
    Make a distinction between these two concepts." Joey 2026-06-02 on the pre/post
    kind: "especially when there are OnBefore and OnAfter events, the pre/post
    processor interface naming is much more suitable" — the JS script-evaluation
    cluster (`OnEvaluatingScript`/`OnScriptEvaluated`) is the canonical example.
  - §2.24.2 row 3b REWRITTEN: was "Intent-revealing methods on events" → now
    "Contributor interface + single aggregating handler" (architect-ratified
    addition per the §2.24.3 gate: Sipke 2026-06-01, Joey 2026-06-02).
  - §2.22 + §2.22.1 EXTENDED: per-feature docs MUST list the contributor
    interfaces a feature implements; the events catalog MUST document each
    fan-in event's contributor interface (Source vs Contributor, signature, DI
    registration note).
  - Elsa §E3.3 / §E3.7 / §E3.10 worked examples realigned to the new pattern.
  - Code cascade: the five fan-in clusters (validation, JSON converters, JS
    declarations, JS runtime functions, activity resolvers/descriptors) plus
    the two reconciliation events refactored to the new shape; the four
    contribution events flattened to directly-accessible `ICollection<T>`.
  - Code cascade follow-up (Joey 2026-06-02): the JS script-evaluation cluster
    moved from the Contributor kind to the PreProcessor/PostProcessor kind —
    `IScriptEvaluationContributor` split into `IScriptPreProcessor` (at
    `OnEvaluatingScript`, handler `PreProcessScript`) + `IScriptPostProcessor`
    (at `OnScriptEvaluated`, handler `PostProcessScript`); the variable copy-back
    became the canonical post-processor; the Jint adapter now publishes
    `OnScriptEvaluated` after evaluation (it previously published only the before
    event). The JS declarations context `IJavaScriptRenderingContext` was renamed
    `IJavaScriptDeclarationsContributionContext` so the name states its purpose.

Unit 1 extension follow-up (2026-06-03 — "Startup-task cross-domain contributions + fan-in rule scope clarification"):
  - §2.6.1 sub-pattern CLARIFIED — the contributor-interface + single-aggregating-handler
    rule is scoped to the *contribution axis* (fan-in flows). Features are explicitly
    permitted to register `IEventHandler<T>` for independent purposes (auditing, cache
    invalidation, cross-cutting reactions). The rule prevents scattered handlers all doing
    the same fan-in contribution; it does not restrict general event observation.
  - §2.22.1 / README pattern EXTENDED — `IStartupTask`, `IRecurringTask`, `IBackgroundTask`
    implementations are cross-domain contributions on equal standing with
    `IEntitySavingHandler`, `IScriptPreProcessor`, etc. Features implementing task
    interfaces from `Elsa.Tasks.Core` MUST list them in their README's
    "Cross-domain contributions" section. Documentation cascade: READMEs created/updated
    for Elsa.Serialization, Elsa.Persistence.EFCore, Elsa.Workflows.Design.Reconciliation,
    Elsa.Events, Elsa.Activities.Design.Reconciliation, Elsa.Activities.Runtime.
  - Root EXTENSION_POINTS.md "universal rule" wording corrected to scope the restriction
    to fan-in contribution flows; independent subscriptions stated as unrestricted.

Unit 1 extension (2026-06-03 — "Per-domain EXTENSION_POINTS.md rollout + intra/cross-domain pattern"):
  - §2.6.1 EXTENDED — "Intra-domain vs. cross-domain contributions" named as a
    formal pattern: intra-domain default = same domain's feature implements its
    own Core's contract; cross-domain contribution = different domain implements
    another domain's Core contract (the inter-domain dependency map). Owning
    feature's EXTENSION_POINTS.md MUST list all Known implementations tagged
    accordingly; contributing feature MUST add a "Cross-domain contributions"
    section to its README with a link back.
  - §2.22.1 EXTENDED — Catalog placement clarified: catalog lives at the
    composition-root feature project (NOT .Core, which is contracts-only);
    exception for domains with no separate feature project. Format spec extended:
    every entry gains a layer badge (*(Core — Proj)*  vs *(Feature contract)*);
    contributor-interface entries gain a Known implementations list with
    intra/cross-domain tags.
  - §2.22.1 MANDATORY maintenance obligation NEW — five trigger conditions:
    (a) new interface, (b) new implementation (intra or cross), (c) rename/remove,
    (d) new aggregator, (e) new feature/.Core project. CatalogParityTests covers
    events; code review covers contributor-interface drift.
  - §2.22.1 worked example UPDATED — 24 per-domain catalogs at composition-root
    feature projects; catalog set declared complete for current domain set.
  - §2.22.2 UPDATED — "inlined until migrated" clause removed; index is pure
    links (no inline entries); "one per .Core root" → "one per composition-root
    feature project."
  - Documentation cascade: 24 new/updated EXTENSION_POINTS.md catalogs across
    all domains; root EXTENSION_POINTS.md converted to pure-links index with
    24-row domain table; CatalogParityTests extended with 7 new assembly→catalog
    pairs + multi-assembly catalog support (union check on reverse direction);
    2 catalogs relocated from .Core to composition-root feature projects
    (Workflows.Design.Core → .Api; Validations.Core → .Validations); cross-domain
    README sections added to 8 feature projects.

Unit 1 consistency fix (2026-06-03 — "EF Core save/load on the contributor +
single-aggregating-handler shape"):
  - §2.6.1 extended — action-named contributor suffixes (`…Validator`,
    `…Handler`) are now explicitly sanctioned alongside Source / Contributor /
    PreProcessor / PostProcessor. They are context-receiving (Contributor-kind)
    but keep an action-specific, intent-revealing name; the single aggregating
    `IEventHandler<OnXxx>` still owns the event subscription.
  - §2.22.1 broadened — the per-domain `EVENTS.md` becomes a per-domain
    `EXTENSION_POINTS.md` with three sections (Overridable contracts /
    Implementable contributor interfaces / Events), distinguishing the two
    extension axes: **override** (replace a `.Core` contract's default impl) vs
    **extend** (add a contributor the single aggregating handler runs). Events
    are absorbed as the third section. The three existing `EVENTS.md` files
    (Workflows.Design.Core, Workflows.Design.Validations.Core, Persistence.EFCore)
    were converted; the `CatalogParityTests` filename was retargeted.
  - §2.22.2 NEW — the repo-wide `EXTENSION_POINTS.md` is recognised as an
    **index** alongside per-feature READMEs and the per-domain catalogs: it maps
    every extension point and links to the owning domain's `EXTENSION_POINTS.md`
    for detail (inlining only the not-yet-migrated domains). Index and per-domain
    catalogs share the filename, distinguished by location (repo root vs `.Core`
    project root).
  - Code cascade: the EF Core entity save/load extension points finished their
    migration off the "coexist" half-state onto the canonical contributor +
    single-aggregating-handler shape (mirror of `IDraftValidator` +
    `ExecuteValidations`). `Elsa.Persistence.EFCore` now ships two single
    aggregators — `ApplyEntitySavingHandlers : IEventHandler<OnEntitySaving>`
    and `ApplyEntityLoadingHandlers : IEventHandler<OnEntityLoading>` (registered
    once by `EFCorePersistenceShellFeatureBase` via `TryAddEnumerable`) — that
    reflect the typed `IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>`
    contributors over the runtime DbContext + entity types. The legacy
    direct-dispatch loops in `ElsaDbContextBase` / `EFCoreQueries` were removed;
    `ActivityDefinitionVersionSavingHandler` was re-homed onto the typed
    `IEntitySavingHandler<,>`; mutate-then-save commands (`UpdateDraft`) now
    publish `OnEntityLoading` Sequential against their own tracked context rather
    than hand-rolling a handler loop. `Elsa.Persistence.EFCore` gains an
    `EXTENSION_POINTS.md`; the repo root gains the `EXTENSION_POINTS.md` index.
    The out-of-band
    `IGlobalEntitySavingHandler` + `IEntityModelCreatingHandler` remain on their
    own dispatch mechanisms (unchanged).

Unit C Phase-8 amendment (2026-05-29, draft pending 2026-06-01 ratification):
  - §2.24 NEW — "Sanctioned patterns — the closed catalog." Articulates Joey's
    2026-05-29 rule: the framework recognises a closed catalog of architectural
    patterns; code MUST resolve problems using a pattern from the catalog;
    new patterns require architect evaluation + documented use case + criteria
    + worked example + ratification before adoption. Random ad-hoc patterns
    are not permitted. §2.24.1 carries the rationale (predictability,
    AI-session continuity, review surface). §2.24.2 carries the catalog as a
    table cross-referencing the existing § identifiers, plus two new
    catalogue rows — **Strategy** (already in implicit use at §2.6.6's
    publishing strategies; codified for general use) and **Factory**
    (candidate; promotion pending Monday's discussion). §2.24.3 carries the
    gate for adding a new pattern. Cross-references the agenda Item 7.

Unit C Phase-5 amendment (2026-05-28, draft pending 2026-06-01 ratification;
filename + scope refined 2026-05-29):
  - §2.22.1 NEW sub-rule — "Domain-level events catalog." Every domain whose
    `.Core` publishes events (domain events under §2.6.1 AND/OR lifecycle
    events under §2.6.2) MUST ship an events catalog at the `.Core` project
    level documenting every event with category, semantic, payload,
    publication site, expected handlers, ordering guarantees, and
    cross-references. The catalog distinguishes the two categories under
    separate headings — domain events (contribution; publisher reads back)
    vs lifecycle events (notification; fire-and-forget by default).
    Complements §2.22's per-feature documentation requirement — the catalog
    is the domain-level index for "what events does this domain publish?".
    Form is application-defined; recommended `EVENTS.md` at the `.Core`
    project root (renamed from the original `DOMAIN_EVENTS.md` on 2026-05-29
    once the two-category split landed; the prior name was misleading
    because lifecycle events are not domain events). Worked examples: Unit C
    creates `src/Elsa.Workflows.Design.Core/EVENTS.md` (all lifecycle) and
    `src/Elsa.Workflows.Design.Validations.Core/EVENTS.md` (mixed —
    `OnDraftValidating` domain, `OnDraftValidated` lifecycle).

Unit C Phase-6 amendment (2026-05-28, draft pending 2026-06-01 ratification):
  - §2.6.1 EXTENDED — "Visibility" bullet REWRITTEN as
    "Visibility — subscriber MUST NEVER break publisher." Articulates Joey's
    2026-05-28 rule (clarify Q1, fold session 2): cross-domain *failure
    coupling* is forbidden; a handler exception MUST NOT propagate to the
    publisher's caller and MUST NOT prevent the remaining handlers from
    running. Adds the **default + escape hatch** framing: framework ships an
    exception-shielding middleware as the default; an engineer composing a
    custom pipeline MAY swap or remove it for aggregate-throw / fail-fast /
    retry / dead-letter semantics. Adds explicit "Handler independence" and
    "Diagnostics" bullets articulating the corollary rules.
  - Code cascade (this branch, Elsa.Mediator domain-event pipeline):
      - NEW: `DomainEventHandlerIteratorMiddleware` — resolves handlers,
        iterates them, sets `IDomainEventContext.CurrentHandler` per
        invocation, calls `next(context)` per handler. Enforces §2.6.1
        completeness rule (every registered handler dispatched).
      - NEW: `DomainEventExceptionShieldingMiddleware` — wraps each
        per-handler invocation in `try/catch`, logs with handler+event
        context, swallows. Default mechanism for the new rule.
      - REFACTORED: `DomainEventHandlerInvokerMiddleware` — no longer
        iterates; invokes only `context.CurrentHandler`. Iteration lifted
        upstream so the shielding middleware can sit between iterator and
        invoker per-handler-invocation-wise.
      - EXTENDED: `IDomainEventContext` gains `CurrentHandler` (mutable
        per-handler state, analogous to `HttpContext.User` in
        ASP.NET Core). `DomainEventContext` record updated.
      - REPLACED: `DomainEventPipeline.CreateDefaultPipeline()` now
        composes `Iterator → ExceptionShielding → Invoker`.
  - Cross-mediator note: the same architectural shape (Iterator + per-call
    Shielding + Invoker) generalises to any multi-handler dispatch — i.e.
    request handlers, command handlers, notification handlers, etc. Other
    mediator variants in this codebase (commands, requests) currently have
    single-handler invokers; if a multi-handler variant lands, it inherits
    this rule and the same default mechanism. Code application to those
    variants is out of Unit C's scope; constitutional codification covers
    all of them via this §2.6.1 sub-rule.
  - Test obligation per §2.23.2 (per-implementation branch coverage):
    the three new middleware classes (Iterator, Shielding, Invoker)
    require branch-covered unit tests. Tracked as a follow-on item in the
    Unit C follow-up; not blocking ratification.

Unit C Phase-3 amendment (2026-05-28, draft pending 2026-06-01 ratification):
  [SUPERSEDED by the Unit 1 addendum 2026-06-02 — the intent-revealing-methods
  sub-pattern was withdrawn; contribution events now expose a directly-accessible
  `ICollection<T>` with a single aggregating handler. Retained here for history.]
  - §2.6.1 EXTENDED — NEW sub-pattern "Domain events expose intent-revealing
    methods, not raw collections." Codifies Joey's 2026-05-28 articulation:
    domain events that gather handler contributions expose method-based
    contribution APIs (e.g. `AddVersion(...)`, `AddValidationError(...)`)
    rather than public mutable collections. Backing collection is private;
    read access is via a public `IReadOnlyList<T>` property — non-mutating by
    type, so public visibility is safe and `InternalsVisibleTo` is NOT
    required (avoided as default). Events MUST be `sealed class` (not
    `record`) to enforce encapsulation. Smell heuristic: too wide a variety
    of methods on one event indicates two distinct events that should be
    split.
  - Code cascade (this branch): `OnActivityVersionsReconciling` (Unit B's
    reconciliation event) refactored from record-with-ICollection to
    sealed-class-with-`AddVersion(...)`; the reconciler and JSON handler
    updated accordingly. Same retroactive cascade reasoning as Phase-1's
    Model X rewrite: don't leave two patterns in the codebase.
  - Worked examples queued for §E3.3 rewrite (in Elsa constitution) and Unit
    C's `OnDraftValidating` (with `AddValidationError(ValidationError)` method).

Added sections (framework layer, relative to v1.0.0):
  - §2.2 — "Secondary-domain naming sub-rule" (new subsection within §2.2):
    when a feature only contributes implementations of another domain's
    contributor/provider interfaces, the model-owning domain wins the prefix
    (`<App>.<ModelDomain>.<ConsumerDomain>`). Cross-references Elsa §E3.8.
  - §2.6 — Restructured umbrella: "Cross-feature composition mechanisms".
    §2.6 head gained the "no tight logic coupling between implementations" rule.
      - §2.6.1 — NEW (was: Replacement vs Contribution contracts). Now:
        "Domain events — the contribution mechanism". Includes the Registry +
        StartUp Task sub-pattern for sync access. Includes the dispatch
        semantics ("sync = awaited end-to-end", not single-threaded sync).
        Includes the inheritance-chain scope note (events apply within
        specialization chains, not only cross-domain). Includes the
        feature-documentation cross-reference to §2.22.
      - §2.6.2 — NEW. Replacement contracts only (the prior §2.6.1's
        "Replacement contracts" content, narrowed; the "Contribution contracts"
        content is gone — contribution flows through §2.6.1).
      - §2.6.3 — NEW. "Generic dispatch is not a coupling mechanism."
        `IMediator` / `IEventBus` / `INotificationSender` for fire-and-forget
        pub/sub only; specific-handler expectations go through §2.6.1.
      - §2.6.4 — NEW. "Design-time vs runtime contract split." When a contract
        has two phase-bound consumers, split into two contracts; orthogonal to
        the §2.6.1/§2.6.2 mechanism choice. Worked example: Elsa §E3.7.
      - §2.6.5 — NEW. "Sync contributor pattern — rare exception." Codifies
        the narrow case where a provider interface resolved via DI as
        `IEnumerable<TContributor>` is permitted because the contribution flow
        is structurally incompatible with both §2.6.1 (domain events, async)
        and its Registry + StartUp Task sub-pattern. Three criteria must all
        hold: intrinsically sync dispatch site, contributor contributes
        behaviour (not data), Registry + StartUp Task doesn't apply. Canonical
        worked example: EF Core's `OnModelCreating` hook + `IEntityModelCreatingHandler`
        (also recorded in Elsa §E3.9). Reviewers MUST challenge §2.6.5
        invocations; the exception is rare.
  - §2.9 — Persistence invariants paragraph (appended): invariants defined
    independently of the persistence provider; `*.Persistence.Core` is the
    provider-agnostic surface; provider-specific mechanism lives in
    `*.Persistence.<Provider>`.
  - §2.9.1 — NEW (Unit B fold, 2026-05-28). "Domain-level shadow properties
    — real properties on entities, hidden at the interface boundary."
    Persistence-only fields (serialised forms, denormalised lookups, backing
    strings for `[NotMapped]` projections) MUST be real CLR properties on the
    entity, not provider-side shadow properties. The interface controls
    cross-domain visibility; provider shadow features bypass cross-cutting
    attribute scanners (e.g. `[Immutable]`) and scatter the entity's surface
    area into provider configuration. Provider shadow properties remain valid
    only for fields that genuinely don't belong on the CLR class.
  - §2.23.5 — NEW (Unit B fold, 2026-05-28). "Exception boundaries —
    infrastructure exceptions are wrapped." `JsonException`, `DbUpdateException`,
    third-party-library exceptions etc. MUST NOT escape a feature boundary
    unwrapped. Translate at the infrastructure call site into a domain-scoped
    exception with diagnostic context (row id, entry index, asset name).
    Reviewers MUST challenge raw infrastructure exceptions crossing feature
    boundaries.
  - §2.23.5 (former) → §2.23.6 — renumbered (Integration testing — out of scope).
  - §2.20 — Rule 3 (appended): feature modules MUST NOT depend on concrete
    provider implementations unless the feature is itself provider-specific.
  - §2.22 — NEW: "Feature documentation." Placeholder section. Minimum required
    content: domain event handlers registered, tasks registered (startup,
    recurring, scheduled). Expansion (settings, services registered, inheritance
    relationships) deferred to future amendments.
  - §2.23 — NEW: "Unit tests." Registration test (§2.23.1) + per-implementation
    branch-covered tests (§2.23.2) + visibility rule (§2.23.3) + refactoring
    obligations inherited from §2.21.1 (§2.23.4) + integration testing
    out-of-scope (§2.23.5). The §2.23.4 "tight logic coupling" diagnostic
    cross-references the §2.6 head rule.

Renamed sections:
  - §2.6 head: "Provider Interface Pattern" → "Cross-feature composition mechanisms".
  - §2.6.1: "Replacement vs Contribution contracts" → "Domain events — the
    contribution mechanism" (semantic redefinition; old content moved).

Cross-reference updates:
  - §2.7.1 — "(see §2.6.1)" → "(see §2.6.2)" for contract-kind declaration.
  - §2.11 — "see §2.6.1 and §3" → "see §2.6.2 and §3" for DI graph validation.

Removed sections: none.

Plan-template gates:
  - G1–G20 retained from v1.0.0; G5 wording sharpens but its semantic citation
    (now §2.6.2) is preserved.
  - G21–G30 ADDED for new v2.0.0 rules. Process-line range updated G1–G20 → G1–G30.

Follow-up TODOs:
  - TODO(RATIFICATION_DATE) — v2.0.0 is the target ratification version
    (was v1.0.0; superseded). Awaiting Joey + Sipke + Frans formal ratification.
  - §2.12 Configuration & Settings Classification — carry-over deferral.
  - §2.22 Feature documentation expansion — placeholder; future amendments
    will codify settings, services-registered inventory, inheritance
    relationships.
  - Integration-testing rule — deliberately not in §2.23; scoped in
    `follow-up-items/2026-05-19_testing_infrastructure.md`.
  - Compile-domain rules from `follow-up-items/2026-05-11_workflow_execution_seam.md`
    (CR-1, CR-3, CR-3a, CR-4, CR-5) — deferred to Units B–G; their final
    framing depends on entity-design substrate.

Code-side commitments (tracked in Unit A follow-up
`follow-up-items/2026-05-27_unitA_constitution_catchup.md`; required before
ratification):
  - Migrate `IExpressionDescriptorProvider`, `IPayloadSerializerConverterProvider`,
    EF Core entity handlers to Registry + StartUp Task + Domain Event pattern.
  - Delete `Elsa.Expressions.JavaScript.Jint3` (test scaffolding).
  - Update plan-template gate G24 entry (renumber if needed when v1.1.0-era
    plans surface).
  - Update entity-design summary doc `2026-05-24_ENTITY_DESIGN_SUMMARY_JOEY.md`
    per Sipke 2026-05-26 items 8 and 9 ("read-only interfaces" wording;
    Tier-2-churn argument narrowing).

Structural deviation from speckit template: unchanged from v1.0.0. The numbered
legal-document structure is preserved.

---  (v1.0.0 SIR retained below for history)

Version change: (initial) → 1.0.0
  Initial v1 population from the empty speckit template. Substance migrated from
  ../elsa-foundation-project-management/epic1-elsa-refactor-constitution/ARCHITECTURE_v2.md
  (now drafting archive) per decision D26 (2026-05-08 triage row 1).

Added sections (relative to the empty speckit template):
  - Preamble — purpose, two-layer split, derivation contract.
  - Glossary — Host, Module, Feature, Domain, Application, Foundation repo, .Core,
    Thin implementation, Heavy dependency, Bundle (retired), Capability/Envelope (retired),
    Multiple-features-per-module rule.
  - §1 Anti-patterns the framework prevents.
  - §2 The Architecture (twenty rules §2.1–§2.20):
      §2.1  Three-Layer Separation per feature
      §2.2  Naming Convention
      §2.3  Framework Primitives Library
      §2.4  Helper Libraries (domain-owned)
      §2.5  Feature Inheritance
      §2.6  Provider Interface Pattern (incl. §2.6.1 Replacement vs Contribution)
      §2.7  Adapter / Bridge (incl. §2.7.1 Decision rule)
      §2.8  Extension methods decision framework
      §2.9  Persistence Base Context — application-level
      §2.10 CQS at Persistence Boundary
      §2.11 No DependsOn — fail fast
      §2.12 Configuration & Settings Classification [DEFERRED]
      §2.13 Packaging and Versioning — application-level
      §2.14 Integration vs Consumption-Shape
      §2.15 Repository organisation (foundation repo + multi-repo preference)
      §2.16 Refactor-cost test
      §2.17 Duplication beats dependency
      §2.18 Methodology for refactoring a modular monolith
            §2.18.0 Shape of the methodology (framing + falsifiability sidebar)
            §2.18.1 Step 1 — Identify the domains (verb-led-sentence test)
            §2.18.2 Step 2 — Extract the implementations (cross-domain consumption test)
            §2.18.3 Step 3 — Resolve cross-domain reuse (inductive feedback)
            §2.18.4 Refactor-cost discipline (cross-refs to §2.16 + §2.21.1)
      §2.19 Feature identity
      §2.20 Provider module decomposition (NEW — promoted from feedback memory 2026-05-10)
      §2.21 Test discipline (NEW — refactor golden rule + greenfield deferral)
  - §3 Runtime composition (Nuplane Strategy A vs B + restart criteria).
  - §4 Versioning (§4.1 Per-Package Versioning, §4.2 SemVer for .Core Libraries).
  - Governance — amendment process, SemVer of the constitution, compliance review,
    public-release notes.

Removed sections: N/A (initial population).
Renamed sections: N/A.

Templates updated:
  ✅ .specify/templates/plan-template.md — Constitution Check rewritten with 20 gates
     G1–G20 citing specific framework/Elsa § identifiers (G17–G19 cover §2.8/§2.10/§2.14;
     G20 covers §2.21.1 refactor test-survival).
  ✅ .specify/templates/spec-template.md — Constitutional Compliance pointer added.
  ✅ .specify/templates/tasks-template.md — Constitutional Compliance pointer added.
  N/A .specify/templates/commands/*.md — directory does not exist in this speckit install.
  ✅ /CLAUDE.md (elsa-foundation) — constitution pointers updated.
  ✅ ../elsa-foundation-project-management/CLAUDE.md — constitution pointers updated.

Navigation:
  Top-of-file Table of Contents added; uses GFM auto-anchors. If a renderer ever strips
  the § symbol or em-dashes differently, fall back to explicit <a id="…"></a> markers
  before each heading.

Structural deviation from speckit template (justified, intentional):
  The speckit constitution-template.md uses a "5 Core Principles + 2 sections + Governance"
  shape. This constitution is a numbered legal-document with 20+ rules organised by §
  identifier. The deviation is intentional and load-bearing:
    (a) v2's content density does not compress into 5 short principles;
    (b) plan-template.md's Constitution Check cites specific § identifiers (G1–G20), which
        requires this numbered structure;
    (c) the speckit-constitution skill explicitly permits varying the principle count
        ("the user might require less or more principles than the ones used in the
        template").
  Future speckit-constitution runs MUST preserve this structure; do NOT revert to the
  5-principle pattern.

Memory promotion executed:
  - feedback_provider_module_decomposition (validated 2026-05-10) → §2.20.
  - (paired): project_workflows_bounded_context → Elsa §E2.2 in constitution.md.

Follow-up TODOs:
  - TODO(RATIFICATION_DATE) — awaiting Joey + Sipke + Frans formal ratification per
    Definition of Done point 1.
  - §2.12 Configuration & Settings Classification — awaiting the Configuration &
    Infrastructure follow-up meeting (Elsa §E4 mirrors).
-->

# Modular Software Design Framework Constitution

**Version:** 3.0.0 (draft)
**Status:** Draft for ratification by Joey Barten, Sipke Schoorstra, Frans van Ek.
**Layer:** Generic framework constitution. The Elsa workflow-engine constitution derives from this document — see `constitution.md`.

---

## Table of Contents

- [Preamble — What this document is](#preamble--what-this-document-is)
- [Glossary (working definitions)](#glossary-working-definitions)
- [§1 Anti-patterns the framework prevents](#1-anti-patterns-the-framework-prevents)
- [§2 The Architecture](#2-the-architecture)
  - [§2.1 Three-Layer Separation — applied per feature](#21-three-layer-separation--applied-per-feature)
  - [§2.2 Naming Convention](#22-naming-convention)
  - [§2.3 The Framework Primitives Library](#23-the-framework-primitives-library)
  - [§2.4 Helper Libraries — domain-owned](#24-helper-libraries--domain-owned)
  - [§2.5 Feature Inheritance](#25-feature-inheritance)
  - [§2.6 Cross-feature composition mechanisms](#26-cross-feature-composition-mechanisms) · [§2.6.1 Events — the in-process pub/sub + contribution mechanism](#261-events--the-in-process-pubsub--contribution-mechanism) · [§2.6.2 Replacement contracts — single-implementation services](#262-replacement-contracts--single-implementation-services) · [§2.6.3 Named events, not anonymous generic dispatch](#263-named-events-not-anonymous-generic-dispatch) · [§2.6.4 Design-time vs runtime contract split](#264-design-time-vs-runtime-contract-split)
  - [§2.7 Adapter / Bridge — as a design default](#27-adapter--bridge--as-a-design-default) · [§2.7.1 Decision rule](#271-decision-rule--inheritance-adapter-or-providercontributor)
  - [§2.8 Extension Methods — Decision Framework](#28-extension-methods--decision-framework)
  - [§2.9 Persistence Base Context — application-level](#29-persistence-base-context--application-level)
  - [§2.10 CQS at the Persistence Boundary](#210-cqs-at-the-persistence-boundary)
  - [§2.11 No DependsOn — Fail Fast](#211-no-dependson--fail-fast)
  - [§2.12 Configuration & Settings Classification — \[DEFERRED\]](#212-configuration--settings-classification--deferred)
  - [§2.13 Packaging and Versioning — application-level](#213-packaging-and-versioning--application-level)
  - [§2.14 Integration vs. Consumption-Shape — separate modules](#214-integration-vs-consumption-shape--separate-modules)
  - [§2.15 Repository organisation — foundation repo + multi-repo preference](#215-repository-organisation--foundation-repo--multi-repo-preference)
  - [§2.16 Refactor-cost test](#216-refactor-cost-test)
  - [§2.17 Duplication beats dependency](#217-duplication-beats-dependency)
  - [§2.18 Applying the Framework — Methodology for Refactoring a Modular Monolith](#218-applying-the-framework--methodology-for-refactoring-a-modular-monolith) · [§2.18.0 Shape](#2180-shape-of-the-methodology) · [§2.18.1 Step 1](#2181-step-1--identify-the-domains) · [§2.18.2 Step 2](#2182-step-2--extract-the-implementations) · [§2.18.3 Step 3](#2183-step-3--resolve-cross-domain-reuse) · [§2.18.4 Refactor-cost discipline](#2184-refactor-cost-discipline)
  - [§2.19 Feature identity — the feature `name`](#219-feature-identity--the-feature-name)
  - [§2.20 Provider module decomposition](#220-provider-module-decomposition)
  - [§2.21 Test discipline](#221-test-discipline) · [§2.21.1 Golden rule of refactoring](#2211-the-golden-rule-of-refactoring) · [§2.21.2 Greenfield deferral](#2212-greenfield-test-discipline)
  - [§2.22 Feature documentation](#222-feature-documentation)
  - [§2.23 Unit tests](#223-unit-tests) · [§2.23.1 Feature-class registration test](#2231-feature-class-registration-test) · [§2.23.2 Per-implementation unit test](#2232-per-implementation-unit-test-with-stubbed-dependencies) · [§2.23.3 Visibility rule](#2233-visibility-rule) · [§2.23.4 Refactoring obligations](#2234-refactoring-obligations-inherited-from-2211) · [§2.23.5 Integration testing — out of scope](#2235-integration-testing--out-of-scope)
  - [§2.24 Sanctioned patterns — the closed catalog](#224-sanctioned-patterns--the-closed-catalog) · [§2.24.1 Why this rule](#2241-why-this-rule) · [§2.24.2 The catalog](#2242-the-catalog-snapshot-2026-05-29-pending-2026-06-01-ratification) · [§2.24.3 Adding a new pattern](#2243-adding-a-new-pattern)
- [§3 Runtime composition — Nuplane Strategy](#3-runtime-composition--nuplane-strategy)
- [§4 Versioning](#4-versioning)
  - [§4.1 Per-Package Versioning](#41-per-package-versioning)
  - [§4.2 SemVer for `.Core` Libraries](#42-semver-for-core-libraries)
- [Governance](#governance)

---

## Preamble — What this document is

This is the constitution of a **Modular Software Design Framework** — using Nuplane and CShells as supporting technologies — and the rules it establishes apply to any application built on that framework. The rules in this document are framework-neutral. They prescribe how an application is decomposed into modules and features, how those units depend on each other, how they are packaged and versioned, and how composition happens at runtime.

The framework's intent is to expose the latent capabilities inside any modular application as independently consumable, independently versioned units. Workflows, scheduling, expressions, persistence, messaging, authentication: each becomes a feature of an application host, not a fixed pillar of one product.

This document is the **generic** layer of a two-layer constitution. An application built on this framework writes its own derived constitution that:

- Cites a specific version of this document.
- Pins its **root domain name** (the `<App>` token used throughout this constitution).
- Adds **specializations** where the generic rule needs application-specific refinement.
- Carries the application's **worked examples** — instantiations of the generic rules using the application's concrete names.

Where the application's constitution is silent, this document applies. Where it overrides, the application's constitution declares the override explicitly with the convention `framework §X — <App> specialization: …`.

---

## Glossary (working definitions)

These terms have a single agreed meaning across the constitution. *Capability* and *envelope* are retired vocabulary; use *feature* and *module* respectively.

| Term | Definition | Concrete form |
|---|---|---|
| **Host** | The application. The thing that runs and that hosts modules. | An ASP.NET Core application (or any other .NET host). |
| **Module** | A logical unit deployed as a single class library / NuGet package. Provides one or more features. **Module identity is its NuGet/package identity** — the unit of deployment, versioning, compatibility, and dependency-graph validation. Source/project references are permitted only as a local development workflow via the workspace/submodule pattern; they must never appear on a release branch or in a published package graph. | A class library; `.dll` and `.nupkg` are the deployment artefacts. |
| **Feature** | A class that implements `IFeature`. Installs services into the host's DI container. | A class named e.g. `<Domain>RuntimeFeature` or `<Domain>PersistenceFeature`. |
| **Domain** | A specialised concept with: a clear intent or goal and a human-understandable scope. A domain is a *mental model* of an area of the application. **A domain can be a single feature, a composition of features, or a composition of domains** (sub-domains are themselves domains). Domains compose by referencing each other's `.Core` libraries (see §2.1). A domain is a mental model first; its boundaries are revisited as parts of it prove reusable elsewhere. | Within an application: a `Scheduling` domain implementable by multiple providers (CRON, queue-based, in-memory); a `Serialization` domain implementable by Newtonsoft, System.Text, etc. |
| **Application** | An instance of the framework — a host that composes a chosen set of domains, features, and configurations into a deployable system. | Each application built on this framework is a separate Application. |
| **Foundation repo** | The umbrella repository that an application maintains as its development-and-deployment baseline: host setup, primitives, the application's main-domain `.Core` libraries, and a set of default features that are implemented whose absence would make local development impractical. Renamed from "core repo" to disambiguate from the per-feature `.Core` library suffix. What specifically lives in it vs. as standalone features is decided by the application's architects and documented in the application's derived constitution. | An application's foundation repo houses host setup, primitives, the main domain `.Core` libraries, and at least one default implementation per domain that requires durable state. |
| **`.Core` (library suffix)** | A `.Core` library contains the contracts (interfaces, abstract classes), models, thin utility implementations, and helper extensions for one feature or one domain. It is the consumable surface other features/domains may reference. Carries no heavy dependencies. **A `.Core` library is nothing more than the `.Core` of a feature** — there is no separate framework-level "Core" concept beyond this. The architects of an application decide which `.Core` libraries are packaged together in the application's foundation repo and which are published as standalone features; those packaging decisions are revisable. | `<App>.<Domain>.Core`, `<App>.<Domain>.<SubDomain>.Core`. |
| **Thin implementation** | A small, dependency-light implementation whose behavior is mechanical rather than domain-decisive: delegation, wrapping, simple default behavior, argument/null guards, option binding, or trivial value transformation. A thin implementation **must not** contain business policy, persistence strategy, infrastructure-specific logic, or branching that encodes meaningful domain decisions. | A `Result<T>` factory helper; a guard like `Guard.AgainstNull(...)`; a wrapper that forwards `IPayloadSerializer` calls to a configured underlying serializer. |
| **Heavy dependency** | A NuGet package that pulls in transitive packages, native binaries, or implies an out-of-process system (Redis, MongoDB, Azure SDK, EF Core database providers other than SQLite, MassTransit, script engines like Jint or Fluid). Heavy dependencies are forbidden in any `.Core` library (§2.1) and gated by the splitting rule for modules (§2.1, multiple-features-per-module rule). |
| **Bundle** | *Not a constitutional concept.* A bundle is a packaging convenience that references other modules with no new functionality — purely a distribution shape. The framework neither prescribes nor forbids bundles; an application chooses whether to publish them. |
| **Capability** | *Retired.* Use *feature*. |
| **Envelope** | *Retired.* Use *module*. |

**Multiple features per module rule.** A module exposes one or more features. More than one feature in a single module is permitted only when those features share their **dependency envelope**. The trigger to split a module is the introduction of a heavy dependency needed by only some of its features. Multiple features in one module typically represent variations of DI registration that some users will want and others will not.

---

## §1 Anti-patterns the framework prevents

The framework was distilled from a structural analysis of an existing modular application that exhibited the failure modes below. The application that produced these observations is documented in derived constitutions as a worked case study; the patterns themselves generalise to any modular application.

The framework is designed to prevent:

1. **God packages.** A single package that accumulates contracts and implementations from many domains, forcing every consumer to take on dependencies they do not need.
2. **Framework leakage into domain code.** Domain types coupled to web frameworks, expression engines, or infrastructure libraries that should be invisible to the domain.
3. **Forced heavy dependencies.** A `.Core` library that pulls in a heavy NuGet (EF Core provider, script engine, message broker SDK), forcing every consumer of the `.Core` to take that dependency whether they use it or not.
4. **Infrastructure locked into the lowest layer.** Infrastructure choices (a specific database provider, a specific lock implementation) baked into the contracts so they cannot be swapped without rewriting the contract.
5. **Inverted dependency direction.** Domain code referencing infrastructure code; consumer code referencing internals of a provider it should know only by contract.
6. **Silent DI resolution.** Multiple implementations registered against the same contract with last-write-wins behaviour, where the consumer cannot detect or diagnose the conflict.
7. **No naming convention.** Domain language buried under layer-marker buckets (`Features.*`, `Modules.*`, `Implementations.*`, `Contracts.*`) that communicate nothing the domain hierarchy doesn't already say.

§2 below prescribes the rules that prevent each of these failure modes.

---

## §2 The Architecture

### §2.1 Three-Layer Separation — applied per feature

The Core / Helper / Features separation is **not a global stack across the solution.** It is a pattern that applies *once per feature*. There is no single global "Core" library. Each feature owns its own three layers; cross-feature consumption happens through the feature's `*.Core` library.

The shape, per feature or domain (`<App>` is the application's root-domain namespace; `<Domain>` is the top-level domain segment; `<SubDomain(s)>` is zero or more further segments representing nested sub-domains; `<Variation>` names a specific implementation choice):

```
Layer 1 — Domain / Feature Core
  <App>.<Domain>[.<SubDomain(s)>].Core
  Contains: interfaces, abstract classes, models, thin utility
            implementations, helper extensions specific to this
            domain or feature.
  Allowed external NuGets: Microsoft.Extensions.* Abstractions and
            Microsoft.Extensions.Primitives only.
  Cross-Core references between domains are explicitly allowed
            and are the primary composition mechanism (see below).

Layer 2 — Helper Libraries (optional, standalone)
  <App>.<Domain>[.<SubDomain(s)>].<Qualifier>
  Contains: lightweight default implementations that may depend on a
            single focused external NuGet.
  Rule: never referenced by Layer 1; feature developers opt in by referencing these helper libraries from layer 3.

Layer 3 — Feature implementation
  <App>.<Domain>[.<SubDomain(s)>][.<Variation>]
  Contains: IFeature implementation, DI registration, concrete services.
  Rule: never references another implementation library directly;
        cross-feature coupling goes through inheritance (§2.5) or provider
        interfaces in a Core library (§2.6).

```

**Cross-`.Core` references — the primary composition mechanism.** Cross-`.Core` references are not just permitted — they are how the framework composes. A modular monolith is built up from features and domains; domains compose by referencing each other's `.Core` libraries to consume contracts.

**Implementation-to-implementation references.** Implementations across **unrelated** sub-domains never reference each other — coupling at unrelated implementation layers is forbidden. Implementations **within the same provider family** *may* reference each other for explicit specialization — for example, a SQL Server provider package extending an EF Core base implementation package. This is the inheritance/specialization branch of §2.7.1's decision rule. Constraints on the carve-out: directional, intentional, and reflected in the package naming and dependency graph; never as informal cross-cutting reuse.

**The only constraints on cross-`.Core` references** are the existing layer-1 rules: a `.Core` may reference another `.Core`, but never a feature implementation, never a helper library, and never a heavy external dependency.

### §2.2 Naming Convention

The framework prescribes a naming pattern; each application chooses its own root-domain namespace and pins it in its derived constitution.

**The decisive rule:** namespaces use *domain language only*. The segments `Features`, `Modules`, or any other layer-marker word do not appear in any namespace. An implementation is identified by its variation name within the domain hierarchy.

| Layer | Pattern |
|---|---|
| Domain / Feature Core | `<App>.<Domain>[.<SubDomain(s)>].Core` |
| Helper libraries | `<App>.<Domain>[.<SubDomain(s)>].<Qualifier>` |
| Feature implementation | `<App>.<Domain>[.<SubDomain(s)>][.<Variation>]` |

The `*.Contracts`, `*.Abstractions`, `Features.*`, and `Modules.*` alternatives have all been considered and rejected. Domain-only naming is the chosen convention.

The `<Variation>` segment is optional. It is added only when a domain
  hosts more than one implementation that needs to be distinguished,
  or when a single implementation already implies a variation choice.
  When a domain ships exactly one implementation and there is no
  foreseeable sibling, the bare `<App>.<Domain>[.<SubDomain(s)>]` form
  is the implementation's name.

**Suggested implementation suffixes.** The same domain-language pattern carries the same suffixes throughout the framework:

```
<App>.<Domain>[.<SubDomain>...].Core
<App>.<Domain>[.<SubDomain>...].<ImplementationOrProvider>
<App>.<Domain>[.<SubDomain>...].Api
<App>.<Domain>[.<SubDomain>...].Testing
```

**Secondary-domain naming sub-rule.** When a feature exists only to contribute implementations of another domain's contributor or provider interfaces — and brings no models of its own to the consumable surface — the **model-owning** domain wins the prefix. The naming form is:

```
<App>.<ModelDomain>.<ConsumerDomain>
```

The test is: *where do the models come from?* The domain whose `.Core` defines the models being contributed **is** the primary domain; the contributing feature is named under it.

The reverse form (`<App>.<ConsumerDomain>.<ModelDomain>`) is an anti-pattern. It forces the consumer domain to grow one sub-branch per upstream consumer — `<ConsumerDomain>.<ModelDomainA>`, `<ConsumerDomain>.<ModelDomainB>`, … — until the namespace is a junk drawer of unrelated model branches glued together by their shared contributor surface. A domain that grows one branch per consumer is not a domain.

The Elsa-specific worked example lives in §E3.8 of the application constitution.

**Type-level `Feature` suffix is permitted.** The word `Feature` describes an activation unit, so a class implementing `IFeature` may be named e.g. `<Domain>RuntimeFeature`. The package, however, is `<App>.<Domain>.Runtime` — never `<App>.Features.<Domain>.Runtime`.

**Avoid global layer-marker buckets** in package or namespace names: `Features.*`, `Modules.*`, `Implementations.*`, `Providers.*`, `Adapters.*`. They communicate nothing the domain hierarchy doesn't already say.

**Discovery / composition / activation domain.** If the framework needs a domain that owns the discovery, description, enabling, validation, and composition of modules and features, that domain is named `<App>.Modularity` — **not** `<App>.Features`.

### §2.3 The Framework Primitives Library

**Default outcome (preferred): small focused packages.** Most types that today live in a generic primitives library belong to a specific, narrower concern that warrants its own focused package — for example: 
- `<App>.Primitives`: contains truly domainless building blocks: `Result<T>`, `Page<T>`, guard helpers. 
- `<App>.Persistence.Primitives`: is small and has a clear scope, and is consumed only by features that need it. E.g. base entity abstractions.

**Conditional outcome: an `<App>.Foundation.Core` package.** A separate framework-foundation domain — owning contracts like `IFeature`, `IShell`, the activation manifest, the modularity discovery surface — is introduced **only if framework-foundation contracts emerge as a coherent set**. The name was coined as a placeholder; if these contracts naturally fit `<App>.Modularity.Core` (see §2.2), use that instead. **`<App>.Foundation.Core` is *not* a default outcome** to populate eagerly.

**Hard rules.**

- If a type has a recognisable domain, it belongs in that domain's `.Core` package — **not** in `<App>.Primitives`.
- `<App>.Primitives` carries zero external NuGet dependencies. Without exception. High admission bar.
- `<App>.Foundation.Core`, *if it exists*, carries only the dependencies any `.Core` is permitted to carry under §2.1.
- The three-repetition rule (§2.17) applies to `<App>.Primitives` and to broadly-shared utilities only. Inside a domain or feature, duplication is a local design tradeoff for that owner — not a constitutional violation.

### §2.4 Helper Libraries — domain-owned

A helper library is an **optional, non-activatable package that provides lightweight reusable implementations or utilities for a specific domain**, usually because they require a dependency that does not belong in `.Core`. Helpers are **domain-owned**, not feature-owned: multiple features within the same domain may share a helper when they share its dependency envelope and lifecycle.

**Hard rules.**

- A helper library is owned by exactly one domain (or sub-domain). Cross-domain helpers do not exist.
- Helpers are never referenced by a `.Core` library.
- Helpers are not activatable: they expose no `IFeature` implementation. A consumer wires a helper into its own feature implementation.
- A helper that turns out to be broadly useful across unrelated domains is a smell — either the content is truly domainless and belongs in `<App>.Primitives` (§2.3), or it reveals a missing domain `.Core`.

### §2.5 Feature Inheritance

Feature inheritance is the only sanctioned form of structural coupling between features (extension, decoration, alteration). No peer references between implementation libraries — features never reference each other directly.

When Feature B must extend, decorate, or specialize Feature A:

- B inherits from A's feature class.
- B overrides `Configure` (or the equivalent registration entry point) to add or replace services.
- B's package depends on A's package; A is unaware of B.

Compile-time inheritance is the load-bearing mechanism; runtime references between feature classes are not part of this pattern.

### §2.6 Cross-feature composition mechanisms

**The framework forbids tight logic coupling between concrete implementations.** Cross-feature dependencies MUST be expressed through one of the sanctioned mechanisms below; coupling that relies on side effects, observable behaviour, or implementation details of another concrete class is a smell. Only **contract-level coupling** is permitted — a contract (typically an event payload, sometimes a replacement-contract interface) is the agreement; the publisher treats every handler/implementation uniformly; the system stays consistent and predictable. A test failure that exposes hidden side-effect coupling between implementations (§2.23.4) is the canonical signal that this rule was violated; the resolution is to lift the dependency to a contract, not to reproduce the side effect.

When inheritance is the wrong tool for cross-feature composition (§2.5), three mechanisms govern how features compose:

1. **Events (§2.6.1) — the in-process pub/sub + contribution mechanism.** Domains publish named events (`IEvent`); features register handlers (`IEventHandler<T>`). A single `IEventPublisher.Publish` dispatches through a framework-managed pipeline; the **delivery strategy** is pluggable, defaulting to **Sequential** (synchronous, awaited end-to-end, CAN break the caller). Contribution — "I'm about to do X, who wants to participate?" — is the Sequential strategy applied to an event whose payload exposes intent-revealing `Add` methods; the publisher reads the accumulated contributions back. Pure notification — "X happened, react if you want" — is the same mechanism, often the Background strategy. For sync access to contributions, the Registry + StartUp Task sub-pattern applies. Events live in their own contracts library (`Elsa.Events.Core`), separate from command/request dispatch (`Elsa.Mediator.Core`), over a shared pipeline engine (`Elsa.Pipelines.Core`).
2. **Replacement contracts (§2.6.2) — single-implementation services.** When exactly one implementation is meaningful per application, the contract is a replacement contract. Multiple registrations are a conflict, not a contribution.
3. **Events are the in-process mechanism; `IMediator` / `IEventBus` indirection is not (§2.6.3).** If a publisher expects a specific handler to run, it publishes a named `IEvent` (§2.6.1) — making the dependency discoverable — rather than hiding it behind anonymous generic dispatch.

§2.6.4 governs the orthogonal question of design-time vs runtime contract split, which applies to whichever mechanism is chosen.

§2.6.5 codifies the **rare exception** where a sync contributor pattern (provider interface, DI-resolved enumerable) is permitted because the contribution flow is structurally incompatible with both §2.6.1 (events) and its Registry + StartUp Task sub-pattern.

#### §2.6.1 Events — the in-process pub/sub + contribution mechanism

The in-process composition mechanism is the **event**. A domain that wants to be extensible enumerates a set of named event types (`IEvent`) in its `.Core` library. Those events are the domain's deliberate, documented extension points — the answer to *"what can other features hook into, contribute to, or react to?"*.

Features extend a domain by **registering a handler (`IEventHandler<T>`) for one of its published events**. They do not register arbitrary provider interfaces; the domain's event vocabulary is the only contribution surface.

**One concept, one publisher, pluggable strategy.** There is a single event marker (`IEvent`), a single handler shape (`IEventHandler<T>` with `Task Handle(T, CancellationToken)`), and a single publisher (`IEventPublisher.Publish`). What varies is the **delivery strategy** (§2.6.6), not the type name. The framework does not ship separate "domain event" / "notification" / "lifecycle event" concepts — that distinction was collapsed; the real axis is *delivery strategy* + *break behaviour*, selected per publish, not baked into the marker.

**The framework provides the dispatch mechanism.** The application supplies the publisher and a pipeline with shared middleware (logging, diagnostics). The pipeline invokes every handler for the published event under common infrastructure. Domain code does not roll its own `foreach` + `try`/`catch` loop.

**Default dispatch semantics — Sequential, synchronous, CAN break the caller.**

- Handlers are async methods (return `Task`).
- The default strategy is **Sequential**: the publisher invokes handlers in DI-resolution order and **awaits the completion of every handler** before returning. *"Synchronous"* here means **awaited end-to-end** — not single-threaded sync in the C# language sense.
- **A handler exception propagates to the publisher's caller.** The default path ships **no exception-shielding middleware**. If a handler throws under the Sequential strategy, the publish fails and the caller sees the exception — exactly as a direct method call would. This is deliberate: when a publisher needs its handlers to have run (contribution, validation, precondition), a swallowed handler failure is a silent correctness bug. Fail-fast is the safe default.
- **Resilience is a strategy choice, not a default.** Fire-and-forget isolation lives **only** in the Background strategy (and its `BackgroundEventPublisher`), which owns the `try`/`catch` + silent logging. A publisher that wants "subscriber must never break me" semantics selects `EventPublishingStrategy.Background` explicitly (§2.6.6). It does not get that behaviour for free on the default path.

**Boundaries and expectations.**

- **Intent.** Internal technical communication between features within the application.
- **Scope.** Cross-feature contribution **and** intra-domain specialization. Events are valid not only across unrelated domains but also within an inheritance/implementation chain — e.g. a domain-specific event like `OnEntitySaving(DbContext, EntityEntry)` is consumed only by features that already specialize an EF-Core-aware implementation. The mechanism is the same; the audience is narrower.
- **Failure coupling follows the strategy.** Cross-domain coupling exists at the **contract level** (the event's shape). Whether a handler failure breaks the publisher is governed by the chosen strategy: **Sequential / Parallel propagate** (the caller is responsible for the handlers having run); **Background isolates** (a flaky subscriber becomes a log entry, not a failed publish). A publisher whose dispatch is purely informational (audit, event-sourcing stream, telemetry, UI-push) SHOULD publish Background so a subscriber failure cannot break a transition that has already been persisted. A publisher that reads contributions back MUST publish Sequential.
- **Completeness under Sequential.** Every registered handler is dispatched in order; the publisher does not skip handlers or return early. Because the default does not shield, the first throwing handler halts the chain — that is the intended fail-fast behaviour, not a completeness violation. Background completeness is FIFO across the channel and isolated per subscriber.
- **Handler independence.** Handlers MUST NOT depend on each other. Each handler reacts to the event for its own purpose; observed handler ordering or side effects of one handler MUST NOT be relied upon by another. A handler that depends on another handler's prior side effect is already in violation of §2.6 (no tight logic coupling between implementations).
- **Diagnostics.** Logging, tracing, and diagnostics attach uniformly via the same middleware surface. A failing handler under Background is observable in operational logs with full identifying context; the application's observability stack treats those entries as first-class signals.

**Sub-pattern — Registry initialization via StartUp Task (for sync access).**

When a registry-style or index-style consumer needs access to contributions from sync code — e.g. a `JsonConverter` callback in a serializer, or any constructor that builds an index — the contribution is still gathered through an event. The async population happens once at startup; sync access happens afterwards. The pattern:

1. The domain defines a **Registry** with a `Register<T>(item)` method (and accessor methods).
2. The domain publishes an `On<RegistryName>Initializing` event whose payload exposes a directly-accessible `ICollection<T>`.
3. The feature implementing the domain registers a **StartUp task** that publishes the event Sequentially and flushes the contributions into the registry:

   ```
   var event = new On<RegistryName>Initializing();
   await eventPublisher.Publish(event);   // default Sequential — contributions read back
   registry.RegisterAll(event.Items);
   ```

4. Other features extend the domain by implementing the registry's contributor interface (`I<X>Source`) and registering it via DI; the single aggregating handler (§2.6.1 sub-pattern below) flushes every source's items into the event's carried `ICollection<T>`.
5. After startup, sync code accesses the populated registry directly — no async dispatch at the access site.

The result: **all cross-feature contribution flows through the same event pipeline**, including cases where the access pattern is sync. There is no separate "sync fallback" mechanism.

**Sub-pattern — Contributor interface + single aggregating handler.**

A fan-in event that gathers contributions from many features MUST use the **contributor-interface + single-handler** shape — NOT one `IEventHandler<On<Phase>>` per contributing feature. **This rule is scoped to the contribution axis.** Features are free to register `IEventHandler<T>` for independent subscriptions — observing an event for auditing, cache invalidation, cross-cutting reactions, or any purpose unrelated to the fan-in aggregation — without any restriction. The constraint is: if you are *contributing to a fan-in*, use the typed interface rather than a scatter of handlers all doing the same kind of thing (imagine 100 JavaScript declaration contributors each implemented as a separate `IEventHandler<OnDeclarationsDocumentGenerating>` — that is the chaos this rule prevents).

- The domain `.Core` defines a **contributor interface** that describes the *intent* of the contribution (e.g. `IDraftValidator`, `IJsonConverterSource`). Features implement it and register it via DI (`services.AddScoped<TContributor, Impl>()`) — they do **not** register a separate event handler for *this contribution purpose*.
- The owning feature registers **exactly one** action-named `IEventHandler<On<Phase>>` (e.g. `ExecuteValidations`, `RegisterJsonConverters`) that injects `IEnumerable<TContributor>`, iterates every contributor, and writes the aggregate into the event.
- The event payload exposes a **directly-accessible `ICollection<T>`** (for flat-collection sinks) or a rich mutable context object (for heterogeneous sinks). No private backing list, no `AddX()` methods, no `IReadOnlyList<T>` read accessor — the single handler is the only writer, so encapsulation is met without ceremony.

**The three contributor-interface kinds.** The suffix MUST match the method shape AND the event topology:

- **`I<X>Source`** — the contributor **returns** its items and touches no shared object (a *pull*). Used when the sink is a flat collection. Signature returns `IEnumerable<T>` (or `ValueTask<IEnumerable<T>>`). The single handler aggregates every source's return into the event's `ICollection<T>`. **"Source" is preferred over "Provider".**
- **`I<X>Contributor`** — the contributor **receives a context and acts on it** (a *push*), returning void / `ValueTask`. Used when the sink is a rich mutable context that accepts heterogeneous, multi-operation contributions (e.g. a declarations context exposing `AddVariable(...)`/`AddType(...)`). The single handler hands the context to each contributor in turn.
- **`I<X>PreProcessor` / `I<X>PostProcessor`** — the contributor **acts on a lifecycle context**, returning void / `ValueTask`. Used when the contribution event is one half of an **OnXxxing / OnXxxed (before / after) pair**: a pre-processor runs at the *before* event to prepare the context (register functions/values, set up state); a post-processor runs at the *after* event to act on the result (copy outputs back, clean up). Each event still has exactly one aggregating handler (e.g. `PreProcessScript` / `PostProcessScript`) injecting `IEnumerable<I<X>PreProcessor>` / `IEnumerable<I<X>PostProcessor>`. **When the events form a before/after pair, prefer this kind over `Contributor`** — `PreProcessor`/`PostProcessor` names the lifecycle position, which reads far more naturally than a generic "Contributor" for a paired hook.

**Action-named suffixes (sanctioned alongside the four above).** When the suffix names the **specific action** the interface performs on the received context, an action-named suffix is preferred over the generic `Contributor` — e.g. **`I<X>Validator`** (inspects the context and *returns* findings, like `IDraftValidator.Validate`) and **`I<X>Handler`** (receives the context + a typed subject and *acts* at a named lifecycle point, like `IEntitySavingHandler.Handle` / `IEntityLoadingHandler.Handle` on the EF Core save/load seam). These are Contributor-kind (context-receiving — a *Validator* returns its findings; a *Handler* acts in place); they simply carry an intent-revealing, action-specific name instead of the bare `Contributor`. The topology rule is unchanged: the contributor interface is what features implement + register via DI, and the single aggregating `IEventHandler<On<Phase>>` (e.g. `ExecuteValidations`, `ApplyEntitySavingHandlers`) still owns the event subscription and dispatches every implementation. Use an action-named suffix when one exists naturally; fall back to `Source`/`Contributor` when no single verb captures the contribution.

Never name a return-style interface `Contributor`, nor a context-acting interface `Source`. A *Source* yields data; a *Contributor* (incl. its action-named forms `Validator`/`Handler`) performs operations against a target; a *PreProcessor*/*PostProcessor* performs operations against a target at a named point in a before/after lifecycle.

**Intra-domain vs. cross-domain contributions.** When a contributor-interface implementation ships in the **same domain** as the `.Core` contract it satisfies, it is an *intra-domain default* — the feature delivers on its own Core's promises. When it ships from an **unrelated domain**, it is a *cross-domain contribution* — the primary mechanism by which domains extend each other's pipelines without direct coupling. The owning feature's `EXTENSION_POINTS.md` catalog (§2.22.1) MUST list all known implementations of each contributor interface, tagged accordingly (`*(intra-domain — default)*` / `*(cross-domain)*`). The contributing feature MUST note in its own `README.md` (under a **Cross-domain contributions** section) which contracts from other domains it satisfies and link back to the owning domain's catalog. This makes the inter-domain dependency map visible from both ends.

**Why.**

- **Centralised contribution logic.** One handler runs inside the event pipeline and owns iteration + aggregation + any ordering/error policy — instead of N scattered handlers each with ad-hoc try/catch. Contribution still runs *inside the event pipeline* (the reason events were chosen for contribution at all); only the per-feature handler sprawl is gone.
- **The interface describes the intent.** `IDraftValidator.Validate(...)` says exactly what a feature contributes; a bare `IEventHandler<OnDraftValidating>` says nothing. Features implement the intent, not the dispatch plumbing.
- **Smell heuristic.** **Too wide a variety of operations on a single context sink indicates two distinct events that should be split.** If one event's context conflates unrelated contribution kinds, split into separate events bound to separate semantic phases.

**Mechanical rule.**

```
// domain .Core
public sealed class On<Phase>(TContext context) : IEvent
{
    public TContext Context { get; } = context;
    public ICollection<TItem> Items { get; } = [];   // directly accessible; written only by the single handler
}

public interface I<X>Source              // returns — flat-collection sink
{
    IEnumerable<TItem> Get<Items>();
}
// or, for a rich-context sink:
public interface I<X>Contributor          // receives + acts — context sink
{
    ValueTask Contribute(TContext context, CancellationToken ct);
}

// owning feature — the ONE handler
public sealed class <Action>(IEnumerable<I<X>Source> sources) : IEventHandler<On<Phase>>
{
    public Task Handle(On<Phase> e, CancellationToken ct)
    {
        foreach (var source in sources)
            foreach (var item in source.Get<Items>())
                e.Items.Add(item);
        return Task.CompletedTask;
    }
}

// contributing features — register the interface impl, NOT an event handler
services.AddScoped<I<X>Source, ThisFeaturesSource>();
```

**Superseded shape.** The earlier "intent-revealing `AddX()` methods + private list + `IReadOnlyList<T>` read accessor" sub-rule is **withdrawn** — it added ceremony to every contribution event and folded contribution logic into the payload. Contribution events now expose a directly-accessible `ICollection<T>` written solely by the single aggregating handler. Records remain discouraged for events carrying a mutable contribution sink — use `sealed class` with a get-only collection auto-property initialised to `[]`.

The Elsa-specific worked examples land in §E3.3 (`OnJsonPayloadConvertersInitializing` + `IJsonConverterSource` — the Source kind), §E3.7 (the JS declarations cluster as the Contributor kind, and the JS script-evaluation cluster as the PreProcessor/PostProcessor kind over the `OnEvaluatingScript`/`OnScriptEvaluated` pair), and §E3.10 (`OnDraftValidating` + `IDraftValidator`).

**Domain-design consequence.** Enumerating a domain's events is part of *defining the domain*. The §2.18 domain-identification methodology gains a corollary: once a domain's purpose and contracts are established, the architect MUST also identify *where other features can bring something or do something* — and surface those points as named events in the domain's `.Core`. A domain whose extension points are implicit (registered providers nobody can enumerate, events nobody can find handlers for) fails this rule.

**Feature documentation requirement.** Every feature's documentation MUST contain a discoverable inventory of:

- Which **event handlers** it registers.
- Which **tasks** it registers (e.g. startup, recurring, scheduled).

The full shape of the feature-documentation contract is governed by §2.22 (Feature documentation).

The Elsa-specific worked example of the Registry + StartUp Task sub-pattern lives in §E3.3 of the application constitution.

#### §2.6.2 Replacement contracts — single-implementation services

Some contracts in a `.Core` library are **replacement contracts**: one implementation is selected per application/runtime context. They are *not* contribution contracts (§2.6.1) — they govern services where exactly one implementation is meaningful at a time.

> *Example (synthetic).* An `IServiceBus` contract — the host uses one specific service bus implementation, not multiple. A second registration is a conflict the framework must detect.

> *Example (synthetic).* A distributed-lock contract — the application chooses one lock implementation (file-system, Redis, …). Multiple registrations would be ambiguous; selection is part of the application's configuration.

**Constitutional requirements** for replacement contracts:

- The contract's kind (replacement) MUST be declared on the contract itself — by marker interface, attribute, naming convention, or contract metadata. The mechanism is application-defined; the obligation is not.
- Replacement-contract conflicts (two implementations registered against the same replacement contract) MUST be either prevented at registration time or detected with a clear diagnostic at startup. Silent last-write-wins is forbidden.
- Contribution-style consumers (`IEnumerable<T>` resolution, collection of behaviours) MUST NOT use a replacement-contract interface; they go through events per §2.6.1.

#### §2.6.3 Named events, not anonymous generic dispatch

`IEventPublisher` **is** the sanctioned in-process pub/sub mechanism (§2.6.1). What this section forbids is **anonymous indirection** — hiding a real dependency behind an `IMediator` / `IEventBus` / generic message-bus abstraction where the sender depends on a particular handler running but the dependency is invisible.

The distinction is the **named, discoverable event type**, not the dispatch surface. Publishing a named `IEvent` that lives in the domain's `.Core` is fine — encouraged. Reaching for an opaque `mediator.Send(someObject)` where neither the event type nor the expected handler is part of any domain's published vocabulary is the smell.

The moment a publisher **expects a specific handler to run** — because that handler updates state the publisher depends on, validates a precondition, mutates a graph, or otherwise completes the publisher's logical operation — the dependency is real and MUST be made visible:

- Publish a **named `IEvent` (§2.6.1)** whose type lives in the domain's `.Core`, so the dependency surface is discoverable.
- Publish it **Sequential** (the default) so the dispatch is awaited end-to-end and the publisher knows handlers have completed — and, for contribution events, can read the result back.
- A refactor that removes a handler shows up at compile-time (missing handler registration) or at startup diagnostics, not as silent runtime drift.

**Read-back needs Sequential + a contribution payload.** If a publisher needs to consume what handlers produced, it MUST publish Sequential with a contribution payload exposing a directly-accessible `ICollection<T>` (or rich context) written by the single aggregating handler (§2.6.1) and read the accumulated result after the chain — never infer results from an out-of-band side channel. Background publishing returns before subscribers run; reading anything back from it is a bug.

**The smell, named.** "Coupling smuggled through anonymous dispatch." The diagnostic question: *does the publisher care that any particular handler ran, and is the event a named type in a domain's `.Core`?* If it cares but the event is anonymous, surface it as a named `IEvent`.

#### §2.6.4 Design-time vs runtime contract split

When a contract surface has both a **design-time consumer** (intellisense, schema validation, declaration generation, picker enumeration) and a **runtime consumer** (binding, execution, evaluation, dispatch), the contract MUST split into **two contracts**, each bound to its consumer.

**Why.** A unified contract forces every contributor to satisfy both consumers even when it has business with only one — design-time tooling pays runtime cost, or runtime code drags design-time concerns into its dependency surface. Worse, a unified contract opens a sneaky channel for design-time code to reach runtime payloads (and vice versa) — bypassing any lifecycle/phase boundary the application maintains at higher levels.

**Shape.**

- The two contracts MAY share a `.Core` data record describing the *shape* of what is being contributed (function signature, schema, port definition, etc.).
- The contracts themselves are distinct and live in their respective sub-domain `.Core`s (or in the same `.Core` clearly labelled).
- Each contract is dispatched independently per §2.6.1 (events) — the design-time event is published when the design-time consumer needs the contributions; the runtime event is published when the runtime consumer needs them.
- A single feature MAY register handlers for both events; it MAY register for only one. Neither is presumed.

**Generalisation.** Many applications maintain a boundary between authoring/design-time concerns and execution/runtime concerns at the sub-domain level. This rule applies the same boundary at the contract level: a contract bound to a design-time consumer MUST NOT carry concerns of a runtime consumer, and vice versa. The application's derived constitution names that boundary concretely; the framework rule applies it at the contract level wherever such a boundary exists.

The application-specific worked example lives in §E3.7 of the application constitution.

#### §2.6.5 Sync contributor pattern — rare exception

§2.6.1's event mechanism (and its Registry + StartUp Task sub-pattern for sync access) is the **default** for cross-feature contribution. A small class of contribution flows fits neither mechanism cleanly. For these — and **only** these — a **sync contributor interface** (a provider interface resolved via DI as `IEnumerable<TContributor>` at the call site) is permitted.

**The criteria — ALL must hold** for the exception to apply:

1. **The contribution is intrinsically sync at its dispatch site.** The host pipeline that invokes contributors does not run async code at that boundary (e.g. `DbContext.OnModelCreating`, a `JsonConverter.Read`/`Write` callback that doesn't have an async path, a synchronous lifecycle hook from an external framework). Forcing async dispatch would require sync-over-async (`.GetAwaiter().GetResult()`) — a smell with no architectural upside.
2. **What is contributed is behaviour, not data.** Contribution events excel when the contribution is "items added to a carried collection". The sync contributor case is when each contributor runs *its own logic at the lifecycle moment*, mutating a shared external target. There is no payload list to populate at startup.
3. **The Registry + StartUp Task sub-pattern does not apply.** Either (a) the contribution data is not knowable at startup (it depends on the lifecycle moment's runtime context), or (b) populating the registry at startup would still require a callback to be invoked at the lifecycle moment — adding indirection without structural benefit.

**Mechanism for the exception.**

- The contributor declares a **sync provider interface** in the domain's `.Core` library (e.g. `interface IEntityModelCreatingHandler { void Handle(ModelBuilder builder, IMutableEntityType entityType); }`).
- Features implement the interface; instances are registered via DI as the interface type.
- The dispatcher resolves `IEnumerable<TContributor>` at the call site and invokes each sync. The dispatcher MUST invoke **all** contributors (no early exit) — same completeness guarantee as §2.6.1.
- Exceptions from a contributor surface up; the dispatcher does not swallow them.
- The contributor interface MUST be declared with a stable contract; renames count as MAJOR per §4.2 (same as a `.Core` rename).

**Hard rule on use.** The §2.6.5 exception is **rare**. Every use case MUST be analysed at design time:

- Can the §2.6.1 event mechanism apply? Try first.
- Can the Registry + StartUp Task sub-pattern apply? Try second.
- Is the contribution genuinely sync, behaviour-shaped, and runtime-context-dependent? Only then does §2.6.5 apply.

**A use case that uses §2.6.5 without satisfying all three criteria is a §2.6.1 violation disguised.** Reviewers MUST challenge §2.6.5 invocations.

**Worked example (constitutional record).** The first identified case is EF Core's `OnModelCreating` lifecycle hook in `Elsa.Persistence.EFCore`:

- `IEntityModelCreatingHandler` is registered via DI; `ElsaDbContextBase.ApplyEntityModelCreatingHandlers` resolves `IEnumerable<IEntityModelCreatingHandler>` and invokes each sync during `OnModelCreating`.
- Criterion 1: `OnModelCreating` is intrinsically sync — EF Core's own contract. ✓
- Criterion 2: contributors customise the shared `ModelBuilder`; no payload list. ✓
- Criterion 3: the contribution's data (per-entity-type model configuration) is per-lifecycle-moment, not pre-populatable at startup. The model builder doesn't exist yet at app startup. ✓

This case is the canonical §2.6.5 worked example. Future §2.6.5 invocations should compare their structural shape to this case.

The application-specific worked example also lives in §E3.9 of the application constitution.

#### §2.6.6 Delivery strategies — one event concept, three dispatch behaviours

*Rewritten (Unit 1, 2026-06-02): the prior two-concept model (`IDomainEvent` + `INotification`/`ILifecycleEvent`) was collapsed into a single `IEvent`. The distinction it tried to capture — "participate" vs "react" — is now expressed as a **delivery strategy** chosen per publish, not as a separate marker type.*

There is **one** event concept (`IEvent`, §2.6.1). The behavioural axis that used to motivate separate markers is the **delivery strategy** passed to `IEventPublisher.Publish`:

| Strategy | Dispatch | Failure behaviour | Publisher reads back? | Use for |
|---|---|---|---|---|
| **Sequential** *(default)* | Handlers run in DI-resolution order; publisher awaits the whole chain end-to-end. | **Propagates.** First handler throw fails the publish and surfaces to the caller. No shielding. | **Yes** — for contribution events whose payload exposes intent-revealing `Add` methods, the publisher reads the accumulated result after the chain. | A participation gate / contribution ("I'm about to do X — who wants to participate?"); any case where the publisher's own correctness depends on handlers having run. |
| **Parallel** | Handlers dispatched concurrently; publisher awaits all. | **Propagates** (aggregated). | No — ordering is unspecified, so reading back is meaningless. | Independent reactions where latency matters and no handler can break the publisher's contract. |
| **Background** | Queued to an in-process channel; publisher returns immediately; a hosted worker (`BackgroundEventPublisher`) drains it. | **Isolated.** The Background strategy + worker own the `try`/`catch` + silent logging. A flaky subscriber becomes a log entry, never a failed publish. | **No** — publisher has already returned. | "X happened — react if you want": audit, event-sourcing stream, telemetry, UI-push. Especially state-transition signals fired *after* the transition is persisted. |

**Resilience lives in the Background strategy, not in a default middleware.** Unit 1 removed the exception-shielding-by-default position. The default Sequential path ships **no shielding** — fail-fast is the safe default for any publisher whose handlers must have run. A publisher that wants "subscriber must never break me" semantics selects `EventPublishingStrategy.Background` explicitly. There is no separate `ILifecycleEventSender` and no typed lifecycle marker; a "lifecycle event" is simply an `IEvent` published Background after the transition is persisted.

**Choosing a strategy — the diagnostic question.** *Does my own correctness depend on these handlers having run?*

- **Yes** → Sequential (the default). If you also need their output, give the event a contribution payload and read it back.
- **No, and a subscriber failure must not break me** → Background.
- **No, but I must wait for them and they're independent** → Parallel (rare).

**Hybrid pattern — `OnXxxing` (Sequential gate) + `OnXxxed` (Background outcome).** When a domain has both a participation gate and an outcome signal for the same transition, the present-participle form is published **Sequential** (validators / contributors run, publisher reads back) and the past-tense form is published **Background** (notifies that the transition happened, outcome carried in the payload, fired after persistence). Worked example: `OnDraftValidating` (Sequential — validators contribute errors) followed by `OnDraftValidated` (Background — fires after the errors are persisted; audit / UI-push react). Both are `IEvent`; only the strategy differs.

**Cross-references.** §2.6.1 (the single event concept + contribution sub-pattern); §2.6.3 (named events, not anonymous dispatch — read-back requires Sequential); §2.22.1 (events catalog); §4.2 (adding an event is MINOR, renaming or removing is MAJOR).

### §2.7 Adapter / Bridge — as a design default

The adapter pattern is a **design default** whenever a feature contains functionality that may be reusable outside its current consumer. When building a feature, the framework expects the author to ask: *"is this functionality potentially useful to consumers other than the one I'm building it for, and if so, can I expose it via a clean adapter or bridge so those other consumers do not have to take on my dependencies?"*

> *Pattern (synthetic).* `<App>.Locking.Core` defines `IDistributedLockProvider` with zero external dependencies. `<App>.Locking.<Provider>` registers an adapter that wraps the third-party lock library. The third-party package is not visible to any consumer of `<App>.Locking.Core`. Replacing one lock provider with another means shipping a new module — no changes anywhere else.

#### §2.7.1 Decision rule — inheritance, adapter, or provider/contributor

§2.5 (inheritance), §2.6 (provider interface), and §2.7 (adapter / bridge) describe three distinct coupling patterns. When a feature must compose with another, apply these questions in order. The patterns may combine in real designs — the questions are a guide, not a strict hierarchy.

1. **Specialization?** Use **inheritance** when the new implementation is *explicitly a more specific version or provider-specific layer of another* and needs to reuse or extend its registration/configuration pipeline.

2. **Isolating a heavy or external dependency?** Use an **adapter** when wrapping an external dependency or infrastructure library behind a stable domain contract, so the heavy dependency remains in one implementation package and is invisible to consumers of the `.Core`.

3. **Independent additive contribution?** Use a **provider / handler / contributor contract** when independent features need to contribute behaviour, metadata, options, converters, handlers, or other additive pieces to a service without referencing each other.

**Always declare the contract's kind** (see §2.6.2): replacement contracts select one and live in §2.6.2; contribution flows go through events per §2.6.1, not through provider/contributor interfaces. The decision rule above is about *coupling pattern*; the contract-semantics distinction is about *who-resolves-what*. Both must be made explicit at design time.

### §2.8 Extension Methods — Decision Framework

Extension methods are a useful ergonomic tool, but they can also exile logic that should live on an interface or in a dedicated service. The framework prescribes a review trigger and a four-question framework:

**Trigger.** When an extension method body exceeds **three lines**, review it before merging. A method can legitimately grow to 5–8 lines due to try/catch, null checks, or local variable declarations; this is not an automatic disqualifier.

**Review questions.**

1. **Does it contain branching or business logic?** → Probably belongs on an interface method or a dedicated service. Promote it.
2. **Should it really be on the interface?** → Consider moving it (or making it a default interface member, subject to §4.2's SemVer rules).
3. **Is the length coming from try/catch, null checks, or local variables with no real logic?** → Fine to stay as an extension. Place it in the contract library closest to the type it extends — never in an implementation or feature library.
4. **Is it used by fewer than three consumers?** → Inline it at the call site; don't publish it as a shared extension.

**Rule of thumb.** Three duplicated extension calls beat one shared helper that introduces a transitive dependency. The team's revised tolerance for duplication (§2.17) applies here too.

### §2.9 Persistence Base Context — application-level

The framework does **not** mandate a base `DbContext` type or any `IEntitySavingHandler` / `IEntityLoadingHandler` / `I<App>DbContext` constraints. Those are **EF-Core-specific application-level concerns**, not Core persistence contracts, and they live at the application level — not in the framework constitution.

EF Core base classes, interceptors, entity mappings, and save/load hooks belong to the EF Core implementation domain, not the framework foundation. An application may provide optional EF Core infrastructure for application-managed contexts, but **consumer-owned `DbContext` types must remain first-class** and must be able to install application mappings/contracts without inheriting from an application base context. If certain advanced behaviour requires an application base context, that must be documented as an opt-in capability in the application's derived constitution, never as a universal requirement.

**The framework's only rule on persistence types** is that any generic constraint at the contract layer is `where TDbContext : DbContext` (Microsoft's base) — never an application-specific base or interface.

How a particular application chooses to structure its persistence layer — including whether it offers a base context with save/load handler hooks — is an application-level design decision. It is documented in that application's derived constitution.

**Persistence invariants are defined independently of the persistence provider.** Where an application's domain model imposes invariants on persisted data (immutability of certain entity properties, audit timestamps, tenant scoping, append-only semantics, etc.), those invariants belong in the model description, not in any specific provider's enforcement mechanism. An EF-Core-backed application may enforce them through `SaveChangesAsync` interceptors and property-save-behaviour configuration; a document-database-backed application may enforce them through write-policies and schema validators; the **same invariants** apply across providers.

Correspondingly, `*.Persistence.Core` (the provider-agnostic persistence sub-domain `.Core`) carries store contracts, persistence-facing models, and the invariants those models must hold — never provider-specific mechanism. Provider-specific implementations live in `*.Persistence.<Provider>` (e.g. `*.Persistence.EFCore`, `*.Persistence.MongoDB`) and are responsible for honouring the invariants through whatever mechanism fits the provider.

#### §2.9.1 Domain-level "shadow" properties — real properties on entities, hidden at the interface boundary

When an entity needs a persistence-only field (e.g. the serialised form of a rich object that the entity also exposes in deserialised form, a denormalised lookup column, a backing string for a `[NotMapped]` projection), declare it as a **real property on the entity class** and **omit it from the read interface**. Do NOT use the provider's "shadow property" feature (e.g. EF Core's `Property<T>("...")` on the builder) for this purpose.

**The distinction.** Provider-side terms like EF Core's "shadow property" mean *"a property the provider tracks but is not on the CLR class"*. Our usage is different: the property IS on the CLR class — it is just **not on the read interface** that other domains depend on. From a *domain* point of view it is a shadow (invisible to other domains); from a *provider* point of view it is a perfectly ordinary mapped property.

**Why.**

1. The central invariant scanner (e.g. an `[Immutable]` attribute scanner that walks the model and applies `PropertySaveBehavior.Throw`) only sees real CLR properties. Provider shadow properties bypass it.
2. Cross-cutting attributes (immutability, audit, tenant scoping) must compose at one place — the entity class. Provider shadow properties scatter that surface area into the provider configuration.
3. Test code and tooling read the entity directly; provider shadow accessors are awkward (string-keyed `Entry().Property("Name").CurrentValue`).
4. Other providers (a document store, a different ORM) don't have a "shadow property" concept; the entity-as-CLR-class model is provider-portable.

**Mechanism.**

- The entity declares the property normally: `public string? SomePayload { get; set; }`.
- The cross-cutting attribute scanner picks it up (`[Immutable]` etc.).
- The read interface (`I<Entity>` in `*.Design.Core`) does NOT include the property — it stays a persistence-internal field.
- The provider configuration is minimal (max-length, column type, etc.). No `Property<T>("...")` shadow registration.

**Anti-pattern.** Declaring a provider shadow property purely to keep the field off the CLR class is a smell. If the field is part of the entity's persisted state, it belongs on the entity. The interface controls visibility; the provider's shadow mechanism is for cases where the field genuinely does not belong on the CLR class (provider-internal bookkeeping, e.g. a generated discriminator the application code never touches).

### §2.10 CQS at the Persistence Boundary

Persistence contracts are split into commands and queries at the contract boundary:

- **Commands** mutate state. They return either `void` / `Task` or a confirmation token (e.g. a new identifier). They do not return queryable views of the data they mutated.
- **Queries** return data. They do not mutate.

Combining the two in a single contract method is a smell. The framework's contracts (e.g. `IAddCommand<T>`, `IQuery<T>`) reflect this separation. Implementations may share an underlying `DbContext` or transactional scope, but the contract surface remains split.

### §2.11 DependsOn — Fail-Fast and Auto-Resolve modes

The framework ships with two modes 'Fail-Fast' and 'Auto-Resolve'. The `DependsOn` mechanism declares static feature-to-feature dependencies. Features that require other services rely on DI resolution at construction time. Depending the mode: when a required service is missing, the host either fails to start or autmatically resolves the missing services through the DependsOn mechanism. This mode is configurable. In either way, DI diagnostic tools must be in place to observe missing and duplicate registrations.

Why supporting both modes is important:
- Static dependency declarations diverge from runtime reality over time. The DI container is the runtime source of truth.
- Validation tooling around the DI graph (see §2.6.2 and §3) can give the same diagnostic value without a separate declaration surface.
- A missing dependency at startup is recoverable: add the feature, restart. A stale `DependsOn` declaration is a maintenance burden.

### §2.12 Configuration & Settings Classification — [DEFERRED]

A unified classification of configuration and settings types (per-host, per-feature, per-tenant, secrets, connection-strings) is **deferred to a dedicated follow-up meeting** (Configuration & Infrastructure). The application's derived constitution may carry a placeholder pending resolution.

Pending items include:

- Secrets resolution from Key Vault / managed identity / per-tenant.
- Per-feature vs application-wide implementations of the same contract.
- Helm chart / deployment configuration conventions.

This section will be revised when the follow-up meeting closes.

### §2.13 Packaging and Versioning — application-level

There is no fixed framework-level definition of "core" beyond the per-feature or per-domain `.Core` library suffix (see Glossary). What an application publishes — *which* packages it ships, *how* they are bundled, and *how* their versions relate — is a set of decisions made by that application's architects and release engineers, not constitutional rules.

The framework does prescribe one rule: **packaging cohesion follows dependency cohesion.** `.Core` libraries that reference each other must be updatable together, and their implementations must follow. Beyond that, the application chooses: a set of independently-versioned packages, a bundled distribution shape, or a hybrid. The application also chooses how versions are shared inside a bundle (whole-version sharing, major+minor sharing, major-only sharing, or fully independent).

Those decisions are **revisable**: a `.Core` and its implementations can graduate out of one bundle into a separately-published feature whenever they prove broadly useful outside that application — and they can also fold back in if the boundary turns out to be wrong.

**The rule** is the rule that produces the packaging — *not* the packaging itself: heavy dependencies stay out of any `.Core`; default implementations may ship alongside their `.Core` so the application is usable out of the box; and packaging can be revisited as evidence accrues.

### §2.14 Integration vs. Consumption-Shape — separate modules

When a module integrates an application with an external system, the *integration itself* and any *consumption-shape modules* that adapt the integration to a specific consumer (a workflow activity, a UI binding, a messaging endpoint, etc.) ship as separate modules. The integration depends on the external system; each consumption-shape depends on the integration plus the consumer's abstractions. A consumer who wants the integration without one particular consumption-shape references only the integration.

> *Example (synthetic).* For a message-broker integration:
> - `<App>.Messaging.<Broker>` — the integration; depends on the broker's SDK and on `<App>.Messaging.Core`.
> - `<App>.Messaging.<Broker>.<Consumer>` — the consumption-shape: broker exposed as the consumer's primitives; depends on the integration plus the consumer's `.Core`.
>
> A consumer who wants broker messaging without exposing it as the consumer's specific primitive references only `<App>.Messaging.<Broker>`.

**Activities (or any consumption-shape unit) that integrate with two external systems.** Such a unit is first treated as a **boundary smell** and re-examined: it usually represents a third integration or orchestration concept, not something that naturally belongs to either external-system module.

If the unit genuinely requires both systems, it lives in **its own domain or integration module** whose dependency envelope explicitly includes both integrations. Hiding the second dependency inside one of the existing modules is forbidden. The package name must make the combined dependency obvious.

> *Example (synthetic).* A `SyncContactToCrmActivity` does not live in either `<App>.Integrations.<SystemA>` or `<App>.Integrations.<SystemB>`. It ships as a dedicated synchronisation/orchestration module that depends on both, e.g. `<App>.Integrations.<SystemA>To<SystemB>Sync` (or under a fresh orchestration domain).

### §2.15 Repository organisation — foundation repo + multi-repo preference

**The foundation repo.** An application's baseline repo is its **foundation repo** — the umbrella that holds host setup, primitives, the main-domain `.Core` libraries, and at least one default implementation per domain that requires durable state. The contents are chosen so that cloning the foundation repo is enough to start the application without dependencies on other repositories.

**What goes in the foundation repo.** Modules typically used by the majority of the application's users *and* whose absence would make local development impractical. The specific composition is an application-level decision documented in the derived constitution.

**Strong preference, not yet ratified:** beyond the foundation repo, the framework is organised as **separate, well-scoped repositories**, not a mono-repo. Each repository is small enough that its scope is obvious from its name and its solution explorer view. Cross-repo development is supported by tooling that can clone a dependency repo, switch package references to project references for debugging, open PRs against a dependency repo, and maintain a developer-local workspace file with the relevant submodules.

**Interim approach.** Start with the single foundation repo and grow into a multi-repo cluster as features cluster naturally and reveal bundles. The how-to-implement question depends on the packaging and versioning strategy (which `.Core` libraries are bundled together, how versions are shared, where standalone features live); until those decisions are made by the application, the multi-repo strategy beyond the foundation repo cannot be fully specified.

### §2.16 Refactor-cost test

Before grouping features into a module, or moving a type between layers, ask: *"if we ever need to undo this grouping, what are the consequences for consumers?"*

**The rule:** preserve NuGet identity wherever possible. If a future move would change a NuGet identity (rename, namespace change), the move must be justified at the cost of every consumer's breaking change.

A corollary: when in doubt about a grouping, prefer the finer-grained split. Merging two packages later is easier than separating one that has consumers.

### §2.17 Duplication beats dependency

The DRY principle is constrained by AI-era economics: *changing* code is cheap; *understanding* code across a wide blast radius is expensive. The constitution's preference order:

1. **A few duplicated lines, each call site, no shared helper** — preferred when the duplication is small and the helper would create a transitive dependency.
2. **A shared helper in `<App>.Primitives` or a domain `.Core`** — only when ≥3 consumers, no new external dependency introduced.
3. **A shared helper in a separate module/library** — only when the helper itself is a feature.

**Scope of the three-repetition rule.** It applies to `<App>.Primitives` and to broadly-shared utilities. Inside a single domain or feature, duplication is a local design tradeoff for that owner — **not** a constitutional violation.

**Thin implementation — definition (referenced by `.Core` admission rules in §2.1, §2.3, §2.4).** A *thin* implementation is small and dependency-light, and its behaviour is **mechanical rather than domain-decisive**: delegation, wrapping, simple default behaviour, argument/null guards, option binding, or trivial value transformation. A thin implementation **must not** contain business policy, persistence strategy, infrastructure-specific logic, or branching that encodes meaningful domain decisions. Anything beyond mechanical glue is a feature implementation, not a thin helper.

### §2.18 Applying the Framework — Methodology for Refactoring a Modular Monolith

The framework is built to be applied to existing modular monoliths, not only to greenfield projects. The recommended approach is a three-step methodology — *sequential by description, inductive in practice.* §2.18.0 frames the shape; §2.18.1–§2.18.3 are the steps; §2.18.4 closes the loop on refactor-cost discipline.

#### §2.18.0 Shape of the methodology

The steps are a **checklist of perspectives to apply, not a sequence to march through.** Step 1 and Step 2 inform each other across repeated passes — domain hypotheses get tested against the actual code; the actual code suggests revised domain hypotheses. Step 3 closes the loop.

**Domains are semantic, not testable.** A domain is a logical, semantic group that describes a process or area of the application. There is no mechanical test for whether something *is* a domain. A domain may be an entire app, a group of features, a specialised single feature, or a group of domains working together towards a goal — all of these are legitimate. The judgement is human and based on intent, not falsifiable by a rule. The methodology is a way to *discover* and *organise* domains in practice; it is not an algorithm for *deciding* whether something qualifies.

> *How it actually happens.* Domain boundaries are typically discovered by clustering existing implementations and asking *"what purpose are these features collectively serving?"* The shared interfaces extracted from implementations are what populate the domain's `.Core`. The methodology below is the explicit framing of what already happens implicitly when a thoughtful architect reads a codebase.

#### §2.18.1 Step 1 — Identify the domains

Map the application into coherent processes, products, or services. Each domain is a *mental model first* — described by its purpose in human terms — and only then realised in code.

**Test.** A domain's purpose MUST be expressible in one verb-led sentence. If the description requires "and" between two unrelated capabilities, the candidate is two domains.

#### §2.18.2 Step 2 — Extract the implementations

Within each domain, separate contracts from implementations:

- **Contracts that other domains or features may want to consume** → move to the domain's `.Core` library. They are the consumable surface.
- **Contracts truly internal to one feature implementation** (no other domain or feature consumes them) → keep them inside the implementation library. They are not part of the consumable surface.

**Test.** Would another domain or feature plausibly need to reference this contract? Yes → `.Core`. No → impl-private. Wrong calls get reversed during Step 3 when reuse evidence appears.

#### §2.18.3 Step 3 — Resolve cross-domain reuse

When two or more domains contain similar features (e.g., both perform serialization, both schedule background work), promote the feature: place its `.Core` library once and reference it from implementations in each consuming domain. Each domain configures the feature differently to its own needs — the `.Core` is the shared contract; the configuration is per-domain.

**Inductive feedback.** Step 3 frequently produces evidence that revises Step 1 (a feature group originally identified as one domain turns out to be two; a candidate domain turns out to be a feature shared by two larger domains). Treat such revisions as a healthy signal of the inductive loop closing, not as a failure of the prior pass.

#### §2.18.4 Refactor-cost discipline

The constitution's refactor-cost test (§2.16) keeps Step-3 revisions affordable: where NuGet identity is preserved, consumers are insulated from the restructuring.

**All refactor work performed under this methodology is governed by the golden rule of refactoring (§2.21.1).** Existing tests on the implementations being refactored MUST continue to succeed across the reorganization, and test deletions require explicit recorded approval from at least one architect.

### §2.19 Feature identity — the feature `name`

A feature's `name` (typically a property on the feature's activation type, e.g. `ShellFeature.name`) is the **stable logical identity** of a feature. It is **not** a dependency mechanism (see §2.11) and it is **not** display text. It is the binding key by which the host, configuration system, diagnostics, telemetry, and tooling refer to a specific feature.

**Uses of the name (constitutional).**

- **Configuration binding key.** A feature's typed options are bound under its name in `appsettings.json` and equivalent sources.
- **Diagnostics, telemetry, and logs.** The name appears in trace context, log scopes, and metrics labels.
- **Features registry / registry display.** The host enumerates installed features by name.
- **Activation manifests.** Manifests reference features by name.
- **Dependency-graph references.** Any future feature-dependency observability surface references features by name; dependency metadata may name another feature, but the name itself is **not** the dependency.

**Rules.**

- Names are **stable across refactors**. Renaming a feature is a compatibility-affecting change because it breaks configuration, manifests, telemetry continuity, and any dependency declarations that reference the old name.
- Names are **unique** within the host's package graph.
- Names are **human-readable** enough for diagnostics output, but they are not display text — UI labels are a separate concern.
- A name change is treated as a **major-version-class change** for the package that owns the feature, even when the change is otherwise internal (see §4.2).

**Recommended rename pattern.** When a feature's name truly must change, the supported pattern is:

1. **Create a new feature** with the desired name.
2. **Retire the existing feature** — deprecation, eventual removal in a major release.

This avoids breaking configuration, manifests, telemetry continuity, and any dependency declarations that referenced the old name. **In-place rename is not supported.**

### §2.20 Provider module decomposition

When a domain has one or more **provider-specific** implementations (e.g. distributed locking with FileSystem, Redis, Postgres providers), the default decomposition is:

- `<App>.<Domain>.Core` — contracts only (interfaces, value objects, no external deps).
- `<App>.<Domain>.<Provider>` — feature class + provider-specific code + adapters wrapping the external library.

**Rule 1 — No premature provider-agnostic umbrella.** The bare umbrella `<App>.<Domain>` (without a provider suffix) is only justified when there is real provider-agnostic shared code. An empty umbrella holding a stub class, or one that ends up containing a single provider's code, is a smell. Specifically:

- When a domain has only one provider, put everything in `<App>.<Domain>.<Provider>`. No umbrella.
- When a second provider arrives and there is actual shared adapter logic, extract it then into `<App>.<Domain>.<ProviderFamily>` per the impl-to-impl carve-out in §2.1 / §2.7.1.
- Do not create empty stub modules in anticipation. Wait for the second consumer to materialise.

**Rule 2 — Replace meta NuGet packages with the specific provider sub-package.** When adding or auditing a package reference, check whether it is a meta-package fronting multiple sub-packages. If yes and only one sub-package is used, depend on the sub-package directly. This aligns with the dependency envelope principle: each module's dependency envelope should reflect what the module actually uses, not what its upstream conveniently bundles. A side effect is that the vulnerability surface shrinks proportionally to the trimmed dependency tree.

**Rule 3 — Feature modules and provider implementations.** Feature modules MUST NOT depend on concrete provider implementations unless the feature is itself provider-specific.

A **provider-specific feature** is one whose package name carries a provider suffix (`<App>.<Domain>.<Provider>` — e.g. distributed-locking-over-FileSystem, persistence-over-EFCore-Sqlite). Its purpose is the provider; it MAY depend on the provider's implementation layer.

All other feature modules — generic APIs, CRUD features, source-contract packages, provisioner contracts, synchronisation features — depend on the domain's contract layer (`.Core`) or, where persistence participation is part of the feature's role, on the provider-agnostic persistence surface (`.Persistence.Core`). They **never** depend on a concrete provider implementation; doing so leaks the provider choice into every consumer of that feature.

The diagnostic question: *would swapping the provider implementation require changes to this feature module?* If yes and the feature is not provider-suffixed, the dependency is misplaced.

### §2.21 Test discipline

The framework does not mandate a specific test-creation cadence (TDD, test-after, or none) for greenfield application code. That choice is application-level (§2.21.2). The framework does, however, prescribe a hard rule for test continuity during refactoring (§2.21.1).

#### §2.21.1 The golden rule of refactoring

When refactoring existing implementations, **all current tests on those implementations MUST continue to succeed after the refactor**. Reorganization is allowed to:

- Change the test's setup, fixtures, or wiring.
- Change the test's transitive dependencies.
- Move the test to a different file, project, or test assembly.

What MUST be preserved across the refactor is the **subject under test** and the **objective of the test** (the behaviour it verifies). If the subject or objective is no longer applicable, the test is a candidate for removal — but **removing a test requires explicit recorded approval from at least one architect** (unanimity is reserved for constitutional amendments per Governance). The approval is recorded in the PR description or in the plan's *Complexity Tracking* section; a passing CI is not sufficient justification for deletion.

The rule prevents a class of silent refactor regressions: a test broken by reorganization is fixed by repairing its wiring, not by deleting it.

#### §2.21.2 Greenfield test discipline

For new application code (greenfield), the choice between TDD, test-after, or no automated tests is an application-level decision. The application's derived constitution declares its discipline; the framework neither prescribes nor forbids any specific cadence.

### §2.22 Feature documentation

Every feature MUST be accompanied by documentation that lets operators, integrators, and other feature authors understand its behaviour without reading the implementation.

**Minimum required content:**

- The **event handlers** the feature registers — and which events they handle (§2.6.1).
- The **contributor interfaces** the feature implements and registers via DI (`I<X>Source` / `I<X>Contributor` / `IDraftValidator`-style) — and which fan-in event each feeds (§2.6.1 sub-pattern).
- The **tasks** the feature registers — startup tasks, recurring tasks, scheduled tasks, and their cadence.

**Anticipated additional content** *(deferred to future amendments; this list is illustrative, not exhaustive)*:

- Configuration / feature settings the feature consumes and exposes.
- Services the feature registers (replacement contracts per §2.6.2, contribution handlers per §2.6.1, infrastructure singletons).
- Inheritance relationships (which feature it extends per §2.5).
- Dependencies on other features.

The form of the documentation (README, manifest, generated reference, sidecar JSON) is application-defined. The obligation is the content, not the medium.

#### §2.22.1 Domain-level extension-points catalog

*New sub-rule (Unit C Phase-5 amendment, 2026-05-28). Renamed 2026-05-29: `DOMAIN_EVENTS.md` → `EVENTS.md`. Updated Unit 1 (2026-06-02): the two-marker categorisation collapsed to one `IEvent` concept distinguished by delivery strategy. Broadened Unit 1 (2026-06-03): the standalone `EVENTS.md` becomes a per-domain `EXTENSION_POINTS.md` whose Events section absorbs the former events catalog — because events are only one of the seams a domain exposes; overridable contracts and implementable contributor interfaces are the rest, and a consumer needs all three in one place.*

Feature documentation per §2.22 covers what an individual feature contributes. A separate, complementary obligation lands at the **domain** level: every domain whose `.Core` library exposes extension points (overridable contracts, implementable contributor interfaces, AND/OR published events) MUST ship an **extension-points catalog** as a documentation deliverable at the domain's **composition root** — the feature project that wires the defaults and registers the aggregating handlers (typically the `<Domain>` or `<Domain>.<Provider>` project, NOT the `.Core` which is contracts-only). Exception: domains with no separate feature project keep the catalog in their `.Core`. The catalog answers, in one discoverable place: *what can I override, what can I implement, and what events does this domain publish?* — without re-reading every feature implementation.

**Two axes of extension the catalog distinguishes.**

- **Override** — *replace* a default implementation of a `.Core` contract (the seam is the contract; one implementation wins via `services.Replace(...)` / register-your-own). A consumer may override one contract and keep the rest (e.g. swap the commands, keep the built-in queries).
- **Extend** — *add* a contributor implementation alongside the built-ins; the single aggregating handler runs every registered implementation (adding one never removes another). The §2.6.1 contributor-interface + single-aggregating-handler pattern.

The catalog has three sections: **Overridable contracts**, **Implementable contributor interfaces**, and **Events** (the former standalone catalog, now a section — since events are the dispatch mechanism behind the contributor interfaces and the observation surface for subscribers).

**Events are one concept; the catalog records each event's delivery strategy.** Per §2.6.1 (the single `IEvent` concept) and §2.6.6 (delivery strategies), every published event is an `IEvent`; what varies is the strategy the publisher uses and whether it reads contributions back. The catalog SHOULD make the strategy explicit per event (a column or grouping) so a reader can tell at a glance how the event behaves:

- **Sequential / contribution** — publisher awaits the chain and reads handler contributions back (e.g. `OnDraftValidating` exposes a directly-accessible `ICollection<ValidationError> Errors` that the single `ExecuteValidations` handler fills from every `IDraftValidator`). Used when the publisher needs the result; a handler throw breaks the publish.
- **Background / notification** — publisher fires and returns; subscribers observe but don't feed back (e.g. `OnDraftCreated`, `OnDraftValidated`). A subscriber failure is isolated (logged, never breaks the publish); typically fired after the transition is persisted.

A domain may publish events of either strategy, or have a present-participle gate with a past-tense outcome sibling (e.g. `OnDraftValidating` = Sequential "validation is happening, contribute"; `OnDraftValidated` = Background "validation completed, here's the outcome"). The catalog documents whichever events the domain ships; the recorded strategy pins the dispatch contract for each.

**Minimum required content per event in the catalog:**

- **Event class name** (e.g. `OnDraftCreated`).
- **Delivery strategy** — Sequential (contribution / gate) or Background (notification). Implied by section heading if the catalog groups them.
- **One-line semantic description** — what just happened in the domain (notification) or what gate has opened (contribution).
- **Payload signature** — the directly-accessible `ICollection<T>` (or rich context) the contribution sink exposes (per the §2.6.1 contribution sub-rule) and payload types handlers receive.
- **Contributor interface** *(fan-in / contribution events only)* — the `I<X>Source` / `I<X>Contributor` (or `IDraftValidator`-style) interface features implement, its method signature, whether it **returns** (Source) or **receives a context and acts** (Contributor), and the note "implement + register via DI; the single `<Action>Handler` aggregates."
- **Publication site** — which command, pipeline step, or lifecycle hook fires the event.
- **Expected handler audiences** — for fan-in events, the single aggregating handler plus the contributor-interface implementors; for notifications, typical subscribers (built-in feature, optional features, cross-domain consumers).
- **Ordering guarantees** if any — e.g., "fires after the materialised snapshot is updated but before the persistence flush."
- **Cross-references** to other domains' catalogs that consumers should be aware of.

For the **Overridable contracts** section, each entry records: a **layer badge** (`*(Core — <OwningProject>)*` when the contract is in a `.Core` and can be used without a feature-project reference, or `*(Feature contract — <FeatureProject>)*` when defined in the feature), its default implementation (and owning project — a seam may live in a sibling project of the same domain per §2.1), its signature, when/why to override, and what depends on it. For the **Implementable contributor interfaces** section, each entry records the interface name + kind + layer badge, signature, registration call, the single aggregating handler that consumes it, and a **Known implementations** list that tags every shipped implementation as `*(intra-domain — default)*` (same domain's feature) or `*(cross-domain)*` (a different domain's feature). The Known implementations list is the inter-domain dependency map navigable from the owning feature's catalog.

**Why one catalog per domain.**

- The §2.22 per-feature documentation answers *"what does THIS feature register?"* — useful when investigating a feature.
- The extension-points catalog answers *"what can I override, implement, or subscribe to in THIS domain?"* — useful when *consuming* the domain, designing a new contributor, swapping a default, or onboarding into the domain cold.
- The two views are complementary; the catalog is the index humans and AI sessions reach for first.

**Form.** Application-defined. Recommended: a single `EXTENSION_POINTS.md` file at the **composition-root feature project** of the domain (e.g. `src/<App>.<Domain>/EXTENSION_POINTS.md`), co-located with the feature's `README.md`. The `.Core` project is contracts-only (§2.1) and cannot describe defaults or wiring; the composition root can. Exception: domains with no separate feature project place the catalog in their `.Core`. Alternatives — generated reference, sidecar JSON, doc-site page — are equally valid; the obligation is the content + discoverability, not the medium.

**Worked example.** Unit 1 (2026-06-03) created a complete set of 24 per-domain catalogs at composition-root feature projects, covering all domains with extension points: `src/Elsa.Workflows.Design.Api/EXTENSION_POINTS.md` (Draft mutation events — all Background — plus lookup/command/diff-engine override seams; composition root is `WorkflowsDesignApiFeature`), `src/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md` (`OnDraftValidating` Sequential + `OnDraftValidated` Background — the canonical mixed-strategy example — plus the `IDraftValidator` contributor with intra-domain defaults tagged; composition root is `WorkflowDesignValidationsFeature`), and `src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md` (the `OnEntitySaving` + `OnEntityLoading` seams, the `IEntitySavingHandler<,>`/`IEntityLoadingHandler<,>` contributors with cross-domain implementations tagged, and the `IQueries<>`/`IUpsertCommandGenerator` override contracts — the canonical "swap one implementation, keep the rest" example). The repo-root `EXTENSION_POINTS.md` links all 24 catalogs grouped by domain family (infrastructure, expressions, HTTP, persistence, activities, workflows, legacy). The catalog set is considered **complete** for the current set of domains; the repo-root index is pure links (no inline entries).

**Maintenance obligation (MANDATORY).** The extension-points catalog is a **living document**. Updating it is MANDATORY in the same unit-of-work (commit / PR / unit) as any change that touches an extension point. The following events always trigger a catalog update:

**(a) New interface** — a new overridable contract, contributor interface, or event is added to a `.Core` or feature project → add an entry to the owning feature's catalog (and a row to the repo-root index if not already linked).

**(b) New implementation** — a class implementing a contributor interface is added to ANY feature (intra or cross-domain) → add it to the "Known implementations" list in the owning feature's catalog, tagged as intra-domain or cross-domain; add a **Cross-domain contributions** note in the implementing feature's README.

**(c) Renamed or removed interface or implementation** — update every catalog entry and known-implementations list that references it; treat as MAJOR version change per §4.

**(d) New aggregating handler or change to how a contributor interface is consumed** — update the catalog entry for every interface that handler aggregates.

**(e) New feature or `.Core` project that exposes extension points** — the feature ships a new `EXTENSION_POINTS.md` as part of its initial deliverable, before merge.

The `CatalogParityTests` reflection guard (§2.23-adjacent) catches event-heading drift automatically; contributor-interface and known-implementations drift is caught at code review. Both layers are required.

#### §2.22.2 Repo-wide extension-points index

*New sub-rule (Unit 1, 2026-06-03).*

Beyond the per-feature documentation (§2.22 — *what does THIS feature register?*) and the per-domain extension-points catalogs (§2.22.1 — *what can I override/implement/subscribe to in THIS domain?*), the repo SHIPS one **repo-wide index of every extension point** — the sanctioned answer to *"how do I extend the system?"* gathered into a single discoverable map rather than scattered across domains.

**Relationship to the per-domain catalogs.** The repo-wide index is a *map*, not a second copy: the authoritative detail for a domain lives in that domain's §2.22.1 catalog, and the index links to it. The index contains **no inline entries** — every domain that exposes extension points ships its own §2.22.1 catalog (§2.22.1 maintenance obligation), so the index is pure links. Both the index and the per-domain catalogs are named `EXTENSION_POINTS.md`, distinguished by location: one at the repo root, one at each domain's composition-root feature project.

**Form.** Application-defined; recommended a single `EXTENSION_POINTS.md` at the repo root containing a per-domain table grouped by domain family, with one row per catalog (domain name, catalog link, brief description). The §2.22.1 worked example (Unit 1) creates the Elsa instance: 24 per-domain catalogs linked from the root index, grouped as infrastructure, expressions, HTTP, persistence, activities, workflows, and legacy.

### §2.23 Unit tests

The framework prescribes a unit-test discipline that complements §2.21 (test discipline) and §2.21.1 (golden rule of refactoring). The unit-test layer carries two obligations; both are required.

#### §2.23.1 Feature-class registration test

Every feature class MUST have a unit test that:

- Constructs the feature.
- Invokes its registration entry point (`Configure`, or the equivalent on the activation type) against an `IServiceCollection`.
- Builds the resulting `IServiceProvider`.
- Asserts that every service the feature is expected to register **resolves**.

The test proves the wiring. It does not prove behaviour.

#### §2.23.2 Per-implementation unit test with stubbed dependencies

Every logic-bearing implementation class within a feature MUST have its own unit tests:

- Construct the class with stubbed/mocked dependencies.
- Exercise its public surface (and relevant internal paths if needed).
- **Every code branch MUST be covered.** Conditional paths, exception paths, default paths — each gets a test. Coverage is judged by branch, not by line.

The test proves behaviour. It does not prove wiring.

**The two obligations are independent.** Passing §2.23.1 (registration) does NOT excuse §2.23.2 (branch-covered implementation tests), and vice versa. Skipping either is forbidden.

#### §2.23.3 Visibility rule

To make §2.23.1 and §2.23.2 cleanly testable without reflection or `[InternalsVisibleTo]`:

- **Feature classes** are `public` and NOT sealed. Feature inheritance (§2.5) requires inheritability; a sealed feature class would amputate the only sanctioned cross-feature coupling pattern.
- **Logic-bearing implementations** are `public sealed`. They are not part of the §2.5 inheritance pattern; tests construct them directly. Sealing prevents accidental specialization.

This replaces the historical `internal sealed` convention, which forced tests to use reflection or `[InternalsVisibleTo]` — both code smells.

#### §2.23.4 Refactoring obligations inherited from §2.21.1

The §2.21.1 golden rule applies: existing tests on refactored implementations MUST continue to succeed **without changes to the test cases themselves**. Test setup, fixtures, wiring, transitive dependencies, and test-project location MAY change; **the subject under test and the objective of the test MUST be preserved**.

**When a test fails because of a refactor, intervention is collaborative.** The diagnosis is not a solo developer decision. Flag the failure, discuss with the architects, decide on the resolution path together, and record the decision durably (PR description, plan Complexity Tracking, follow-up file, or design notes — wherever the next reader will find it).

Diagnostic questions:

- **Has the subject moved?** Repair the test's wiring; no behaviour change.
- **Has the test's objective become invalid because the refactor resolved a bug the test was silently relying on?** Architects record the bug, the resolution, and any consumers that may have depended on the buggy behaviour. Test removal still requires architect approval per §2.21.1.
- **Has the refactor broken behaviour the test correctly asserted?** The refactor is wrong; fix the implementation, not the test.
- **Has the refactor exposed hidden coupling — a side effect of a concrete dependency that another implementation silently relied on?** This is a smell, not a feature to preserve. **Tight logic coupling between implementations is forbidden** (§2.6); only contract-level coupling is permitted (e.g. an event whose payload shape is the agreement; the publisher treats every handler uniformly; the system stays consistent and predictable). The resolution is to **lift the dependency to a contract** — typically a named event (§2.6.1) — or to remove the dependency entirely. The stub does NOT reproduce the side effect to make the test pass; that re-buries the coupling rather than resolving it. Flag, discuss with architects, decide, document.

New implementation classes that emerge from a refactor pick up new §2.23.2 obligations; new feature classes pick up §2.23.1 obligations.

#### §2.23.5 Exception boundaries — infrastructure exceptions are wrapped

Infrastructure exceptions (`JsonException`, `DbUpdateException`, `IOException`, `SqlException`, third-party-library exceptions, etc.) MUST NOT escape a feature boundary unwrapped. When a feature catches such an exception at the point where it interacts with infrastructure, it MUST translate it into a **domain-scoped exception** that carries the context needed to diagnose the failure.

**Why.** A consumer of a feature is entitled to know *which exceptions can come out* and *what they mean in the feature's vocabulary*. `JsonException` thrown from a JSON-file reconciliation source tells the caller "something went wrong in JSON" — useful to a JSON library author, useless to the reconciliation pipeline. `InvalidJsonCatalogEntryException(entryIndex=37, activityTypeKey='Acme.Foo', message='no descriptor type registered for kind "Unknown"')` tells the caller exactly which row is bad and why. The first leaks an implementation detail; the second is a domain contract.

**The rule.**

- Every public method that can fail due to infrastructure documents (via XML doc or exception list) the **domain exceptions** it throws. Infrastructure exceptions are not part of the public contract.
- At every infrastructure boundary inside the feature, wrap with `try/catch` and rethrow as a domain exception. Preserve the original as `InnerException` so the cause stays diagnosable.
- Domain exception types live in the feature's `.Core` (or a sibling) so consumers can `catch` them by type.
- The wrapping message is **specific** — it carries identifiers (row id, entry index, asset name) sufficient to localise the problem in operational logs.

**What this rule is NOT.** It is NOT a license to swallow infrastructure exceptions silently. The wrap-and-rethrow is exposed to the caller; the caller decides whether to log, retry, abort, or surface to a user.

**A use case that re-throws a raw `JsonException` past a feature boundary is a violation.** Reviewers MUST challenge such code paths.

#### §2.23.6 Integration testing — out of scope

Integration testing — composing multiple features, exercising real external systems, configuring deployed bundles of features (typically spun up in test containers and exercised end-to-end) — is **deliberately not prescribed by this constitution**.

The category structure (cross-feature contract composition, external-system integration, deployment use-case verification) and the infrastructure (test containers, real-DB harnesses, deployed-bundle scaffolding) are open questions, scoped in a follow-up for a dedicated future architects' meeting.

The unit-test discipline above does NOT depend on integration testing existing. A feature is testable through §2.23.1 and §2.23.2 alone.

### §2.24 Sanctioned patterns — the closed catalog

*New section (Unit C Phase-8 amendment, 2026-05-29; draft pending 2026-06-01 ratification.)*

The framework recognises a **closed catalog** of architectural patterns for resolving the recurring problems of modular design. **Code MUST resolve problems using a pattern from this catalog.** If a recurring problem genuinely does not fit any catalogued pattern, that gap is brought to the architects, evaluated, documented (with use case + criteria + worked example), ratified, and added to the catalog *before* the new pattern is adopted across the codebase. **Ad-hoc patterns invented at the call site are not permitted.**

This rule is a **discipline rule**, not a behaviour rule. It does not change what the existing sections (§2.1–§2.23) prescribe; it asserts that the union of those sections IS the sanctioned vocabulary, and that the catalog grows only through the gate in §2.24.3.

#### §2.24.1 Why this rule

- **Predictability.** A code reader (human or AI) opens a feature and finds patterns they have seen elsewhere in the same form. Inventing a new pattern fragments the surface area readers must learn.
- **AI-session continuity.** Future AI sessions plan against the constitution. If the constitution names the patterns the codebase uses, the AI's planning surface matches the codebase's structural surface. Random patterns force the AI to either re-derive the structure (expensive, error-prone) or document the deviation (debt).
- **Review surface.** Reviewers ask "which pattern applies?" rather than "is this pattern OK?". A bounded vocabulary makes the question answerable mechanically.
- **Recurring problems have recurring shapes.** The patterns in this catalog were discovered by hitting the same problem repeatedly. New problems may need new patterns — but the right answer is to *codify* the new pattern, not to reinvent it ad hoc each time.

#### §2.24.2 The catalog (snapshot 2026-05-29; pending 2026-06-01 ratification)

| # | Pattern | Canonical § | One-line use case | Trigger / criteria |
|---|---|---|---|---|
| 1 | **Three-layer separation per feature** | §2.1 | Decompose a feature into `.Core` (contracts) / helper (optional thin impl) / implementation (DI activation). | Every new feature follows this shape; cross-feature consumption happens through `.Core`. |
| 2 | **Feature inheritance** | §2.5 | Extend, decorate, or specialise an existing feature's registration pipeline. | The only sanctioned form of *structural* cross-feature coupling. |
| 3 | **Events — in-process pub/sub + contribution** | §2.6.1 | One `IEvent` concept; `IEventPublisher.Publish` with a pluggable delivery strategy. Sequential (default) for contribution; Background for notification. | Cross-feature composition through named events in a domain's `.Core`. |
| 3a | *sub-pattern:* Registry + StartUp Task | §2.6.1 | Sync access to async-gathered contributions. | The dispatch site is sync (e.g. a JsonConverter callback); contributions are stable at startup. |
| 3b | *sub-pattern:* Contributor interface + single aggregating handler | §2.6.1 | Many features contribute to one fan-in event without each shipping its own handler. | Domain `.Core` defines `I<X>Source` (returns), `I<X>Contributor` (receives context + acts), or `I<X>PreProcessor`/`I<X>PostProcessor` (acts on a lifecycle context at the before/after event of an OnXxxing/OnXxxed pair); features register the impl via DI; exactly one action-named `IEventHandler<On<Phase>>` injects `IEnumerable<TContributor>` and writes the event's directly-accessible `ICollection<T>` / context. *(Architect-ratified addition: Sipke 2026-06-01, Joey 2026-06-02; supersedes the withdrawn intent-revealing-methods sub-rule. Pre/post kind added Joey 2026-06-02.)* |
| 3c | *sub-rule:* Default Sequential CAN break the caller | §2.6.1 | No exception-shielding on the default path; a handler throw fails the publish (fail-fast). | Default for every Sequential publish. Resilience is opt-in via the Background strategy, NOT a default middleware. |
| 4 | **Delivery strategies** | §2.6.6 | Select dispatch behaviour per publish: Sequential (default, awaited, propagates), Parallel, Background (isolated fire-and-forget). | Background for "X happened, react if you want" (audit, telemetry, UI-push). No separate marker type — strategy is the axis. |
| 5 | **Replacement contracts** | §2.6.2 | One implementation per application (e.g. one `IDistributedLockProvider`). | Multiple registrations = conflict, not contribution. Detect at startup. |
| 6 | **Design-time vs runtime contract split** | §2.6.4 | Split a contract when it has both a design-time consumer (intellisense, picker) and a runtime consumer (binding, execution). | Two consumers, two contracts. Each may share a shape record. |
| 7 | **Sync contributor pattern — rare exception** | §2.6.5 | A sync-dispatched, behaviour-shaped contribution that cannot fit §2.6.1 or Registry + StartUp Task. | All three criteria hold (sync site, behaviour-not-data, registry-inapplicable). Reviewers MUST challenge every invocation. |
| 8 | **Adapter / Bridge** | §2.7 | Isolate a heavy or external dependency behind a stable domain contract. | Wrap third-party libraries so consumers of `.Core` never see them. |
| 9 | **Strategy** | §2.24 (this section) | Same problem, multiple algorithmic variants selected per-context. | A behaviour has 2+ legitimate implementations that consumers select between. Worked example: `IEventPublishingStrategy` (Sequential / Parallel / Background) per §2.6.6. Proposed worked example: `IReconciliationStrategy` per agenda Item 2 addendum. |
| 10 | **Factory** *(candidate, pending Monday)* | §2.24 (this section) | Encapsulate complex object construction behind a contract; prevent consumers from referencing concrete construction logic. | Object construction requires materialised dependencies, configuration, or branching logic. Promoted to first-class pattern at 2026-06-01 ratification if Monday confirms. |
| 11 | **Provider module decomposition** | §2.20 | One domain, multiple provider-specific implementations packaged as siblings. | When a domain accrues a second provider with real shared logic. Rule 1 forbids premature umbrellas. |
| 12 | **Domain-level shadow properties** | §2.9.1 | Persistence-only field that exists on the CLR entity but is hidden from the read interface. | Use real CLR property + omit from `I<Entity>` — never the provider's shadow mechanism. |
| 13 | **CQS at persistence boundary** | §2.10 | Split persistence contracts into commands (mutate) and queries (read). | Every persistence-facing interface. Combined-mutate-and-query methods are a smell. |
| 14 | **Integration vs Consumption-Shape — separate modules** | §2.14 | Integration code (`<App>.<Integration>`) and the consumption-shape (`<App>.<Integration>.<Consumer>`) ship as separate modules. | Activities or other consumer-facing modules adapting an external-system integration. |

**Patterns not in the catalog are not sanctioned.** A code reviewer who sees a pattern not in this catalog applied to a problem MUST flag it. The fix is either (a) refactor to use a catalogued pattern, or (b) bring the pattern to the architects per §2.24.3.

**The catalog is not a coding style guide.** Naming conventions, test discipline, refactor rules, versioning, packaging — all governed by their own § identifiers (§2.2, §2.16, §2.17, §2.21, §2.22, §2.23, §4) — are not *patterns* in this catalog's sense. The catalog enumerates **structural solution shapes** for cross-feature composition and modular decomposition.

#### §2.24.3 Adding a new pattern

A candidate pattern that does not yet appear in §2.24.2 follows this gate before adoption:

1. **Surface the candidate.** An architect raises it via the agenda mechanism (Governance > Amendment process).
2. **Document the use case.** What recurring problem does the pattern solve? In which features or units has the problem appeared? Why doesn't an existing catalogued pattern apply?
3. **Define the criteria.** Under what specific conditions does this pattern apply? Equally important: under what conditions does it *not* apply? (The criteria prevent the pattern from drifting into a junk-drawer general-purpose solution.)
4. **Provide a worked example.** A concrete worked example in the codebase (preferred) or a synthetic example using `<App>` placeholders. The worked example becomes part of the constitutional record.
5. **Ratify by architect consensus** per Governance.
6. **Catalog.** Add a row to §2.24.2 with the canonical § reference (a new sub-section under §2.24 or an extension of an existing pattern's §). Update plan-template Constitution Check gates if the new pattern's compliance can be checked mechanically.

A pattern adopted *before* going through this gate is technical debt — surface it retroactively for ratification, and either ratify or refactor.

**Cross-references.** §2.6.1 (the contribution mechanism); §2.6.6 (delivery strategies + worked-strategy example); §2.7 (adapter); §2.20 (provider module decomposition). Application-specific worked examples land in the application's derived constitution.

---

## §3 Runtime composition — Nuplane Strategy

Nuplane is selected as the framework's runtime hot-reload mechanism. It loads, activates, and replaces modules at runtime where the underlying runtime allows it. Application architects are free to choose any other software that meets this same intent.

**Two strategies were considered:**

- **Strategy A:** Nuplane manages everything, including `.Core` libraries. The entire runtime is replaced atomically; consumers are insulated by the abstraction layer Nuplane provides.
- **Strategy B (default and recommended):** the host pins `.Core` libraries; Nuplane focuses on dynamically loading whatever needs to be dynamically loaded — Layer-3 implementations, helper libraries, optional features. The host's contract surface is stable and inspectable, which makes observability and version compatibility easier to reason about.

**The framework's default is Strategy B.** Strategy A is not hard-excluded — it remains a valid distribution strategy where the entire runtime is replaced atomically and prevalidated. Switching to A in a specific deployment context is a deliberate choice, not the default.

**Restart criteria.** Replacing a host-pinned `.Core` package requires a host restart. Implementation-package upgrades may be hot-reloadable when their contract surface and loaded type identity remain stable. The boundary between "hot-reloadable" and "requires restart" depends on the type loader and dependency context — applications document the specific boundary in their derived constitution.

---

## §4 Versioning

### §4.1 Per-Package Versioning

The unit of versioning is the **module** (NuGet package). NuGet identity, module identity, dependency-graph identity, and SemVer policy align on the same boundary. Bundle or meta-packages may aggregate modules for distribution convenience, but they do not change the underlying rule: **compatibility is reasoned about at the package/module boundary**, and each package version must accurately reflect the public surface and dependency expectations of *that* package.

### §4.2 SemVer for `.Core` Libraries

`.Core` packages carry stricter SemVer rules than implementation packages because they form the contract surface other features depend on.

| Bump | Meaning |
|---|---|
| **Patch** | No public API change and no behavioural contract change. Internal implementation fixes, bug fixes, documentation, non-contract helper fixes. |
| **Minor** | Compatible public API expansion: new optional types, new overloads, new extension methods, additive options, default interface members (when they do not break existing implementors). **Adding a default interface method is minor** — even if existing implementors are not forced to change, the public contract has expanded. |
| **Major** | Source, binary, or behavioural contract breakage: removing/renaming members, changing signatures, adding required interface members, changing model semantics, or altering behaviour that consumers reasonably depend on. A feature-name change (§2.19) counts as major for the owning package. |

**Implementation packages** (Layer-3 features and helper libraries) are governed by the same SemVer table, but the practical bar for "behavioural contract change" is the contract surface they expose to *their* consumers, not the underlying provider's API. A provider upgrade that an implementation absorbs without changing its own public surface can be a patch.

---

## Governance

### Amendment process

This constitution is amended by consensus among the application's architects. The amendment process:

1. **Propose.** A proposed change is captured as a numbered decision (e.g. D35, D36) in the project's decision record.
2. **Discuss.** The change is debated by the architects. Where the application has a public-stakeholder review process, it runs in parallel.
3. **Ratify.** When consensus is reached, the change is folded into this document with the next version bump.
4. **Propagate.** The application's derived constitution is updated if the change affects its specializations. Speckit templates and other tooling-side artefacts are propagated in the same change.

### SemVer of the constitution itself

This document follows the same SemVer rules as a `.Core` library (§4.2):

- **PATCH** — clarifications, wording, typo fixes, non-semantic refinements.
- **MINOR** — new principle/section added or materially expanded guidance.
- **MAJOR** — backward-incompatible governance/principle removals or redefinitions.

Each amendment includes a Sync Impact Report comment at the top of the file (added by the `speckit-constitution` skill) summarising old → new version, modified principles, added/removed sections, and templates requiring updates.

### Compliance and review

- Every plan/spec generated against this constitution must satisfy a Constitution Check step that verifies compliance.
- An application's CI is expected to enforce rules that admit mechanical checking (naming conventions, dependency-envelope assertions in `.Core` packages, namespace-segment forbids).
- Where AI cannot apply a rule cleanly, that's the signal to escalate — architects intervene, analyse, and decide on a new rule for the case. The constitution matures via this loop.

### Notes for public release

When an application built on this framework chooses to publish its derived constitution publicly, the framework constitution accompanies it as the upstream document. The framework constitution may be:

- **Distributed as-is** — for adoption by other applications.
- **Versioned independently** — applications pin a specific framework constitution version they comply with.
- **Forked into a sibling specialization** — where another application needs an overlay analogous to the original derivation.

The framework constitution is intentionally written with synthetic and `<App>`-placeholder examples so it can stand alone for any of these distribution modes.

---

**Version:** 3.0.0 | **Ratified:** TODO(RATIFICATION_DATE) | **Last Amended:** 2026-06-03
