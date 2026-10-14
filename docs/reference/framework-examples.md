# Framework Examples

These examples support the framework constitution without forming the enforceable gate surface. The enforceable rules remain in `.specify/memory/constitution-framework.md`.

## Integration vs consumption-shape examples

Supports framework §2.14.

For a message-broker integration:

- `<App>.Messaging.<Broker>` is the integration; it depends on the broker SDK and on `<App>.Messaging.Core`.
- `<App>.Messaging.<Broker>.<Consumer>` is the consumption-shape; it exposes broker behavior as the consumer's primitives and depends on the integration plus the consumer's `.Core`.

A consumer who wants broker messaging without exposing it as the consumer's specific primitive references only `<App>.Messaging.<Broker>`.

A `SyncContactToCrmActivity` does not live in either `<App>.Integrations.<SystemA>` or `<App>.Integrations.<SystemB>`. It ships as a dedicated synchronization/orchestration module that depends on both, such as `<App>.Integrations.<SystemA>To<SystemB>Sync`, or under a fresh orchestration domain.

## Extension-points catalog example

Supports framework §2.22.1 and §2.22.2.

Unit 1 (2026-06-03) created a set of per-domain catalogs at composition-root feature projects, covering domains with extension points.

Representative examples:

- `Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md` - draft mutation events, lookup/command/diff-engine override seams, and the `WorkflowsDesignApiFeature` composition root.
- `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md` - `DraftValidating`, `DraftValidated`, and the `IDraftValidator` contributor with intra-domain defaults.
- `Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md` - provider-neutral storage ports, migration policy, and override contracts.

The repo-root `EXTENSION_POINTS.md` links every source catalog grouped by domain family. The root index is pure links; authoritative extension-point detail remains in each local catalog.

Generated maps are now the review surface for catalog/index drift:

- `docs/maps/extension-point-map.md`
- `docs/reports/maps-v2-findings.md`

## Event contribution

Supports framework §2.6.1. Moved verbatim from framework constitution v5.0.1: the full section, including the Registry + StartUp Task worked example, the contributor-interface kinds and the superseded `AddX()` shape. The constitution keeps the gates.

The in-process composition mechanism is the **event**. A domain that wants to be extensible enumerates a set of named event types (`IEvent`) in its `.Core` library. Those events are the domain's deliberate, documented extension points — the answer to *"what can other features hook into, contribute to, or react to?"*.

Features extend a domain by **registering a handler (`IEventHandler<T>`) for one of its published events**. They do not register arbitrary provider interfaces; the domain's event vocabulary is the only contribution surface.

**One concept, one publisher, three failure/dispatch concerns.** There is a single event marker (`IEvent`), a single handler shape (`IEventHandler<T>` with `Task Handle(T, CancellationToken)`), and one publish operation, reached through `IInlineEventPublisher` (Sequential) or `IDeferredEventPublisher` (Background). What varies is the **delivery strategy** (§2.6.6), not the type name. The framework does not ship separate "domain event" / "notification" / "lifecycle event" concepts — that distinction was collapsed; the real axes are publisher-owned delivery, publisher-owned dispatcher failure policy, and subscriber-owned failure classification.

**The framework provides the dispatch mechanism.** The application supplies the publisher and a pipeline with shared middleware (logging, diagnostics). The pipeline invokes every handler for the published event under common infrastructure. Domain code does not roll its own `foreach` + `try`/`catch` loop.

**Default dispatch semantics — Sequential, synchronous, CAN break the caller.**

- Handlers are async methods (return `Task`).
- The default strategy is **Sequential**: the publisher invokes handlers in DI-resolution order and **awaits the completion of every handler** before returning. *"Synchronous"* here means **awaited end-to-end** — not single-threaded sync in the C# language sense.
- **A handler exception propagates to the publisher's caller.** The default path ships **no exception-shielding middleware**. If a handler throws under the Sequential strategy, the publish fails and the caller sees the exception — exactly as a direct method call would. This is deliberate: when a publisher needs its handlers to have run (contribution, validation, precondition), a swallowed handler failure is a silent correctness bug. Fail-fast is the safe default.
- **Resilience is a strategy choice, not a default.** Fire-and-forget isolation lives **only** in the Background strategy (and the background worker that drains it), which owns the `try`/`catch` + silent logging. A publisher that wants "subscriber must never break me" semantics depends on `IDeferredEventPublisher` (§2.6.6). It does not get that behavior from `IInlineEventPublisher`.

