<!--
Sync Impact Report — Elsa Workflow Engine Constitution
========================================================

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
  - DI Container Observability & Resolve Behaviour — replacement-vs-contribution
    contract enforcement (framework §2.6.1) + explicit feature-dependency graph
    (replaces the old DependsOn from framework §2.11).
  - Packaging & Versioning + Branching Strategy
    (`follow-up-items/2026-05-11_branching_strategy_github_flow.md`) — multi-iteration
    packaging meeting will refine §E2.4 and §E5.
-->

# Elsa Workflow Engine Constitution

**Version:** 1.0.0 (draft)
**Status:** Draft for ratification by Joey Barten, Sipke Schoorstra, Frans van Ek.
**Layer:** Elsa-specific specialization of the [Modular Software Design Framework Constitution](constitution-framework.md).
**Derives from:** framework constitution **v1.0.0**.

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
- [§E3 Elsa-specific worked examples](#e3-elsa-specific-worked-examples)
  - [§E3.1 Cross-`.Core` composition](#e31-cross-core-composition-framework-21)
  - [§E3.2 Adapter pattern](#e32-adapter-pattern-framework-27--220)
  - [§E3.3 Provider contract](#e33-provider-contract-framework-26-261)
  - [§E3.4 Feature inheritance](#e34-feature-inheritance-framework-25)
  - [§E3.5 Dual-integration smell](#e35-dual-integration-smell--elsahttp--elsaexpressionsjavascript)
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

### §E2.3 `Elsa.Primitives` charter

**framework §2.3 — Elsa specialization.** The historical `Elsa.Common` package was the leakage vector through which `IronCompress`, `DistributedLock.Core`, and configuration types bled into every consumer in elsa-core (§E1, anti-pattern 6).

The 2026-05-10 first move renamed `Elsa.Common` → `Elsa.Primitives` (per framework §2.3 default outcome). The rename was mechanical: csproj folder + namespace + usings across 55 `.cs` files; deepest-chain consumer builds cleanly.

**Current charter:**

- `Elsa.Primitives` carries only truly domainless building blocks: `Result<T>`, `Page<T>`, base entity abstractions, guard helpers.
- Zero external NuGet dependencies. Without exception.
- Three-repetition rule applies.

**Anticipated further decomposition.** As code reviews land, additional concerns are split out per framework §2.3:

- `Elsa.Serialization` — already present.
- `Elsa.Notifications` — charter pending.
- `Elsa.Mediator` — charter pending if a mediator pattern materialises.

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

**framework §2.9 — Elsa specialization.** Framework §2.9 forbids the constitution from mandating a base `DbContext` type. Elsa documents an **opt-in** `ElsaDbContextBase` pattern that consumers may inherit from to receive Elsa's global entity save/load hooks (`IEntitySavingHandler`, `IEntityLoadingHandler`). These hooks are invoked before `SaveChangesAsync` reaches EF Core and are useful for shadow properties, custom deserializers, and similar cross-cutting concerns.

**Hard rules per framework §2.9:**

- The base context is **opt-in only**. Consumer-owned `DbContext` types remain first-class.
- The framework's only constraint at the EF Core contract layer is `where TDbContext : DbContext`. Never `where TDbContext : ElsaDbContextBase` or `where TDbContext : IElsaDbContext`.
- Consumers must be able to install Elsa's entity mappings and contracts **without** inheriting from `ElsaDbContextBase`.

The save/load handler hooks are documented as an opt-in feature in the relevant module's README. They are not a constitutional requirement.

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

### §E3.3 Provider contract (framework §2.6, §2.6.1)

`IPayloadSerializerConverterProvider` is defined in `Elsa.Serialization.Core` as a **contribution contract** (framework §2.6.1).

- `JsonPayloadSerializer` (in `Elsa.Serialization.Newtonsoft`) collects all registered `IPayloadSerializerConverterProvider` instances and uses their converters.
- The expressions implementation registers its own `IPayloadSerializerConverterProvider` to contribute a `VariableJsonConverter` — without either feature referencing the other.

Multiple registrations against `IPayloadSerializerConverterProvider` are the point. Resolution is via `IEnumerable<IPayloadSerializerConverterProvider>`.

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

**Status.** Untangling `Elsa.Http` ↔ `Elsa.Expressions.JavaScript` is on the *Next features* list in `PERSONAL_TODO.md`. It is the real-life worked example for framework §2.14's dual-integration rule.

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

This document declares the framework constitution version it derives from in the header (currently **v1.0.0**). When the framework constitution bumps:

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

**Version:** 1.0.0 | **Ratified:** TODO(RATIFICATION_DATE) | **Last Amended:** 2026-05-11 | **Derives from framework constitution:** v1.0.0
