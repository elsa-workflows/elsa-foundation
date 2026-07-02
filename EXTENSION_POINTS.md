# Extension Points — repo-wide index

The codebase-wide **index** of the sanctioned ways to extend Elsa without modifying the framework (framework §2.6.1, §2.24.2, §2.22.2). This is the map; the authoritative per-domain detail lives in each domain's own `EXTENSION_POINTS.md`.

## Two axes: override vs. extend

Every seam below is one of two kinds (framework §2.22.1):

| Axis | What you do | Mechanism |
|---|---|---|
| **Override** | *Replace* a default implementation of a `.Core` contract — "bring my own data access / my own commands". | One implementation wins: `services.Replace(...)` / register-your-own. You can override one contract and keep the rest (e.g. swap the commands, keep the built-in queries). |
| **Extend** | *Add* an implementation alongside the built-ins. | A single aggregating handler resolves **all** registered implementations and runs each. Adding one never removes another. |

## Intra-domain vs. cross-domain contributions

When a contributor-interface implementation ships in the **same domain** as the `.Core` contract it satisfies it is an *intra-domain default* — the feature delivers on its Core's promises. When it ships from an **unrelated domain** it is a *cross-domain contribution* — the primary mechanism by which domains extend each other's pipelines without direct coupling (framework §2.6.1).

Each per-domain catalog lists **Known implementations** for every contributor interface, tagged `*(intra-domain — default)*` or `*(cross-domain)*`. Features that implement contracts from other domains note it in their own `README.md` under **Cross-domain contributions**.

## How to read the kinds

Per framework §2.6.1 contributor sub-pattern, an extension interface is one of:

| Kind | Shape | Naming |
|---|---|---|
| **Source** | *Returns* values (pull). `GetX()` / `Read()`. | `I…Source` |
| **Contributor** | *Receives* a context and acts on it (push). `Contribute(ctx)`. | `I…Contributor` |
| **PreProcessor / PostProcessor** | Receives a context and acts at a specific phase. | `I…PreProcessor` / `I…PostProcessor` |
| **Validator** | Action-named contributor: inspects and **returns** findings. | `I…Validator` |
| **entity Handler** | Action-named contributor: receives ctx + entity and **acts** at a persistence lifecycle point. | `I…Handler` |

The action-named suffixes (`…Validator`, `…Handler`) are semantically sanctioned alongside Source / Contributor / PreProcessor / PostProcessor (framework §2.6.1).

**The rule for fan-in contribution flows:** when contributing to a domain's fan-in contribution flow, features implement the typed contributor interface and register it via DI — they do **NOT** register a dedicated `IEventHandler<T>` for that contribution purpose. Exactly one aggregating handler per contribution event resolves `IEnumerable<TContributor>` and dispatches every implementation. Independent subscriptions — auditing, cache invalidation, reacting to an event for a feature's own unrelated purpose — are unrestricted and use `IEventHandler<T>` directly.

## The doc layering

- **per-feature READMEs** — what THIS feature registers/provides; includes a **Cross-domain contributions** section when the feature implements contracts from other domains.
- **per-domain `EXTENSION_POINTS.md`** — the authoritative catalog for THAT domain: its overridable contracts, its implementable contributor interfaces, and (as an **Events** section) the events it publishes.
- **this file** — the repo-wide index that points into each per-domain catalog.

**Root-indexing policy:** this root index includes every discovered `src/**/EXTENSION_POINTS.md`
catalog. Source/contribution-module catalogs are indexed here even when they point back to an
authoritative owner lifecycle catalog. Generated maps report index/catalog drift as review signals,
not automatic constitution violations.

---

## Per-domain catalogs

### Infrastructure

| Domain | Catalog |
|---|---|
| Events (substrate — `IEvent`, `IEventHandler<T>`, strategies) | [`src/Elsa/Events/EXTENSION_POINTS.md`](src/Elsa/Events/EXTENSION_POINTS.md) |
| Mediator (command / request pipelines) | [`src/Elsa/Mediator/EXTENSION_POINTS.md`](src/Elsa/Mediator/EXTENSION_POINTS.md) |
| Pipelines (middleware — Core-only, no feature project) | [`src/Elsa/Pipelines/Core/EXTENSION_POINTS.md`](src/Elsa/Pipelines/Core/EXTENSION_POINTS.md) |
| Tasks (startup / recurring / background tasks) | [`src/Elsa/Tasks/EXTENSION_POINTS.md`](src/Elsa/Tasks/EXTENSION_POINTS.md) |
| Caching (cache manager + change-token signaling) | [`src/Elsa/Caching/Memory/EXTENSION_POINTS.md`](src/Elsa/Caching/Memory/EXTENSION_POINTS.md) |
| Serialization (JSON converter sources) | [`src/Elsa/Serialization/SystemText/EXTENSION_POINTS.md`](src/Elsa/Serialization/SystemText/EXTENSION_POINTS.md) |
| Locking (distributed lock provider) | [`src/Elsa/Locking/FileSystem/EXTENSION_POINTS.md`](src/Elsa/Locking/FileSystem/EXTENSION_POINTS.md) |
| Modularity API (shell feature-management surface) | [`src/Elsa/Modularity/Api/EXTENSION_POINTS.md`](src/Elsa/Modularity/Api/EXTENSION_POINTS.md) |

