# Architecture Rationale

This reference preserves explanatory context that supports the constitutions without
being the enforceable gate surface. The constitutions keep the rules, allowed
exceptions, status, and governance.

## Workflows.Design and Workflows.Runtime split

The Workflows.Design and Workflows.Runtime split enables three deployable
distribution shapes:

| Distribution | Dependencies | Purpose |
|---|---|---|
| WorkflowDesigner | Design only | Build, edit, and persist workflow definitions. |
| WorkflowExecutor | Currently both Design and Runtime | Execute workflows. The long-term goal is Runtime-only through the deferred execution seam. |
| RuntimeMonitorService | Runtime only | Report on execution state, execution log, and runtime persistence. |

The naming convention makes the split visible at the project boundary so the
dependency direction can be audited or enforced in CI.

Rejected names for the design sub-domain:

- `Elsa.Workflows.Management.*` was too broad because management could cover
  runtime concerns.
- `Elsa.Workflows.Definitions.*` was ambiguous because both Design and Runtime
  concern workflow definitions in different forms.
- `Elsa.Workflows.Design.*` names the activity of designing workflows, which
  keeps the asymmetry with Runtime explicit.

`WorkflowDefinitionState`, read models/projections, and `WorkflowExecutable`
complete the Design-side triplet that names how authored content eventually
reaches Runtime.

## Elsa foundation repo composition

The 2026-05-11 snapshot treated the foundation repo as the local-development
baseline: the host, primitives, workflow design/runtime cores and defaults,
default persistence, activity and expression abstractions, and serialization
basics remained in `elsa-foundation`.

Standalone-feature candidates were provider-heavy or optional dependency
surfaces: non-SQLite EF Core providers, optional expression engines, MassTransit,
non-default locking providers, drive/Redis/SaaS integrations, and serialization
variations beyond the default.

EF Core persistence stayed in the foundation repo pragmatically because moving it
out impeded nearby feature development. That stance remains revisable when a
cleaner split no longer slows the work.

## Activity picker catalog rationale

The Elsa 3 picker enumerated loaded `IActivity` implementations at picker time.
That made picker output depend on loaded assemblies: implicit, difficult to
audit, and poorly suited to non-CLR activities.

The catalog-as-source-of-truth rule makes picker output depend on persisted,
queryable, provenance-bearing catalog rows. CLR activities, JSON descriptors,
workflow descriptors, scripts, and future sources can all enter through the same
catalog contract.

Activity availability is a policy layer on top of the catalog, not a replacement
for it. Host configuration may optionally define the maximum baseline through
activity keys and host-defined activity sets; management settings may narrow that
baseline; future user-context policy such as RBAC can narrow it further. This
policy controls picker addability only: existing workflow definitions still
render their authored activity nodes even when those activity types are no longer
available for new selection.

## Framework composition rationale

Replacement-contract examples:

- An `IServiceBus` contract selects one service bus implementation for the host.
  A second registration is a conflict.
- A distributed-lock contract selects one lock implementation for the application
  configuration. Multiple registrations would be ambiguous.

The contributor-interface plus single-handler sub-pattern centralizes fan-in
logic inside the event pipeline. A contributor interface such as
`IDraftValidator` or `IJsonConverterSource` names what a feature contributes;
the owning feature's single handler owns iteration, ordering, and aggregation.

The first sync-contributor exception was the now-removed EF Core implementation's `OnModelCreating` lifecycle
hook in `Elsa.Persistence.EFCore`: the dispatch site is intrinsically sync, the
contribution mutates a `ModelBuilder`, and the target object does not exist at
startup. This is historical rationale, not a shipped API. Future uses of the exception compare their shape to that case.

## Domain-level shadow properties

Domain-level shadow properties are real CLR properties on entities that are
hidden from read interfaces. They are different from provider-side shadow
properties such as EF Core string-keyed properties.

The real-property rule keeps invariant scanners, cross-cutting attributes, test
code, and non-EF providers aligned around the CLR entity shape. Provider shadow
properties remain appropriate only for provider-internal bookkeeping that does
not belong on the CLR class.

## Pattern catalog rationale

