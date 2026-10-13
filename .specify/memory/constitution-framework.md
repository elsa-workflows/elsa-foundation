<!--
SYNC IMPACT REPORT — 6.0.0 -> 6.0.1 (2026-10-13)
PATCH: §2.23.3 wording (#2591, owner decision E2). Implementations are tested through [InternalsVisibleTo] to their own domain's test assemblies rather than "the project's own test assembly", because the test tree has one test project per domain, not per project; a project never grants [InternalsVisibleTo] to another production project.
Gate index: the §2.23.3 row names the realigned checks (InternalsVisibleTo per domain, the public API tracking opt-in, feature-class shape), switched on domain by domain by #2591.
Templates requiring updates: .specify/memory/constitution.md (re-pin v6.0.1).
Approval: Sipke Schoorstra, 2026-10-13 (#2591).
-->
<!--
SYNC IMPACT REPORT — 5.0.1 -> 6.0.0 (2026-10-12)
MAJOR: compaction to enforceable gates (#2578, ADR 0080 D6); sections are removed and three gates redefined. Owner decisions D1–D9 per #2578.
Removed: §1 (-> docs/reference/architecture-rationale.md#anti-patterns); §2.12 and §2.23.6 (-> Deferred); §2.15 (-> architecture-rationale.md#repository-organisation, D2); §2.18.4 (restated §2.21.1).
Redefined: §2.22.1 required per-event catalog fields 11 -> 6 (D3); §2.24 catalog is a one-line list and stays draft (D4); §2.23.3 states the 5.0 rule, its check realigned per domain by #2591 (D6).
Added: Gate index; Deferred; the derivation rules and the generic parts of Elsa §E2.3, §E5 and Governance.
Moved: rationale, examples and methodology to docs/reference/ (architecture-rationale, framework-examples, refactoring-methodology); review-only gates to docs/contributing/review-checklist.md. Every surviving § number is kept. Reports before 6.0.0 are removed; git keeps them.
Templates requiring updates: .specify/memory/constitution.md (re-pin v6.0.0); .specify/templates/plan-template.md.
Approval: Sipke Schoorstra, 2026-10-12 (#2578), on his authority alone; open to revision if Joey Barten or Frans van Ek dissent, as with the 4.0.0 ratification.
-->
# Modular Software Design Framework Constitution

**Version:** 6.0.1 | **Ratified:** 2026-08-08 | **Last Amended:** 2026-10-13

**Status:** Ratified 2026-08-08 by Sipke Schoorstra. Governance calls for consensus among Joey Barten, Sipke Schoorstra and Frans van Ek; this ratification was taken on Sipke Schoorstra's authority alone and is open to revision if the other architects dissent. A section marked draft (§2.24) is **not** covered by it.

**Layer:** the generic gate layer; the Elsa constitution, [constitution.md](constitution.md), derives from it. Terms: [docs/glossary/root.md](../../docs/glossary/root.md). Rationale and examples: [docs/reference/](../../docs/reference/README.md).

## Gate index

Gates with an automated check (tests in `Essentials/tests/Architecture/Tests/`). Every other gate is checked at review against [docs/contributing/review-checklist.md](../../docs/contributing/review-checklist.md).

| § | Gate | Check |
|---|---|---|
| §2.1 | A `.Core` references no implementation or helper project | `ArchitectureGuardTests.Core_projects_do_not_reference_implementation_projects` |
| §2.1 | A `.Core` takes no heavy package | `ArchitectureGuardTests.Core_projects_do_not_reference_heavy_packages` |
| §2.1 | A `.Core` holds no implementation-shaped type | `ArchitectureGuardTests.Core_projects_contain_no_implementation_shaped_types` |
| §2.2 | No `/core/` or `/modules/` solution folder; project paths follow the domain tree (other naming rules: review) | `ArchitectureGuardTests.Solution_has_no_global_layer_marker_folders`, `.Project_paths_match_domain_tree_convention` |
| §2.3 | `<App>.Primitives` has no external package | `ArchitectureGuardTests.Elsa_primitives_has_no_external_package_references` |
| §2.5, §2.23.1 | Every feature composes and its services resolve (virtual `ConfigureServices` and inheritance shape: review) | `FeatureCompositionTests.Every_feature_composes_and_each_service_it_registers_resolves` |
| §2.9 | Persistence contracts take no concrete provider | `ArchitectureGuardTests.In_scope_persistence_contract_projects_remain_free_of_concrete_provider_dependencies` |
| §2.20 | Only admitted projects resolve EF Core packages | `EfCoreDependencyGuardTests` |
| §2.23.3 | Grants reach only the granting project's own domain's tests; tracked public surface; feature classes public and not sealed | `ArchitectureGuardTests.InternalsVisibleTo_grants_go_only_to_the_granting_domains_test_assemblies` (all three grant forms; one listed host exception), `PublicApiFilesGuardTests` (the `ElsaTrackPublicApi` opt-in, both files and the analyzer move together), `FeatureClassShapeGuardTests` (per activated domain). #2591 switches the opt-in and the activation on domain by domain. |

## Preamble — What this document is

This is the constitution of a **Modular Software Design Framework**, with Nuplane and CShells as supporting technologies. Its framework-neutral rules govern how an application decomposes into modules and features, how those depend on each other, how they are packaged and versioned, and how they compose at runtime.

An application writes a derived constitution that cites a version of this document, pins its **root domain name** (`<App>`), adds specializations and links its examples. **Derivation:** every rule here applies by reference; where the derived constitution is silent, this document applies; an override is declared as `framework §X — <App> specialization: …`.

## Glossary (working definitions)

Canonical terms live in [docs/glossary/root.md](../../docs/glossary/root.md). **Capability** and **envelope** are retired; use **feature** and **module**. A module may expose several features only when they share its dependency envelope; a heavy dependency needed by only some of them is the trigger to split it.

## §2 The Architecture

### §2.1 Three-Layer Separation — applied per feature

The Core / Helper / Features separation applies **once per feature or domain**, not as a global stack; there is no global "Core" library.

| Layer | Name | Contains | Rule |
|---|---|---|---|
| 1. Core | `<App>.<Domain>[.<SubDomain(s)>].Core` | Interfaces, abstract classes, models, thin implementations (§2.17), domain helper extensions | NuGets: `Microsoft.Extensions.*` Abstractions and `Microsoft.Extensions.Primitives` only. MAY reference another `.Core`; never a feature implementation, a helper or a heavy dependency. |
| 2. Helper (optional) | `<App>.<Domain>[.<SubDomain(s)>].<Qualifier>` | Lightweight defaults over one focused NuGet | Never referenced by layer 1 (§2.4). |
| 3. Feature | `<App>.<Domain>[.<SubDomain(s)>][.<Variation>]` | `IFeature`, DI registration, concrete services | Couples to other features only through inheritance (§2.5) or `.Core` contracts (§2.6). |

**Cross-`.Core` references** are the primary composition mechanism. **Implementation-to-implementation references** are forbidden between unrelated sub-domains; within one provider family one MAY specialize another (a SQL Server package extending an EF Core base, §2.7.1), directionally and visibly in package names and the dependency graph.

### §2.2 Naming Convention

Namespaces use **domain language only**. Layer-marker buckets (`Features.*`, `Modules.*`, `Implementations.*`, `Providers.*`, `Adapters.*`) appear in no package or namespace; `*.Contracts`/`*.Abstractions` are rejected ([rationale](../../docs/reference/architecture-rationale.md#naming-convention-alternatives)). Usual suffixes: `.Core`, `.<ImplementationOrProvider>`, `.Api`, `.Testing`.

- `<Variation>` is added only when a domain hosts more than one implementation or one implementation implies a variation choice.
- **Secondary-domain sub-rule.** A feature that only contributes implementations of another domain's contributor or provider interfaces, with no models of its own, is `<App>.<ModelDomain>.<ConsumerDomain>`: the domain defining the models wins the prefix. The reverse form is an anti-pattern.
- A class implementing `IFeature` MAY end in `Feature`; its package never has a `Features` segment. The module-composition domain is `<App>.Modularity`, not `<App>.Features`.

### §2.3 The Framework Primitives Library

Small focused packages are preferred: `<App>.Primitives` holds only domainless building blocks (`Result<T>`, `Page<T>`, guard helpers), and a narrower concern gets its own package (e.g. `<App>.Persistence.Primitives`). `<App>.Foundation.Core` is introduced only if foundation contracts (`IFeature`, `IShell`, activation manifest, discovery) emerge as a coherent set that does not fit `<App>.Modularity.Core`, never eagerly.

**Hard rules.** A type with a recognisable domain belongs in that domain's `.Core`. `<App>.Primitives` carries zero external NuGet dependencies, without exception, behind a high admission bar. `<App>.Foundation.Core`, if it exists, carries only what §2.1 allows a `.Core`.

### §2.4 Helper Libraries — domain-owned

A helper library is an optional, non-activatable package of lightweight implementations for one domain, usually isolating a dependency unfit for `.Core`; features of that domain may share it when they share its dependency envelope and lifecycle. It belongs to exactly one domain or sub-domain, is never referenced by a `.Core`, and exposes no `IFeature`. A helper useful across unrelated domains is a smell: its content belongs in `<App>.Primitives` or reveals a missing `.Core`.

### §2.5 Feature Inheritance

Feature inheritance is the only sanctioned structural coupling between features; implementation libraries never reference each other. Feature B extends A by inheriting A's feature class, overriding `ConfigureServices` (usually after `base.ConfigureServices(services)`) and depending on A's package; A is unaware of B.

- `ConfigureServices` MUST be `virtual`, and feature classes are not sealed (§2.23.3).
- A collaborator that is an extension point MUST be registered against a contract its consumers depend on. Others MAY be concrete `internal sealed` types; when an inheriting feature must replace one, the base promotes it to a contract in that change (ADR 0080 D3).

**§2.5.1 Service lifetimes — scoped by default.** Singleton is reserved for application-wide static values (options snapshots, registries, caches, immutable lookups). A service that executes logic (resolves, scans, validates, reconciles, dispatches, maps) is scoped unless a documented reason requires otherwise.

### §2.6 Cross-feature composition mechanisms

Tight logic coupling between concrete implementations is forbidden: a cross-feature dependency goes through a contract (an event payload or a replacement contract), never another class's side effects or internals. A test failure exposing such coupling (§2.23.4) means lifting it to a contract.

**§2.6.1 Events — the in-process pub/sub + contribution mechanism.** A domain's extension points are named `IEvent` types in its `.Core`; features register `IEventHandler<T>` and one `IEventPublisher.Publish` dispatches through a pipeline with shared middleware. Domain code does not hand-roll handler loops. Long form: [framework-examples](../../docs/reference/framework-examples.md#event-contribution).

- **Default Sequential CAN break the caller:** handlers run in DI order, awaited end-to-end, with no exception shielding. Isolation is opt-in through Background (§2.6.6).
- **The publisher owns delivery and failure policy; the subscriber owns failure meaning.** An informational publisher SHOULD publish Background; one that reads contributions back MUST publish Sequential. A subscriber MUST NOT strengthen the publisher's delivery contract; one that must be able to fail the publisher needs a Sequential gate event.
- Handlers MUST NOT depend on each other's ordering or side effects.
- **Fan-in** MUST use a contributor interface plus exactly one action-named aggregating `IEventHandler<PhaseEvent>` that injects `IEnumerable<TContributor>` and writes the event's directly-accessible `ICollection<T>` or rich context. Features register contributors via DI, not handlers. The payload is a `sealed class` with a get-only collection initialised to `[]`. Independent subscriptions (audit, cache invalidation) are unrestricted.
- **Suffix matches shape:** `I<X>Source` returns items (preferred over `Provider`); `I<X>Contributor` receives a context and acts (new ones use `I<X>Extension`, ADR 0080 D5; both accepted until each domain's rename lands); `I<X>PreProcessor`/`I<X>PostProcessor` act at an `Xxxing`/`Xxxed` pair; `I<X>Validator`/`I<X>Handler` are action-named contributors. A return-style interface is never a `Contributor`, nor a context-acting one a `Source`.
- **Registry + StartUp Task** serves sync readers: a startup task publishes `<RegistryName>Initializing` Sequentially and flushes the items into a registry.
- A domain MUST surface its contribution points as named events in its `.Core`. Its catalog (§2.22.1) tags each known contributor intra- or cross-domain, and a contributing feature lists the contracts it satisfies in a **Cross-domain contributions** README section.

**§2.6.2 Replacement contracts — single-implementation services.** One implementation per application or runtime context. The kind MUST be declared on the contract (marker, attribute, naming convention or metadata). A second registration MUST be prevented or detected with a clear diagnostic at startup; silent last-write-wins is forbidden. Contribution-style consumers (`IEnumerable<T>`) MUST NOT use a replacement contract.

**§2.6.3 Named events, not anonymous generic dispatch.** Hiding a real dependency behind `IMediator`, `IEventBus` or similar anonymous dispatch is forbidden: a publisher that expects a handler to run publishes a named `IEvent` from a `.Core`, Sequentially. Consuming handler output MUST use Sequential with a contribution payload read after the chain; reading back from a Background publish is a bug.

**§2.6.4 Design-time vs runtime contract split.** A surface with both a design-time consumer (intellisense, schema validation, declarations, pickers) and a runtime consumer (binding, execution, evaluation) MUST split into two independently dispatched contracts. They MAY share a `.Core` shape record. Neither carries the other's concerns.

**§2.6.5 Sync contributor pattern — rare exception.** A sync contributor interface (`IEnumerable<TContributor>`, invoked synchronously) is permitted only when **all** hold: (1) the dispatch site is intrinsically sync (e.g. `DbContext.OnModelCreating`); (2) contributors add behaviour against a shared target, not data; (3) Registry + StartUp Task cannot apply because the contribution depends on the lifecycle moment. Try §2.6.1 and Registry + StartUp Task first. Every contributor is invoked, none is swallowed, and a rename is MAJOR. Reviewers MUST challenge every use. Example: [elsa-worked-examples](../../docs/reference/elsa-worked-examples.md#sync-contributor-pattern-ientitymodelcreatinghandler).

**§2.6.6 Delivery and failure strategies — one event concept, three responsibility axes.** Delivery strategy and dispatcher failure policy are publisher-owned; a subscriber's failure classification cannot override them. **Sequential** (default): DI order, awaited, the publisher reads contributions back; for gates and contributions. **Parallel**: concurrent, awaited, no read-back. **Background**: queued and drained by `BackgroundEventPublisher`, the publisher returns at once; for audit, telemetry, UI push and signals after a persisted transition. Policies: Throw immediately, Run all then throw aggregate, Log/handle and continue. **Built today:** Sequential throws immediately; Background logs per handler and continues. Run-all-then-aggregate, a per-publish policy selector, classification metadata and Parallel are not built; a publisher needing one raises a work unit instead of hand-rolling it. Diagnostic questions: [architecture-rationale](../../docs/reference/architecture-rationale.md#event-delivery).

**Event naming.** An event type never has an `On` prefix; it names the fact, subject first (`ApiCapabilitiesCollecting`). A gate/outcome pair is `Xxxing` (Sequential) plus `Xxxed` (Background, after persistence): `DraftValidating` / `DraftValidated`.

### §2.7 Adapter / Bridge — as a design default

Functionality other consumers could reuse is exposed through an adapter or bridge, so its `.Core` contract carries no external dependency and the third-party package stays in one implementation package ([example](../../docs/reference/framework-examples.md#adapter--bridge)).

**§2.7.1 Decision rule — inheritance, adapter, or provider/contributor.** In order: specializing another implementation's registration pipeline → inheritance (§2.5); isolating a heavy or external dependency → adapter; independent additive contribution → a contributor contract dispatched through events (§2.6.1). Patterns may combine. Always declare the contract's kind (§2.6.2).

### §2.8 Extension Methods — Decision Framework

An extension method body over **three lines** is reviewed before merge. Branching or business logic moves to an interface or service; try/catch, null checks and locals are fine, in the contract library closest to the extended type, never an implementation library. Under three consumers, inline it.

### §2.9 Persistence Base Context — application-level

The framework mandates no base `DbContext`, no `IEntitySavingHandler`/`IEntityLoadingHandler`, no `I<App>DbContext`. Consumer-owned `DbContext` types stay first-class and MUST be able to install application mappings and contracts without inheriting an application base context; a base context is an opt-in capability of the derived constitution. The only contract-layer constraint is `where TDbContext : DbContext`.

Persistence invariants (immutable properties, audit timestamps, tenant scoping, append-only) belong to the model and hold on every provider. `*.Persistence.Core` carries store contracts, persistence models and invariants, never provider mechanism; `*.Persistence.<Provider>` enforces them its own way.

**§2.9.1 Domain-level "shadow" properties.** A persistence-only field is a real entity property omitted from the read interface (`I<Entity>`), never a provider shadow property; those are for provider bookkeeping the application never touches ([rationale](../../docs/reference/architecture-rationale.md#domain-level-shadow-properties)).

### §2.10 CQS at the Persistence Boundary

Persistence contracts split commands (mutate; return nothing or a confirmation token such as a new id) from queries (return data, never mutate). A method doing both is a smell.

### §2.11 DependsOn — Fail-Fast and Auto-Resolve modes

`DependsOn` declares static feature dependencies. The host runs in a configurable mode: **Fail-Fast** (a missing required service stops startup) or **Auto-Resolve** (the missing feature is resolved through `DependsOn`). Either way, DI diagnostics MUST surface missing and duplicate registrations ([rationale](../../docs/reference/architecture-rationale.md#dependson-modes)).

### §2.13 Packaging and Versioning — application-level

What ships, how it bundles and how versions are shared are revisable application decisions. The rule: **packaging cohesion follows dependency cohesion**. `.Core` libraries that reference each other update together and their implementations follow; heavy dependencies stay out of every `.Core`; defaults MAY ship beside their `.Core`.

### §2.14 Integration vs. Consumption-Shape — separate modules

An external-system integration and each consumption-shape adapting it (an activity, UI binding, messaging endpoint) are separate modules. A unit integrating two external systems is a boundary smell; if it truly needs both, it is its own module whose name and envelope state both ([examples](../../docs/reference/framework-examples.md#integration-vs-consumption-shape-examples)).

### §2.16 Refactor-cost test

Before grouping features into a module or moving a type between layers, weigh the cost of undoing it: preserve NuGet identity wherever possible, justify any identity change against every consumer's break, and prefer the finer-grained split when in doubt.

**§2.16.1 Minimum-viable-project guidance.** No minimum size. A *new* Layer-3 project under about **100 lines** records one sentence on why it earns its own identity, unless merging it away would (a) violate another gate or (b) collapse a real capability boundary. The enumerated cases of (a) and (b) are: (1) a contracts-only `.Core` seam, (2) primitives or constants project, (3) provider leaf, (4) migration or compatibility boundary, (5) Layer-2 helper or adapter, or (6) independently composable feature unit or cross-domain contribution seam ([detail](../../docs/reference/framework-examples.md#minimum-viable-project-exemption-classes)). Merging one that fits none stays discretionary.

### §2.17 Duplication beats dependency

Prefer (1) a few duplicated lines over a helper that adds a transitive dependency; (2) a helper in `<App>.Primitives` or a domain `.Core` only with three or more consumers and no new external dependency; (3) a separate library only when the helper is a feature. This three-repetition rule applies to `<App>.Primitives` and broadly shared utilities; inside one domain, duplication is the owner's call.

**Thin implementation** (the `.Core` admission bar of §2.1, §2.3, §2.4): small, dependency-light and mechanical (delegation, wrapping, defaults, guards, option binding, trivial transformation); never business policy, persistence strategy, infrastructure logic or domain-deciding branches.

### §2.18 Applying the Framework — Methodology for Refactoring a Modular Monolith

The inductive three-step method lives in [docs/reference/refactoring-methodology.md](../../docs/reference/refactoring-methodology.md); refactor work follows §2.21.1. Its gates: **§2.18.1** a domain's purpose fits one verb-led sentence, and one needing "and" between unrelated capabilities is two domains; **§2.18.2** a contract another domain or feature would plausibly consume goes in the `.Core`, an implementation-internal one stays inside; **§2.18.3** a feature duplicated across domains is promoted to one `.Core` that each domain configures.

### §2.19 Feature identity — the feature `name`

A feature's `name` is its stable logical identity: the configuration binding key and the key in diagnostics, telemetry, the features registry, activation manifests and dependency metadata. It is neither display text nor a dependency mechanism (§2.11). Names are unique in the host's package graph and stable across refactors. A rename is MAJOR (§4.2) and not supported in place: create a new feature, then retire the old one.

### §2.20 Provider module decomposition

A domain with providers decomposes into `<App>.<Domain>.Core` (contracts, no external dependencies) and `<App>.<Domain>.<Provider>` (feature, provider code, adapters). **Rule 1:** no premature umbrella; one provider keeps everything in `<App>.<Domain>.<Provider>`, and `<App>.<Domain>.<ProviderFamily>` is extracted only when a second provider brings real shared logic; no empty stubs. **Rule 2:** reference the specific provider sub-package, not a fronting meta-package. **Rule 3:** a feature module MUST NOT depend on a concrete provider implementation unless it is provider-suffixed itself; others depend on `.Core` or `.Persistence.Core`.

### §2.21 Test discipline

**§2.21.1 The golden rule of refactoring.** A refactor preserves behaviour. Tests that still describe valid behaviour MUST keep passing; their setup, fixtures, wiring, dependencies and location MAY change. A refactor MAY rewrite or delete a test whose code was removed or whose behaviour another test proves; the PR names which, and the reviewer checks it; no separate architect approval is needed. A test failing because the refactor changed behaviour it correctly asserted means the refactor is wrong.

#### §2.21.2 Greenfield test discipline

For greenfield code, TDD, test-after or no automated tests is an application-level choice the derived constitution declares; the framework prescribes no cadence.

### §2.22 Feature documentation

Every feature MUST document the event handlers it registers and the events they handle, the contributor interfaces it implements and the fan-in event each feeds, and the tasks it registers with their cadence. The medium is application-defined.

**§2.22.1 Domain-level extension-points catalog.** A domain whose `.Core` exposes extension points ships `EXTENSION_POINTS.md` at its composition-root feature project (its `.Core` when there is none), with **Overridable contracts**, **Implementable contributor interfaces** (with Known implementations) and **Events**. Each event records at least its name, delivery strategy, one-line meaning, payload, contributor interface (fan-in only) and publication site; other fields are optional (D3). Each overridable contract records its layer badge, default implementation and owning project, signature, when to override and what depends on it. Each contributor interface records its name, kind, layer badge, signature, registration call, the single aggregating handler and Known implementations tagged intra- or cross-domain. Entry spec: [framework-examples](../../docs/reference/framework-examples.md#extension-points-catalog).

**Maintenance obligation (MANDATORY).** The catalog changes in the same unit of work as: (a) a new contract, contributor interface or event; (b) a new contributor implementation anywhere, tagged intra- or cross-domain, with a **Cross-domain contributions** note in the implementer's README; (c) a rename or removal, also MAJOR (§4); (d) a new aggregating handler or changed consumption; (e) a new feature or `.Core` with extension points, which ships its catalog before merge. Drift is caught at review; a test MUST NOT assert on catalog text.

**§2.22.2 Repo-wide extension-points index.** One root `EXTENSION_POINTS.md` links every domain catalog by domain family and holds no inline entries ([example](../../docs/reference/framework-examples.md#extension-points-catalog-example)).

### §2.23 Unit tests

Tests prove behaviour at risk, at the cheapest level that proves it (ADR 0080 D1).

- **§2.23.1 Composition test.** One test builds a shell from every feature and resolves each feature's registrations. A feature needs its own registration test only when its wiring carries logic (conditional registration, decoration, options validation).
- **§2.23.2 Behaviour tests.** Logic-bearing implementations are tested through behaviour, with stubs where cheapest, covering risk: decisions, failure paths, boundaries, concurrency, persistence semantics. Coverage is the reviewer's judgement; trivial branches need no test. Tests MUST NOT assert on markdown, documentation or source text; a mechanically checkable rule gets an analyzer or an architecture test.
- **§2.23.3 Visibility rule.** Feature classes are `public` and not sealed. Implementations are `internal sealed` unless public surface, tested through `[InternalsVisibleTo]` to their own domain's test assemblies; a project never grants `[InternalsVisibleTo]` to another production project. The public surface (contracts users implement or call, models crossing a package boundary, activities, feature classes, options, entry points) is tracked with PublicApiAnalyzers in every packable project. Checked per domain as #2591 realigns each (D6).
- **§2.23.4 Refactoring obligations.** Hidden coupling a refactor exposes is the defect: lift it to a contract, typically a named event, or remove it. A stub MUST NOT reproduce the side effect to pass a test.
- **§2.23.5 Exception boundaries — infrastructure exceptions are wrapped.** Infrastructure exceptions (`JsonException`, `DbUpdateException`, `IOException`, `SqlException`, third-party) MUST NOT escape a feature boundary unwrapped. Rethrow a domain exception from the feature's `.Core` or a sibling, with the original as `InnerException` and identifiers that localise the failure; public methods document it. Wrapping is not swallowing. Reviewers MUST challenge a raw infrastructure exception crossing a boundary ([rationale](../../docs/reference/architecture-rationale.md#exception-boundaries)).

### §2.24 Sanctioned patterns — the closed catalog

**Status:** Draft pending ratification (D4). Code resolves recurring modular-design problems with a catalogued pattern; an ad-hoc pattern is flagged at review. Full table: [framework-examples](../../docs/reference/framework-examples.md#sanctioned-patterns-catalog).

**§2.24.2 The catalog.** Three-layer separation (§2.1); feature inheritance (§2.5); events with Registry + StartUp Task and contributor + single aggregating handler (§2.6.1); delivery strategies (§2.6.6); replacement contracts (§2.6.2); design-time vs runtime split (§2.6.4); sync contributor exception (§2.6.5); adapter / bridge (§2.7); Strategy (selectable algorithm variants, e.g. `IEventPublishingStrategy`); Factory (candidate); provider module decomposition (§2.20); domain-level shadow properties (§2.9.1); CQS (§2.10); integration vs consumption-shape (§2.14).

**§2.24.3 Adding a new pattern.** Raise it with the architects; document the use case, criteria for and against, and a worked example; ratify; add it to §2.24.2. A pattern adopted before ratification is debt: ratify or refactor.

### §2.25 Consolidation review — the subtractive obligation

A periodic review (cadence set by the architects; quarterly suggested) is a normal work unit, and the only one with standing to retire artifacts. **§2.25.2 Standing:** move a spec to a terminal status or archive; delete a guard test whose gate was superseded, naming the replacement; prune a stale catalog entry; merge projects under §2.16/§2.16.1; delete generated artifacts and generators without consumers. **§2.25.3 Evidence bar — subtraction is verified, never inferred:** every removal MUST be justified by evidence the build or test suite produces (a compile, a test run, a reachability check, a guard still green without the code); a static census, text search or similarity judgement is not sufficient. **§2.25.4 Report:** what was retired and why, and what was examined and kept.

## §3 Runtime composition — Nuplane Strategy

Nuplane, or software meeting the same intent, loads and replaces modules at runtime. The default is **Strategy B**: the host pins its baseline contract packages and Nuplane loads implementations, helpers and optional features. Strategy A (atomic replacement of a prevalidated runtime) remains a deliberate deployment choice. Replacing a host-pinned `.Core` needs a restart; the application documents its hot-reload boundary.

## §4 Versioning

- **§4.1** The unit of versioning is the module (NuGet package); compatibility is judged at the package boundary, and each version reflects that package's surface and dependency expectations.
- **§4.2** SemVer for `.Core`: **Patch**, no public API or behavioural contract change; **Minor**, compatible expansion (optional types, overloads, extension methods, additive options, default interface members, a new event); **Major**, source, binary or behavioural breakage (removed or renamed members or events, changed signatures, required interface members, changed model semantics, a feature rename).
- Implementation packages follow the same table against their own surface; an absorbed provider upgrade can be a patch.

## Deferred

Not governed until an amendment settles them. Findings: [unfinished work](../../docs/reports/unfinished-work.md); planned work routes through `docs/program-goals/`.

- **§2.12 Configuration & settings classification** (per-host, per-feature, per-tenant, secrets, connection strings), and with it Elsa configuration (formerly Elsa §E4).
- **§2.23.6 Integration testing** (cross-feature composition, real external systems, deployed bundles); §2.23.1–§2.23.2 do not depend on it.
- **Further feature documentation** (§2.22): configuration, registered services, inheritance, feature dependencies.
- **Design ↔ Runtime seam** (Elsa §E2.2): the carrier type, activity-contract surfacing, the role of publication, an `ActivityRegistry`.

## Governance

**Amendment process.** A change is proposed as a numbered decision (in this repository an ADR under `docs/adr/`), ratified by consensus of the architects, folded in with a version bump, and propagated in the same change to derived constitutions and the Speckit templates.

**Versioning** follows §4.2: PATCH for wording, MINOR for a new or materially expanded section, MAJOR for a removed or redefined rule. Each amendment adds a Sync Impact Report at the top; reports before the current MAJOR are dropped (git keeps them). Derived constitutions re-pin on every bump: review the clarification on PATCH, every specialization on MINOR, everything on MAJOR (usually bumping MAJOR too).

**Compliance and review.** A plan or spec passes a Constitution Check that loads this file and the derived constitution and applies both Gate indexes and [docs/contributing/review-checklist.md](../../docs/contributing/review-checklist.md). CI enforces what admits mechanical checking. A rule that cannot be applied cleanly is escalated to the architects. Written with `<App>` placeholders, this document can be distributed as-is, versioned independently, or forked.