### Expressions

| Domain | Catalog |
|---|---|
| Expressions (evaluator + descriptor providers) | [`src/Elsa/Expressions/EXTENSION_POINTS.md`](src/Elsa/Expressions/EXTENSION_POINTS.md) |
| JavaScript expressions (pre/post-processors) | [`src/Elsa/Expressions/JavaScript/EXTENSION_POINTS.md`](src/Elsa/Expressions/JavaScript/EXTENSION_POINTS.md) |
| JavaScript rendering (declaration contributors) | [`src/Elsa/Expressions/JavaScript/Rendering/EXTENSION_POINTS.md`](src/Elsa/Expressions/JavaScript/Rendering/EXTENSION_POINTS.md) |
| Liquid expressions (rendering lifecycle) | [`src/Elsa/Expressions/Liquid/EXTENSION_POINTS.md`](src/Elsa/Expressions/Liquid/EXTENSION_POINTS.md) |

### HTTP

| Domain | Catalog |
|---|---|
| HTTP (downloadable content handlers) | [`src/Elsa/Http/EXTENSION_POINTS.md`](src/Elsa/Http/EXTENSION_POINTS.md) |

### Agent and Identity

| Domain | Catalog |
|---|---|
| Agent core (sessions, policy, context, proposals, provider facade, audit) | [`src/Elsa/Agent/Core/EXTENSION_POINTS.md`](src/Elsa/Agent/Core/EXTENSION_POINTS.md) |
| Foundation Identity (authentication providers, IAM contracts, permissions, ownership capabilities, security guards) | [`src/Elsa/Foundation/Identity/Abstractions/EXTENSION_POINTS.md`](src/Elsa/Foundation/Identity/Abstractions/EXTENSION_POINTS.md) |
| Workflow Agent (workflow explain/troubleshoot/change-proposal contribution) | [`src/Elsa/Agent/Workflows/EXTENSION_POINTS.md`](src/Elsa/Agent/Workflows/EXTENSION_POINTS.md) |

### Secrets

| Domain | Catalog |
|---|---|
| Secrets core (contracts — manager, resolver, repository, store, type provider, audit sink) | [`src/Elsa/Secrets/Core/EXTENSION_POINTS.md`](src/Elsa/Secrets/Core/EXTENSION_POINTS.md) |
| Secrets (default services + shell feature registration; service overrides for repository, value protector, audit sink, stores, type providers) | [`src/Elsa/Secrets/EXTENSION_POINTS.md`](src/Elsa/Secrets/EXTENSION_POINTS.md) |

### Persistence

| Domain | Catalog |
|---|---|
| EF Core persistence (entity saving/loading, upsert, schema) | [`src/Elsa/Persistence/EFCore/EXTENSION_POINTS.md`](src/Elsa/Persistence/EFCore/EXTENSION_POINTS.md) |

### Activities