The sanctioned-patterns catalog exists to make modular-design choices
predictable. Reviewers can ask which catalogued pattern applies instead of
evaluating an invented local shape from scratch. If a recurring problem does not
fit the catalog, the new pattern is raised, documented, ratified, and then added.

## Runtime composition strategy

Two Nuplane strategies were considered:

- Strategy A: Nuplane manages everything, including `.Core` libraries, and the
  whole runtime is replaced atomically.
- Strategy B: the host pins `.Core` libraries while Nuplane dynamically loads
  Layer-3 implementations, helper libraries, and optional features.

Strategy B is the framework default because the host contract surface remains
stable and inspectable. Strategy A remains a deliberate deployment choice where
the full runtime replacement boundary is prevalidated.

## Anti-patterns

Supports the framework constitution as a whole (formerly framework §1). Moved verbatim from framework constitution v5.0.1.

The framework is designed to prevent these modular-architecture failure modes:

1. **God packages.** A single package that accumulates contracts and implementations from many domains, forcing every consumer to take on dependencies they do not need.
2. **Framework leakage into domain code.** Domain types coupled to web frameworks, expression engines, or infrastructure libraries that should be invisible to the domain.
3. **Forced heavy dependencies.** A `.Core` library that pulls in a heavy NuGet (EF Core provider, script engine, message broker SDK), forcing every consumer of the `.Core` to take that dependency whether they use it or not.
4. **Infrastructure locked into the lowest layer.** Infrastructure choices (a specific database provider, a specific lock implementation) baked into the contracts so they cannot be swapped without rewriting the contract.
5. **Inverted dependency direction.** Domain code referencing infrastructure code; consumer code referencing internals of a provider it should know only by contract.
6. **Silent DI resolution.** Multiple implementations registered against the same contract with last-write-wins behaviour, where the consumer cannot detect or diagnose the conflict.
7. **No naming convention.** Domain language buried under layer-marker buckets (`Features.*`, `Modules.*`, `Implementations.*`, `Contracts.*`) that communicate nothing the domain hierarchy doesn't already say.

Framework constitution §2 prescribes the rules that prevent each of these failure modes.

## Naming convention alternatives

Supports framework §2.2. Moved verbatim from framework constitution v5.0.1.

The `*.Contracts`, `*.Abstractions`, `Features.*`, and `Modules.*` alternatives have all been considered and rejected. Domain-only naming is the chosen convention.

The reverse form (`<App>.<ConsumerDomain>.<ModelDomain>`) is an anti-pattern. It forces the consumer domain to grow one sub-branch per upstream consumer — `<ConsumerDomain>.<ModelDomainA>`, `<ConsumerDomain>.<ModelDomainB>`, … — until the namespace is a junk drawer of unrelated model branches glued together by their shared contributor surface. A domain that grows one branch per consumer is not a domain.

## Repository organisation

