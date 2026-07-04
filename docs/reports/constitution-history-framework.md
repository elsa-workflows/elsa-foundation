# Framework Constitution Draft History

Status: extracted from `.specify/memory/constitution-framework.md` during Constitution Thinning v1.

This file preserves raw framework constitution provenance. It is historical report material, not the enforceable gate surface. Use [constitution-draft-history.md](constitution-draft-history.md) as the curated entrypoint.

~~~markdown
<!--
Sync Impact Report — Modular Software Design Framework Constitution
=====================================================================

Version change: 3.0.0 (draft) → 3.1.0 (draft)
Date: 2026-07-04
Unit: W21 / MD-5 minimum-project-size amendment (Elsa 4 architecture review
  2026-07, review-modularity.md MD-5 + Open Question 1). Ratified by Sipke
  2026-07-04 from the W21 proposal report
  (elsa-4-w21-md5-minimum-project-size-amendment.md).
  SemVer: MINOR — new guidance subsection added; no existing rule removed or
  redefined.

§2.16.1 (new) — Minimum-viable-project guidance. Codifies that the
  finer-grained-split preference (§2.16) is not overridden by small project
  size: no minimum line count forces a merge. New Layer-3 projects below
  ~100 physical LoC record a one-sentence justification; a six-class
  exemption test (contracts-only .Core seam, primitives/constants, provider
  leaf, migration/compatibility boundary, Layer-2 helper/adapter,
  independently-composable feature unit or cross-domain contribution seam)
  makes projects automatically legitimate where a merge would violate
  another gate or collapse a capability boundary. Soft guidance + exemption
  test, deliberately NOT a threshold gate — the audit showed a hard gate
  would create more violations than it resolves. Framework text kept
  example-free per the synthetic-examples policy; worked examples cascade
  to the Elsa constitution (§E5 interpretive note).
-->
~~~

~~~markdown
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
    creates `src/Elsa/Workflows/Design/Core/EVENTS.md` (all lifecycle) and
    `src/Elsa/Workflows/Design/Validations/Core/EVENTS.md` (mixed —
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
~~~