| Domain | Catalog |
|---|---|
| Activities flowchart (scoped execution seam + runtime composite activity contracts + gateway policy contract) | [`src/Elsa/Activities/Flowchart/EXTENSION_POINTS.md`](src/Elsa/Activities/Flowchart/EXTENSION_POINTS.md) |
| Activities runtime (activity constructors + resume target declarations) | [`src/Elsa/Activities/Runtime/EXTENSION_POINTS.md`](src/Elsa/Activities/Runtime/EXTENSION_POINTS.md) |
| Activities sequence (runtime composite activity contracts) | [`src/Elsa/Activities/Sequence/EXTENSION_POINTS.md`](src/Elsa/Activities/Sequence/EXTENSION_POINTS.md) |
| Activities control-flow — If branch (Then/Else slots, True/False outcomes; `IActivityStructureHandler` contribution) | [`src/Elsa/Activities/ControlFlow/If/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/If/EXTENSION_POINTS.md) |
| Activities control-flow — Switch branches (per-case + default slots; `IActivityStructureHandler` contribution) | [`src/Elsa/Activities/ControlFlow/Switch/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/Switch/EXTENSION_POINTS.md) |
| Activities control-flow — While loop (body slot, `Done` outcome, per-iteration `IterationId`; structure handler contribution) | [`src/Elsa/Activities/ControlFlow/While/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/While/EXTENSION_POINTS.md) |
| Activities control-flow — Do loop (body slot, `Break`/`Done` outcomes, per-iteration `IterationId`; structure handler contribution) | [`src/Elsa/Activities/ControlFlow/Do/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/Do/EXTENSION_POINTS.md) |
| Activities control-flow — For loop (counted iteration; structure handler + consumed runtime extensions) | [`src/Elsa/Activities/ControlFlow/For/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/For/EXTENSION_POINTS.md) |
| Activities control-flow — ForEach loop (collection iteration; structure handler + runtime seam dependencies) | [`src/Elsa/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md) |
| Activities control-flow — Parallel branches (fan-out/join; consumed runtime extension points + structure handler) | [`src/Elsa/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md`](src/Elsa/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md) |
| Activities design — reconciliation sources | [`src/Elsa/Activities/Design/Reconciliation/EXTENSION_POINTS.md`](src/Elsa/Activities/Design/Reconciliation/EXTENSION_POINTS.md) |
| Activities design — CLR reconciliation source contribution | [`src/Elsa/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md`](src/Elsa/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md) |
| Activities design — JSON reconciliation source contribution | [`src/Elsa/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md`](src/Elsa/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md) |
| Activities design — persistence commands + lookup | [`src/Elsa/Activities/Design/Persistence/EFCore/EXTENSION_POINTS.md`](src/Elsa/Activities/Design/Persistence/EFCore/EXTENSION_POINTS.md) |
| Activities design — Groundwork persistence commands + lookup | [`src/Elsa/Activities/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](src/Elsa/Activities/Design/Persistence/Groundwork/EXTENSION_POINTS.md) |

### Workflows

| Domain | Catalog |
|---|---|
| Workflows design — model, mutation events, commands, diff engine | [`src/Elsa/Workflows/Design/Api/EXTENSION_POINTS.md`](src/Elsa/Workflows/Design/Api/EXTENSION_POINTS.md) |
| Workflows design — draft validators | [`src/Elsa/Workflows/Design/Validations/EXTENSION_POINTS.md`](src/Elsa/Workflows/Design/Validations/EXTENSION_POINTS.md) |
| Workflows design — reconciliation sources | [`src/Elsa/Workflows/Design/Reconciliation/EXTENSION_POINTS.md`](src/Elsa/Workflows/Design/Reconciliation/EXTENSION_POINTS.md) |
| Workflows design — persistence commands + diff engine | [`src/Elsa/Workflows/Design/Persistence/EFCore/EXTENSION_POINTS.md`](src/Elsa/Workflows/Design/Persistence/EFCore/EXTENSION_POINTS.md) |
| Workflows design — Groundwork persistence commands + diff engine | [`src/Elsa/Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](src/Elsa/Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md) |
| Workflows runtime (checkpoint policy/writer, post-commit dispatcher/outbox, recovery scanner, domain retry policy, bookmark resume resolver, value binding resolver/register/validator, payload capture policy, activity execution inspection store/accumulator, execution agent provider, start dispatcher, scheduler queue/drain, runtime middleware, signal handler, completion handler) | [`src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md`](src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md) |
| Workflows runtime — HTTP endpoint behaviour | [`src/Elsa/Workflows/Runtime/Http/EXTENSION_POINTS.md`](src/Elsa/Workflows/Runtime/Http/EXTENSION_POINTS.md) |

### Legacy

| Domain | Catalog |
|---|---|
| Elsa3 activities import (JSON source for legacy activity definitions) | [`src/Elsa3/Activities/Design/Import/EXTENSION_POINTS.md`](src/Elsa3/Activities/Design/Import/EXTENSION_POINTS.md) |
| Elsa3 mapping (workflow definition import boundary) | [`src/Elsa3/Mapping/EXTENSION_POINTS.md`](src/Elsa3/Mapping/EXTENSION_POINTS.md) |

### Diagnostics

| Domain | Catalog |
|---|---|
| Structured logs (log store, live feed, sink, source provider) | [`src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md`](src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md) |
| OpenTelemetry (telemetry store, live feed, ingestor, redactor, source registry, provider, collector config) | [`src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md`](src/Elsa/Diagnostics/OpenTelemetry/EXTENSION_POINTS.md) |

---

## Constitutional basis

- §2.6.1 — the single `IEvent` concept + contribution sub-pattern; intra-domain vs cross-domain contributions; Source vs Contributor naming; sanctioned action-named suffixes (`…Validator`, `…Handler`).
- §2.22.1 — per-domain extension-points catalog (overridable contracts + implementable contributor interfaces + Events section); layer badge convention; Known implementations with intra/cross-domain tags; mandatory maintenance obligation.
- §2.22.2 — this repo-wide extension-points index (pure links, no inline entries).
- §2.24.2 — contributor interface + single aggregating handler (the sanctioned pattern catalog).