**Boundaries and expectations.**

- **Intent.** Internal technical communication between features within the application.
- **Scope.** Cross-feature contribution **and** intra-domain specialization. Events are valid not only across unrelated domains but also within an inheritance/implementation chain — e.g. a domain-specific event like `EntitySaving(DbContext, EntityEntry)` is consumed only by features that already specialize an EF-Core-aware implementation. The mechanism is the same; the audience is narrower.
- **Delivery coupling follows the publisher-owned strategy.** Cross-domain coupling exists at the **contract level** (the event's shape). The publisher owns the delivery strategy because only the publisher knows whether its caller needs handlers to have completed, whether it will read contributions back, and whether the transition has already been persisted. A publisher whose dispatch is purely informational (audit, event-sourcing stream, telemetry, UI-push) SHOULD publish Background so a subscriber failure cannot break a transition that has already been persisted. A publisher that reads contributions back MUST publish Sequential.
- **Handler-loop failure policy is publisher-owned.** The publisher owns the failure policy for the dispatch loop because only the publisher knows whether one failing handler should stop dispatch immediately, whether every handler should still get a chance to run before an aggregate failure is thrown, or whether failures should be logged/handled without breaking the publisher. The baseline policies are **Throw immediately**, **Run all then throw aggregate**, and **Log/handle gracefully and continue**. Only two are built: Sequential throws immediately, and Background logs per handler and continues. Run all then throw aggregate is not available until a publisher needs it (§2.6.6).
- **Failure meaning is subscriber-owned classification.** A handler/subscriber owns the meaning of its own failure: business-critical, optional telemetry, retryable, ignorable, report-only, dead-letter-worthy, or escalated. That classification operates **inside the publisher-owned delivery and dispatcher-failure boundary**. A subscriber MAY declare its failure classification where the event substrate supports handler-level metadata, but it MUST NOT silently strengthen the publisher's delivery contract. If a subscriber must be able to fail the publisher, the domain needs a Sequential gate/contribution event or a separate phase; it is a modeling error to attach such a subscriber only to a Background notification.
- **Completeness under Sequential.** Every registered handler is dispatched in order; the publisher does not skip handlers or return early. Because the default does not shield, the first throwing handler halts the chain — that is the intended fail-fast behaviour, not a completeness violation. Background completeness is FIFO across the channel and isolated per subscriber.
- **Handler independence.** Handlers MUST NOT depend on each other. Each handler reacts to the event for its own purpose; observed handler ordering or side effects of one handler MUST NOT be relied upon by another. A handler that depends on another handler's prior side effect is already in violation of §2.6 (no tight logic coupling between implementations).
- **Diagnostics.** Logging, tracing, and diagnostics attach uniformly via the same middleware surface. A failing handler under Background is observable in operational logs with full identifying context; the application's observability stack treats those entries as first-class signals.

**Sub-pattern — Registry initialization via StartUp Task (for sync access).**

When a registry-style or index-style consumer needs access to contributions from sync code — e.g. a `JsonConverter` callback in a serializer, or any constructor that builds an index — the contribution is still gathered through an event. The async population happens once at startup; sync access happens afterwards. The pattern:

1. The domain defines a **Registry** with a `Register<T>(item)` method (and accessor methods).
2. The domain publishes an `<RegistryName>Initializing` event whose payload exposes a directly-accessible `ICollection<T>`.
3. The feature implementing the domain registers a **StartUp task** that publishes the event Sequentially and flushes the contributions into the registry:

   ```
   var event = new <RegistryName>Initializing();
   await eventPublisher.Publish(event);   // default Sequential — contributions read back
   registry.RegisterAll(event.Items);
   ```

4. Other features extend the domain by implementing the registry's contributor interface (`I<X>Source`) and registering it via DI; the single aggregating handler (§2.6.1 sub-pattern below) flushes every source's items into the event's carried `ICollection<T>`.
5. After startup, sync code accesses the populated registry directly — no async dispatch at the access site.

The result: **all cross-feature contribution flows through the same event pipeline**, including cases where the access pattern is sync. There is no separate "sync fallback" mechanism.

**Sub-pattern — Contributor interface + single aggregating handler.**

A fan-in event that gathers contributions from many features MUST use the **contributor-interface + single-handler** shape — NOT one `IEventHandler<PhaseEvent>` per contributing feature. **This rule is scoped to the contribution axis.** Features are free to register `IEventHandler<T>` for independent subscriptions — observing an event for auditing, cache invalidation, cross-cutting reactions, or any purpose unrelated to the fan-in aggregation — without any restriction. The constraint is: if you are *contributing to a fan-in*, use the typed interface rather than a scatter of handlers all doing the same kind of thing (imagine 100 JavaScript declaration contributors each implemented as a separate `IEventHandler<DeclarationsDocumentGenerating>` — that is the chaos this rule prevents).

- The domain `.Core` defines a **contributor interface** that describes the *intent* of the contribution (e.g. `IDraftValidator`, `IJsonConverterSource`). Features implement it and register it via DI (`services.AddScoped<TContributor, Impl>()`) — they do **not** register a separate event handler for *this contribution purpose*.
- The owning feature registers **exactly one** action-named `IEventHandler<PhaseEvent>` (e.g. `ExecuteValidations`, `RegisterJsonConverters`) that injects `IEnumerable<TContributor>`, iterates every contributor, and writes the aggregate into the event.
- The event payload exposes a **directly-accessible `ICollection<T>`** (for flat-collection sinks) or a rich mutable context object (for heterogeneous sinks). No private backing list, no `AddX()` methods, no `IReadOnlyList<T>` read accessor — the single handler is the only writer, so encapsulation is met without ceremony.

**The three contributor-interface kinds.** The suffix MUST match the method shape AND the event topology:

- **`I<X>Source`** — the contributor **returns** its items and touches no shared object (a *pull*). Used when the sink is a flat collection. Signature returns `IEnumerable<T>` (or `ValueTask<IEnumerable<T>>`). The single handler aggregates every source's return into the event's `ICollection<T>`. **"Source" is preferred over "Provider".**
- **`I<X>Contributor`** — the contributor **receives a context and acts on it** (a *push*), returning void / `ValueTask`. Used when the sink is a rich mutable context that accepts heterogeneous, multi-operation contributions (e.g. a declarations context exposing `AddVariable(...)`/`AddType(...)`). The single handler hands the context to each contributor in turn. *(5.0.0: new push-style contracts use `I<X>Extension`, per ADR 0080 D5; `I<X>Contributor` stays valid until each domain's rename batch lands, and the rest of this section reads `Contributor` as either name.)*
- **`I<X>PreProcessor` / `I<X>PostProcessor`** — the contributor **acts on a lifecycle context**, returning void / `ValueTask`. Used when the contribution event is one half of an **Xxxing / Xxxed (before / after) pair**: a pre-processor runs at the *before* event to prepare the context (register functions/values, set up state); a post-processor runs at the *after* event to act on the result (copy outputs back, clean up). Each event still has exactly one aggregating handler (e.g. `PreProcessScript` / `PostProcessScript`) injecting `IEnumerable<I<X>PreProcessor>` / `IEnumerable<I<X>PostProcessor>`. **When the events form a before/after pair, prefer this kind over `Contributor`** — `PreProcessor`/`PostProcessor` names the lifecycle position, which reads far more naturally than a generic "Contributor" for a paired hook.

**Action-named suffixes (sanctioned alongside the four above).** When the suffix names the **specific action** the interface performs on the received context, an action-named suffix is preferred over the generic `Contributor` — e.g. **`I<X>Validator`** (inspects the context and *returns* findings, like `IDraftValidator.Validate`) and **`I<X>Handler`** (receives the context + a typed subject and *acts* at a named lifecycle point, like `IEntitySavingHandler.Handle` / `IEntityLoadingHandler.Handle` on the EF Core save/load seam). These are Contributor-kind (context-receiving — a *Validator* returns its findings; a *Handler* acts in place); they simply carry an intent-revealing, action-specific name instead of the bare `Contributor`. The topology rule is unchanged: the contributor interface is what features implement + register via DI, and the single aggregating `IEventHandler<PhaseEvent>` (e.g. `ExecuteValidations`, `ApplyEntitySavingHandlers`) still owns the event subscription and dispatches every implementation. Use an action-named suffix when one exists naturally; fall back to `Source`/`Contributor` when no single verb captures the contribution.

Never name a return-style interface `Contributor`, nor a context-acting interface `Source`. A *Source* yields data; a *Contributor* (incl. its action-named forms `Validator`/`Handler`) performs operations against a target; a *PreProcessor*/*PostProcessor* performs operations against a target at a named point in a before/after lifecycle.

**Intra-domain vs. cross-domain contributions.** When a contributor-interface implementation ships in the **same domain** as the `.Core` contract it satisfies, it is an *intra-domain default* — the feature delivers on its own Core's promises. When it ships from an **unrelated domain**, it is a *cross-domain contribution* — the primary mechanism by which domains extend each other's pipelines without direct coupling. The owning feature's `EXTENSION_POINTS.md` catalog (§2.22.1) MUST list all known implementations of each contributor interface, tagged accordingly (`*(intra-domain — default)*` / `*(cross-domain)*`). The contributing feature MUST note in its own `README.md` (under a **Cross-domain contributions** section) which contracts from other domains it satisfies and link back to the owning domain's catalog. This makes the inter-domain dependency map visible from both ends.

**Superseded shape.** The earlier "intent-revealing `AddX()` methods + private list + `IReadOnlyList<T>` read accessor" sub-rule is **withdrawn** — it added ceremony to every contribution event and folded contribution logic into the payload. Contribution events now expose a directly-accessible `ICollection<T>` written solely by the single aggregating handler. Records remain discouraged for events carrying a mutable contribution sink — use `sealed class` with a get-only collection auto-property initialised to `[]`.

**Domain-design consequence.** Enumerating a domain's events is part of *defining the domain*. The §2.18 domain-identification methodology gains a corollary: once a domain's purpose and contracts are established, the architect MUST also identify *where other features can bring something or do something* — and surface those points as named events in the domain's `.Core`. A domain whose extension points are implicit (registered providers nobody can enumerate, events nobody can find handlers for) fails this rule.

**Feature documentation requirement.** Every feature's documentation MUST contain a discoverable inventory of:

- Which **event handlers** it registers.
- Which **tasks** it registers (e.g. startup, recurring, scheduled).

The full shape of the feature-documentation contract is governed by §2.22 (Feature documentation).

## Adapter / Bridge

Supports framework §2.7. Moved verbatim from framework constitution v5.0.1.

The adapter pattern is a **design default** whenever a feature contains functionality that may be reusable outside its current consumer. When building a feature, the framework expects the author to ask: *"is this functionality potentially useful to consumers other than the one I'm building it for, and if so, can I expose it via a clean adapter or bridge so those other consumers do not have to take on my dependencies?"*

> *Pattern (synthetic).* `<App>.Locking.Core` defines `IDistributedLockProvider` with zero external dependencies. `<App>.Locking.<Provider>` registers an adapter that wraps the third-party lock library. The third-party package is not visible to any consumer of `<App>.Locking.Core`. Replacing one lock provider with another means shipping a new module — no changes anywhere else.

## Extension-points catalog

Supports framework §2.22.1. Moved from framework constitution v5.0.1. Owner decision D3 (#2578) makes six per-event fields required and the rest optional; the field lists below are split accordingly.

**Two axes of extension the catalog distinguishes.**

- **Override** — *replace* a default implementation of a `.Core` contract (the seam is the contract; one implementation wins via `services.Replace(...)` / register-your-own). A consumer may override one contract and keep the rest (e.g. swap the commands, keep the built-in queries).
- **Extend** — *add* a contributor implementation alongside the built-ins; the single aggregating handler runs every registered implementation (adding one never removes another). The §2.6.1 contributor-interface + single-aggregating-handler pattern.

The catalog has three sections: **Overridable contracts**, **Implementable contributor interfaces**, and **Events** (the former standalone catalog, now a section — since events are the dispatch mechanism behind the contributor interfaces and the observation surface for subscribers).

**Events are one concept; the catalog records delivery and failure expectations.** Per §2.6.1 (the single `IEvent` concept) and §2.6.6 (delivery and failure strategies), every published event is an `IEvent`; what varies is the strategy the publisher uses, whether it reads contributions back, which dispatcher failure policy applies, and what subscriber failure classifications are expected. The catalog SHOULD make the delivery strategy explicit per event (a column or grouping) so a reader can tell at a glance how the event behaves:

- **Sequential / contribution** — publisher awaits the chain and reads handler contributions back (e.g. `DraftValidating` exposes a directly-accessible `ICollection<ValidationError> Errors` that the single `ExecuteValidations` handler fills from every `IDraftValidator`). Used when the publisher needs the result; a handler throw breaks the publish.
- **Background / notification** — publisher fires and returns; subscribers observe but don't feed back (e.g. `DraftCreated`, `DraftValidated`). A subscriber failure is isolated (logged, never breaks the publish); typically fired after the transition is persisted.

A domain may publish events of either strategy, or have a present-participle gate with a past-tense outcome sibling (e.g. `DraftValidating` = Sequential "validation is happening, contribute"; `DraftValidated` = Background "validation completed, here's the outcome"). The catalog documents whichever events the domain ships; the recorded strategy pins the dispatch contract for each.

**Required content per event in the catalog (D3):**

- **Event class name** (e.g. `DraftCreated`).
- **Delivery strategy** — Sequential (contribution / gate) or Background (notification). Implied by section heading if the catalog groups them.
- **One-line semantic description** — what just happened in the domain (notification) or what gate has opened (contribution).
- **Payload signature** — the directly-accessible `ICollection<T>` (or rich context) the contribution sink exposes (per the §2.6.1 contribution sub-rule) and payload types handlers receive.
- **Contributor interface** *(fan-in / contribution events only)* — the `I<X>Source` / `I<X>Contributor` (or `IDraftValidator`-style) interface features implement, its method signature, whether it **returns** (Source) or **receives a context and acts** (Contributor), and the note "implement + register via DI; the single `<Action>Handler` aggregates."
- **Publication site** — which command, pipeline step, or lifecycle hook fires the event.

**Optional content per event:**

- **Dispatcher failure policy** — whether handler failures throw immediately, are collected and thrown as an aggregate after all handlers run, or are logged/handled gracefully while dispatch continues.
- **Subscriber failure classifications** — which classifications are expected or permitted for handlers of this event, especially whether business-critical subscribers are allowed or should instead use a separate Sequential gate event.
- **Expected handler audiences** — for fan-in events, the single aggregating handler plus the contributor-interface implementors; for notifications, typical subscribers (built-in feature, optional features, cross-domain consumers).
- **Ordering guarantees** if any — e.g., "fires after the materialised snapshot is updated but before the persistence flush."
- **Cross-references** to other domains' catalogs that consumers should be aware of.

For the **Overridable contracts** section, each entry records: a **layer badge** (`*(Core — <OwningProject>)*` when the contract is in a `.Core` and can be used without a feature-project reference, or `*(Feature contract — <FeatureProject>)*` when defined in the feature), its default implementation (and owning project — a seam may live in a sibling project of the same domain per §2.1), its signature, when/why to override, and what depends on it. For the **Implementable contributor interfaces** section, each entry records the interface name + kind + layer badge, signature, registration call, the single aggregating handler that consumes it, and a **Known implementations** list that tags every shipped implementation as `*(intra-domain — default)*` (same domain's feature) or `*(cross-domain)*` (a different domain's feature). The Known implementations list is the inter-domain dependency map navigable from the owning feature's catalog.

**Why one catalog per domain.**

- The §2.22 per-feature documentation answers *"what does THIS feature register?"* — useful when investigating a feature.
- The extension-points catalog answers *"what can I override, implement, or subscribe to in THIS domain?"* — useful when *consuming* the domain, designing a new contributor, swapping a default, or onboarding into the domain cold.
- The two views are complementary; the catalog is the index humans and AI sessions reach for first.

**Form.** Application-defined. Recommended: a single `EXTENSION_POINTS.md` file at the **composition-root feature project** of the domain (e.g. `src/<App>.<Domain>/EXTENSION_POINTS.md`), co-located with the feature's `README.md`. The `.Core` project is contracts-only (§2.1) and cannot describe defaults or wiring; the composition root can. Exception: domains with no separate feature project place the catalog in their `.Core`. Alternatives — generated reference, sidecar JSON, doc-site page — are equally valid; the obligation is the content + discoverability, not the medium.

Generated maps are the current review surface for extension-point catalog/index drift.

## Minimum-viable-project exemption classes

Supports framework §2.16.1. Moved verbatim from framework constitution v5.0.1.

**Guidance for *new* projects.** When creating a *new* implementation project (Layer 3) that would ship below roughly **100 physical lines of code**, briefly record why it earns a separate NuGet identity rather than folding into an existing sibling. The bar is *a reason*, not a size — a single sentence in the PR or the project's README suffices.

**Exemption test.** A project below the guidance size is **automatically legitimate** — no justification required — when merging it away would either (a) violate another gate, or (b) collapse a real capability boundary. The following exception classes are the enumerated worked catalog of (a) and (b); a project fitting any of them is exempt:

1. **Contracts-only `.Core` seam** — a `.Core` project consisting of interfaces, delegates, abstract types, and models only. It is the cross-`.Core` composition mechanism (§2.1); merging it into a consumer would break that mechanism.
2. **Primitives / constants project** — a zero- or near-zero-dependency project of shared constants and primitive models.
3. **Provider leaf** — a provider-specific implementation (`*.Sqlite`, `*.PostgreSql`, `*.FileSystem`, …) whose small size reflects that the provider genuinely needs little code. Provider isolation (§2.7 / the `.Core`-heavy-package rule) *mandates* the separate project.
4. **Migration / compatibility boundary** — a one-way import or compatibility surface whose separateness is required by an explicit boundary rule.
5. **Layer-2 helper / adapter library** — an opt-in default implementation that isolates a single focused external dependency, kept out of Layer 1 by §2.1.
6. **Independently-composable feature unit or cross-domain contribution seam** — a project that exists as a separately-toggleable feature composition unit, or a project that bridges two domains by contributing one domain's types to another (and so cannot fold into either without creating a forbidden dependency).

A project that fits **none** of these classes and is below the guidance size is a candidate for merging into its nearest sibling — but the merge is still discretionary under §2.16 (which favors keeping NuGet identity stable), not mandatory.

## Sanctioned patterns catalog

Supports framework §2.24, still draft pending ratification (owner decision D4, #2578). Moved verbatim from framework constitution v5.0.1: the full table and the adoption steps.

The framework recognises a **closed catalog** of architectural patterns for resolving the recurring problems of modular design. **Code MUST resolve problems using a pattern from this catalog.** If a recurring problem genuinely does not fit any catalogued pattern, that gap is brought to the architects, evaluated, documented (with use case + criteria + worked example), ratified, and added to the catalog *before* the new pattern is adopted across the codebase. **Ad-hoc patterns invented at the call site are not permitted.**

This rule is a **discipline rule**, not a behaviour rule. It does not change what the existing sections (§2.1–§2.23) prescribe; it asserts that the union of those sections IS the sanctioned vocabulary, and that the catalog grows only through the gate in §2.24.3.

| # | Pattern | Canonical § | One-line use case | Trigger / criteria |
|---|---|---|---|---|
| 1 | **Three-layer separation per feature** | §2.1 | Decompose a feature into `.Core` (contracts) / helper (optional thin impl) / implementation (DI activation). | Every new feature follows this shape; cross-feature consumption happens through `.Core`. |
| 2 | **Feature inheritance** | §2.5 | Extend, decorate, or specialise an existing feature's registration pipeline. | The only sanctioned form of *structural* cross-feature coupling. |
| 3 | **Events — in-process pub/sub + contribution** | §2.6.1 | One `IEvent` concept; `IInlineEventPublisher` (Sequential) for contribution, `IDeferredEventPublisher` (Background) for notification. | Cross-feature composition through named events in a domain's `.Core`. |
| 3a | *sub-pattern:* Registry + StartUp Task | §2.6.1 | Sync access to async-gathered contributions. | The dispatch site is sync (e.g. a JsonConverter callback); contributions are stable at startup. |
| 3b | *sub-pattern:* Contributor interface + single aggregating handler | §2.6.1 | Many features contribute to one fan-in event without each shipping its own handler. | Domain `.Core` defines `I<X>Source` (returns), `I<X>Contributor` (receives context + acts), or `I<X>PreProcessor`/`I<X>PostProcessor` (acts on a lifecycle context at the before/after event of an Xxxing / Xxxed pair); features register the impl via DI; exactly one action-named `IEventHandler<PhaseEvent>` injects `IEnumerable<TContributor>` and writes the event's directly-accessible `ICollection<T>` / context. |
| 3c | *sub-rule:* Default Sequential CAN break the caller | §2.6.1 | No exception-shielding on the default path; a handler throw fails the publish (fail-fast). | Default for every Sequential publish. Resilience is opt-in via the Background strategy, NOT a default middleware. |
| 4 | **Delivery strategies** | §2.6.6 | Select dispatch behaviour per publish: Sequential (default, awaited, propagates), Parallel, Background (isolated fire-and-forget). | Background for "X happened, react if you want" (audit, telemetry, UI-push). No separate marker type — strategy is the axis. |
| 5 | **Replacement contracts** | §2.6.2 | One implementation per application (e.g. one `IDistributedLockProvider`). | Multiple registrations = conflict, not contribution. Detect at startup. |
| 6 | **Design-time vs runtime contract split** | §2.6.4 | Split a contract when it has both a design-time consumer (intellisense, picker) and a runtime consumer (binding, execution). | Two consumers, two contracts. Each may share a shape record. |
| 7 | **Sync contributor pattern — rare exception** | §2.6.5 | A sync-dispatched, behaviour-shaped contribution that cannot fit §2.6.1 or Registry + StartUp Task. | All three criteria hold (sync site, behaviour-not-data, registry-inapplicable). Reviewers MUST challenge every invocation. |
| 8 | **Adapter / Bridge** | §2.7 | Isolate a heavy or external dependency behind a stable domain contract. | Wrap third-party libraries so consumers of `.Core` never see them. |
| 9 | **Strategy** | §2.24 (this section) | Same problem, multiple algorithmic variants selected per-context. | A behaviour has 2+ legitimate implementations that consumers select between. Worked example: `IEventPublishingStrategy` (Sequential / Parallel / Background) per §2.6.6. |
| 10 | **Factory** *(candidate pending ratification)* | §2.24 (this section) | Encapsulate complex object construction behind a contract; prevent consumers from referencing concrete construction logic. | Object construction requires materialised dependencies, configuration, or branching logic. |
| 11 | **Provider module decomposition** | §2.20 | One domain, multiple provider-specific implementations packaged as siblings. | When a domain accrues a second provider with real shared logic. Rule 1 forbids premature umbrellas. |
| 12 | **Domain-level shadow properties** | §2.9.1 | Persistence-only field that exists on the CLR entity but is hidden from the read interface. | Use real CLR property + omit from `I<Entity>` — never the provider's shadow mechanism. |
| 13 | **CQS at persistence boundary** | §2.10 | Split persistence contracts into commands (mutate) and queries (read). | Every persistence-facing interface. Combined-mutate-and-query methods are a smell. |
| 14 | **Integration vs Consumption-Shape — separate modules** | §2.14 | Integration code (`<App>.<Integration>`) and the consumption-shape (`<App>.<Integration>.<Consumer>`) ship as separate modules. | Activities or other consumer-facing modules adapting an external-system integration. |

**Patterns not in the catalog are not sanctioned.** A code reviewer who sees a pattern not in this catalog applied to a problem MUST flag it. The fix is either (a) refactor to use a catalogued pattern, or (b) bring the pattern to the architects per §2.24.3.

**The catalog is not a coding style guide.** Naming conventions, test discipline, refactor rules, versioning, packaging — all governed by their own § identifiers (§2.2, §2.16, §2.17, §2.21, §2.22, §2.23, §4) — are not *patterns* in this catalog's sense. The catalog enumerates **structural solution shapes** for cross-feature composition and modular decomposition.

### §2.24.3 Adding a new pattern

A candidate pattern that does not yet appear in §2.24.2 follows this gate before adoption:

1. **Surface the candidate.** An architect raises it via the agenda mechanism (Governance > Amendment process).
2. **Document the use case.** What recurring problem does the pattern solve? In which features or units has the problem appeared? Why doesn't an existing catalogued pattern apply?
3. **Define the criteria.** Under what specific conditions does this pattern apply? Equally important: under what conditions does it *not* apply? (The criteria prevent the pattern from drifting into a junk-drawer general-purpose solution.)
4. **Provide a worked example.** A concrete worked example in the codebase (preferred) or a synthetic example using `<App>` placeholders. The worked example becomes part of the constitutional record.
5. **Ratify by architect consensus** per Governance.
6. **Catalog.** Add a row to §2.24.2 with the canonical § reference (a new sub-section under §2.24 or an extension of an existing pattern's §). Update plan-template Constitution Check gates if the new pattern's compliance can be checked mechanically.

A pattern adopted *before* going through this gate is technical debt — surface it retroactively for ratification, and either ratify or refactor.

**Cross-references.** §2.6.1 (the contribution mechanism); §2.6.6 (delivery strategies); §2.7 (adapter); §2.20 (provider module decomposition).