Formerly framework §2.15, moved out of the gates by owner decision D2 (#2578). Moved verbatim from framework constitution v5.0.1.

**The foundation repo.** An application's baseline repo is its **foundation repo** — the umbrella that holds host setup, primitives, the main-domain `.Core` libraries, and at least one default implementation per domain that requires durable state. The contents are chosen so that cloning the foundation repo is enough to start the application without dependencies on other repositories.

**What goes in the foundation repo.** Modules typically used by the majority of the application's users *and* whose absence would make local development impractical. The specific composition is an application-level decision documented in the derived constitution.

**Strong preference, not yet ratified:** beyond the foundation repo, the framework is organised as **separate, well-scoped repositories**, not a mono-repo. Each repository is small enough that its scope is obvious from its name and its solution explorer view. Cross-repo development is supported by tooling that can clone a dependency repo, switch package references to project references for debugging, open PRs against a dependency repo, and maintain a developer-local workspace file with the relevant submodules.

**Interim approach.** Start with the single foundation repo and grow into a multi-repo cluster as features cluster naturally and reveal bundles. The how-to-implement question depends on the packaging and versioning strategy (which `.Core` libraries are bundled together, how versions are shared, where standalone features live); until those decisions are made by the application, the multi-repo strategy beyond the foundation repo cannot be fully specified.

Elsa's specialization, formerly Elsa §E2.4. Moved verbatim from Elsa constitution v5.0.0:

**framework §2.15 — Elsa specialization.** Elsa's foundation repo is this repository (`elsa-foundation`). It contains the workbench host (§E5), the baseline domain cores, and default implementations needed for local development. Heavy provider-specific or optional integrations remain candidates for standalone feature packages.

The current composition remains revisable as evidence accrues.

## Event delivery

Supports framework §2.6.6. Moved verbatim from framework constitution v5.0.1: the axes, the strategy table and the three diagnostic questions.

There is **one** event concept (`IEvent`, §2.6.1). Event behaviour has three distinct responsibility axes:

- **Delivery strategy** is publisher-owned. It controls when and how handlers run relative to the publisher's caller: Sequential, Parallel, or Background.
- **Dispatcher failure policy** is publisher-owned. It controls what the event substrate does across the handler loop when handlers fail: throw immediately, run all then throw aggregate, or log/handle gracefully and continue.
- **Subscriber failure classification** is subscriber-owned. It records what a specific handler failure means: business-critical, optional, telemetry-only, retryable, dead-letter-worthy, operationally escalated, or another application-defined classification. It informs the dispatcher policy and observability, but it cannot override the publisher-owned delivery or dispatcher-failure boundary.

A publisher selects the delivery strategy by the contract it depends on: `IInlineEventPublisher` for Sequential, `IDeferredEventPublisher` for Background. The strategies and the `IEventPublisher` behind both contracts are internal to the Events domain. The strategies are:

| Strategy | Dispatch | Publisher reads back? | Use for |
|---|---|---|---|
| **Sequential** *(default)* | Handlers run in DI-resolution order; publisher awaits the whole chain end-to-end. | **Yes** — for contribution events whose payload exposes directly-accessible collections or contexts, the publisher reads the accumulated result after the chain. | A participation gate / contribution ("I'm about to do X — who wants to participate?"); any case where the publisher's own correctness depends on handlers having run. |
| **Parallel** | Handlers dispatched concurrently; publisher awaits all. | No — ordering is unspecified, so reading back is meaningless. | Independent reactions where latency matters and the publisher still needs awaited completion. |
| **Background** | Queued to an in-process channel; publisher returns immediately; a hosted worker drains it. | **No** — publisher has already returned. | "X happened — react if you want": audit, event-sourcing stream, telemetry, UI-push. Especially state-transition signals fired *after* the transition is persisted. |

**Delivery and dispatcher failure policy are publisher-owned; handler failure meaning is subscriber-owned.** Unit 1 removed the exception-shielding-by-default position. The default Sequential path ships **no shielding** — fail-fast is the safe default for any publisher whose handlers must have run. A publisher that wants "subscriber must never break me" semantics depends on `IDeferredEventPublisher`, whose Background strategy pairs it with a graceful/logging dispatcher policy. There is no separate `ILifecycleEventSender` and no typed lifecycle marker; a "lifecycle event" is simply an `IEvent` published Background after the transition is persisted. Within that boundary, each handler/subscriber may declare its own failure classification where the substrate supports it. A handler classification cannot make a Background publish rollback-capable or make an already-returned publisher observe a failure; needing that is evidence for a separate Sequential gate event.

**Built-in dispatcher failure policies.**

- **Throw immediately** — stop the handler loop on the first failure and surface that failure to the publisher. This is the default for Sequential gate/contribution events where later handlers should not run after the gate has already failed.
- **Run all then throw aggregate** — continue dispatching remaining handlers, collect failures, then fail the publisher with an aggregate exception after every handler had a chance to run. Use when the publisher must fail, but one broken handler must not starve other business-critical handlers.
- **Log/handle gracefully and continue** — handle failures through logging/observability/retry/dead-letter policy and do not fail the publisher. This is the normal Background notification policy.

**Implementation status (amended 4.0.1).** The event substrate builds two of these policies, each fixed by its delivery strategy. Sequential is *Throw immediately*. Background is *Log/handle gracefully and continue*, applied per handler: a failing handler is logged and the remaining handlers of the same event still run. *Run all then throw aggregate*, a per-publish policy selector, handler-level failure-classification metadata, and the Parallel delivery strategy are **not built**. They are added when a concrete publisher needs them. Until then, a publisher that needs one of them raises it as a work unit; it does not hand-roll the semantics at the call site.

**Choosing a strategy — the diagnostic question.** *Does my own correctness depend on these handlers having run?*

- **Yes** → Sequential (the default). If you also need their output, give the event a contribution payload and read it back.
- **No, and a subscriber failure must not break me** → Background.
- **No, but I must wait for them and they're independent** → Parallel (rare).

**Choosing a dispatcher failure policy — the publisher diagnostic question.** *If one handler fails, should the dispatch loop stop, continue then fail, or continue without failing me?*

- First failure invalidates the whole operation → Throw immediately.
- Every handler should still run, but the publisher must fail if any failed → Run all then throw aggregate.
- Handler failures should become operational signals/retries/dead letters, not publisher failures → Log/handle gracefully and continue.

**Choosing a failure classification — the subscriber diagnostic question.** *What does my handler's failure mean inside the event's publisher-owned boundary?*

- Business-critical participation under Sequential → classify as critical so the publisher's dispatcher policy can propagate immediately or aggregate.
- Optional telemetry, audit, UI-push, or cache invalidation → classify as optional/retryable/dead-letter-worthy/operationally escalated under a graceful Background policy.
- If the desired classification requires stronger delivery or dispatcher-failure semantics than the event provides, do not subscribe as-is; raise an architecture question and model a separate event phase.

**Event naming — no `On` prefix.** An event type is named for the thing that is happening or has happened, never with an `On` prefix. `On` is reserved for the *handling* side (a method that raises or reacts to an event, as in `OnModelCreating`); putting it on the event type names the consumer instead of the fact and reads as a method where a type is meant. The BCL's own event pairs (`Closing`/`Closed`, `Saving`/`Saved`) carry no prefix, and neither do ours. Word order is subject-first (`ApiCapabilitiesCollecting`, not `CollectingApiCapabilities`) so events for one subject sort together.

**Hybrid pattern — `Xxxing` (Sequential gate) + `Xxxed` (Background outcome).** When a domain has both a participation gate and an outcome signal for the same transition, the present-participle form is published **Sequential** (validators / contributors run, publisher reads back) and the past-tense form is published **Background** (notifies that the transition happened, outcome carried in the payload, fired after persistence). Worked example: `DraftValidating` (Sequential — validators contribute errors) followed by `DraftValidated` (Background — fires after the errors are persisted; audit / UI-push react). Both are `IEvent`; only the strategy differs. With no `On` prefix in the way, the tense alone carries the gate-vs-outcome distinction.

**Cross-references.** §2.6.1 (the single event concept + contribution sub-pattern); §2.6.3 (named events, not anonymous dispatch — read-back requires Sequential); §2.22.1 (events catalog); §4.2 (adding an event is MINOR, renaming or removing is MAJOR).

## DependsOn modes

Supports framework §2.11. Moved verbatim from framework constitution v5.0.1.

The framework ships with two modes 'Fail-Fast' and 'Auto-Resolve'. The `DependsOn` mechanism declares static feature-to-feature dependencies. Features that require other services rely on DI resolution at construction time. Depending the mode: when a required service is missing, the host either fails to start or autmatically resolves the missing services through the DependsOn mechanism. This mode is configurable. In either way, DI diagnostic tools must be in place to observe missing and duplicate registrations.

Why supporting both modes is important:
- Static dependency declarations diverge from runtime reality over time. The DI container is the runtime source of truth.
- Validation tooling around the DI graph (see §2.6.2 and §3) can give the same diagnostic value without a separate declaration surface.
- A missing dependency at startup is recoverable: add the feature, restart. A stale `DependsOn` declaration is a maintenance burden.

## Packaging and versioning

Supports framework §2.13. Moved verbatim from framework constitution v5.0.1.

There is no fixed framework-level definition of "core" beyond the per-feature or per-domain `.Core` library suffix (see Glossary). What an application publishes — *which* packages it ships, *how* they are bundled, and *how* their versions relate — is a set of decisions made by that application's architects and release engineers, not constitutional rules.

The framework does prescribe one rule: **packaging cohesion follows dependency cohesion.** `.Core` libraries that reference each other must be updatable together, and their implementations must follow. Beyond that, the application chooses: a set of independently-versioned packages, a bundled distribution shape, or a hybrid. The application also chooses how versions are shared inside a bundle (whole-version sharing, major+minor sharing, major-only sharing, or fully independent).

Those decisions are **revisable**: a `.Core` and its implementations can graduate out of one bundle into a separately-published feature whenever they prove broadly useful outside that application — and they can also fold back in if the boundary turns out to be wrong.

**The rule** is the rule that produces the packaging — *not* the packaging itself: heavy dependencies stay out of any `.Core`; default implementations may ship alongside their `.Core` so the application is usable out of the box; and packaging can be revisited as evidence accrues.

## Duplication beats dependency

Supports framework §2.17. Moved verbatim from framework constitution v5.0.1.

The DRY principle is constrained by AI-era economics: *changing* code is cheap; *understanding* code across a wide blast radius is expensive. The constitution's preference order:

1. **A few duplicated lines, each call site, no shared helper** — preferred when the duplication is small and the helper would create a transitive dependency.
2. **A shared helper in `<App>.Primitives` or a domain `.Core`** — only when ≥3 consumers, no new external dependency introduced.
3. **A shared helper in a separate module/library** — only when the helper itself is a feature.

**Scope of the three-repetition rule.** It applies to `<App>.Primitives` and to broadly-shared utilities. Inside a single domain or feature, duplication is a local design tradeoff for that owner — **not** a constitutional violation.

## Exception boundaries

Supports framework §2.23.5. Moved verbatim from framework constitution v5.0.1.

**Why.** A consumer of a feature is entitled to know *which exceptions can come out* and *what they mean in the feature's vocabulary*. `JsonException` thrown from a JSON-file reconciliation source tells the caller "something went wrong in JSON" — useful to a JSON library author, useless to the reconciliation pipeline. `InvalidJsonCatalogEntryException(entryIndex=37, activityTypeKey='Acme.Foo', message='no descriptor type registered for kind "Unknown"')` tells the caller exactly which row is bad and why. The first leaks an implementation detail; the second is a domain contract.

**What this rule is NOT.** It is NOT a license to swallow infrastructure exceptions silently. The wrap-and-rethrow is exposed to the caller; the caller decides whether to log, retry, abort, or surface to a user.

## Subtraction evidence

Supports framework §2.25.3. Moved verbatim from framework constitution v5.0.1.

This bar is not theoretical caution. Static analysis systematically cannot see why code exists. Near-identical text routinely encodes independently-versioned contracts; an apparently unused symbol is often reached by reflection, configuration, or a package consumer outside the tree; and a duplication that looks collapsible can turn out to cost more to share than to repeat. A body empowered to delete, reasoning from a census, removes load-bearing code — and does so with the confidence the census supplied. The build and the test suite are the only instruments that answer the question actually being asked.

Each run records what it retired and why, and — equally — what it examined and deliberately kept. The second list is what stops the next review re-deriving the same conclusions, and it is where the "looks duplicated, is actually two independent contracts" cases get written down.

## Deferred framework sections

Framework §2.12 and §2.23.6 are now one line each in the constitution's Deferred list. Moved verbatim from framework constitution v5.0.1.

A unified classification of configuration and settings types (per-host, per-feature, per-tenant, secrets, connection-strings) is **deferred to a dedicated follow-up meeting** (Configuration & Infrastructure). The application's derived constitution may carry a placeholder pending resolution.

Pending items include:

- Secrets resolution from Key Vault / managed identity / per-tenant.
- Per-feature vs application-wide implementations of the same contract.
- Helm chart / deployment configuration conventions.

This section will be revised when the follow-up meeting closes.

Integration testing — composing multiple features, exercising real external systems, configuring deployed bundles of features (typically spun up in test containers and exercised end-to-end) — is **deliberately not prescribed by this constitution**.

The category structure (cross-feature contract composition, external-system integration, deployment use-case verification) and the infrastructure (test containers, real-DB harnesses, deployed-bundle scaffolding) are open questions, scoped in a follow-up for a dedicated future architects' meeting.

The unit-test discipline above does NOT depend on integration testing existing. A feature is testable through §2.23.1 and §2.23.2 alone.
