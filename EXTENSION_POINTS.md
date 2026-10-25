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
| Events (substrate — `IEvent`, `IEventHandler<T>`, strategies) | [`Essentials/src/Events/Elsa.Events/EXTENSION_POINTS.md`](Essentials/src/Events/Elsa.Events/EXTENSION_POINTS.md) |
| Mediator (command / request pipelines) | [`Essentials/src/Mediator/Elsa.Mediator/EXTENSION_POINTS.md`](Essentials/src/Mediator/Elsa.Mediator/EXTENSION_POINTS.md) |
| Pipelines (middleware — Core-only, no feature project) | [`Essentials/src/Pipelines/Core/EXTENSION_POINTS.md`](Essentials/src/Pipelines/Core/EXTENSION_POINTS.md) |
| Tasks (startup / recurring / background tasks) | [`Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md`](Essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md) |
| Caching (cache manager + change-token signaling) | [`Essentials/src/Caching/Memory/EXTENSION_POINTS.md`](Essentials/src/Caching/Memory/EXTENSION_POINTS.md) |
| Serialization (JSON converter sources) | [`Essentials/src/Serialization/SystemText/EXTENSION_POINTS.md`](Essentials/src/Serialization/SystemText/EXTENSION_POINTS.md) |
| Locking (distributed lock provider) | [`Essentials/src/Locking/FileSystem/EXTENSION_POINTS.md`](Essentials/src/Locking/FileSystem/EXTENSION_POINTS.md) |
| Cluster (membership provider, member report sources and the shared schema dormancy check) | [`Essentials/src/Cluster/Core/EXTENSION_POINTS.md`](Essentials/src/Cluster/Core/EXTENSION_POINTS.md) |
| Modularity API (shell feature-management surface; feature-catalog contributors and activation guards) | [`Essentials/src/Modularity/Api/EXTENSION_POINTS.md`](Essentials/src/Modularity/Api/EXTENSION_POINTS.md) |

### Expressions

| Domain | Catalog |
|---|---|
| Expressions (evaluator + descriptor providers) | [`Essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md`](Essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md) |
| Expressions API (descriptor projections and capability declaration) | [`Essentials/src/Expressions/Api/EXTENSION_POINTS.md`](Essentials/src/Expressions/Api/EXTENSION_POINTS.md) |
| JavaScript expressions (pre/post-processors) | [`Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](Essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md) |
| JavaScript rendering (declaration contributors) | [`Essentials/src/Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](Essentials/src/Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md) |
| Liquid expressions (rendering lifecycle) | [`Essentials/src/Expressions/Liquid/EXTENSION_POINTS.md`](Essentials/src/Expressions/Liquid/EXTENSION_POINTS.md) |

### HTTP

| Domain | Catalog |
|---|---|
| HTTP (downloadable content handlers) | [`Essentials/src/Http/Elsa.Http/EXTENSION_POINTS.md`](Essentials/src/Http/Elsa.Http/EXTENSION_POINTS.md) |

### API

| Domain | Catalog |
|---|---|
| API Capabilities (typed operational Sources and deterministic catalog replacement) | [`Essentials/src/Api/Capabilities/EXTENSION_POINTS.md`](Essentials/src/Api/Capabilities/EXTENSION_POINTS.md) |
| API endpoint layer (failure pipeline, endpoint composition contracts, unload-safety enforcement) | [`Essentials/src/Api/EXTENSION_POINTS.md`](Essentials/src/Api/EXTENSION_POINTS.md) |

### Agent and Identity

| Domain | Catalog |
|---|---|
| Agent core (sessions, policy, context, proposals, provider facade, audit) | [`Extensions/Agent/src/Core/EXTENSION_POINTS.md`](Extensions/Agent/src/Core/EXTENSION_POINTS.md) |
| Foundation Identity (authentication providers, IAM contracts, permissions, ownership capabilities) | [`Essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md`](Essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md) |
| Foundation Identity ASP.NET Core Identity EF adapter (opt-in framework stores, authentication, session invalidation, and seeding over the shared EF authority) | [`Essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Foundation Identity EF persistence (opt-in complete IAM authority and provider-configuration backend; OpenIddict boundary) | [`Essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/README.md`](Essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/README.md) |
| Foundation Data Protection (the host's key ring: fixed application name, opt-in EF key store, certificate at rest, startup warnings) | [`Essentials/src/Foundation/DataProtection/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Foundation/DataProtection/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflow Agent (workflow explain/troubleshoot/change-proposal contribution) | [`Extensions/Agent/src/Workflows/EXTENSION_POINTS.md`](Extensions/Agent/src/Workflows/EXTENSION_POINTS.md) |

### Integrations

| Domain | Catalog |
|---|---|
| Integrations SDK (connector activities, named HTTP client auth scope) | [`Extensions/Integrations/src/Sdk/EXTENSION_POINTS.md`](Extensions/Integrations/src/Sdk/EXTENSION_POINTS.md) |

### Secrets

| Domain | Catalog |
|---|---|
| Secrets core (contracts — manager, resolver, repository, store, type and scope providers, audit sink) | [`Essentials/src/Secrets/Core/EXTENSION_POINTS.md`](Essentials/src/Secrets/Core/EXTENSION_POINTS.md) |
| Secrets (default services + shell feature registration) | [`Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md`](Essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md) |
| Secrets Entity Framework Core persistence (the Secrets persistence family under ADR 0073) | [`Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |

### Connections

| Domain | Catalog |
|---|---|
| Connections core (provider-neutral contracts — repository, manager, resolver, credential sources, auth schemes, `SensitiveValue`) | [`Essentials/src/Connections/Core/EXTENSION_POINTS.md`](Essentials/src/Connections/Core/EXTENSION_POINTS.md) |
| Connections (default services, http type, built-in schemes, named credentials) | [`Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md`](Essentials/src/Connections/Elsa.Connections/EXTENSION_POINTS.md) |
| Connections API (CRUD, picker, OAuth callback) | [`Essentials/src/Connections/Api/EXTENSION_POINTS.md`](Essentials/src/Connections/Api/EXTENSION_POINTS.md) |
| Connections Design (publish gate) | [`Essentials/src/Connections/Design/EXTENSION_POINTS.md`](Essentials/src/Connections/Design/EXTENSION_POINTS.md) |
| Connections Workflows (http applicator + secret mask) | [`Essentials/src/Connections/Workflows/EXTENSION_POINTS.md`](Essentials/src/Connections/Workflows/EXTENSION_POINTS.md) |

### Persistence

| Domain | Catalog |
|---|---|
| Provider-neutral persistence access and scope selection | [`Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) |
| Entity Framework persistence policy (history table, provider guard, migrate vs validate; shared policy under ADR 0073) | [`Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md`](Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md) |

### Activities

| Domain | Catalog |
|---|---|
| Activities flowchart (scoped execution seam + runtime composite activity contracts + gateway policy contract) | [`Essentials/src/Activities/Flowchart/EXTENSION_POINTS.md`](Essentials/src/Activities/Flowchart/EXTENSION_POINTS.md) |
| Activities runtime (activity constructors + resume target declarations) | [`Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/EXTENSION_POINTS.md`](Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/EXTENSION_POINTS.md) |
| DispatchWorkflow runtime (staged checkpoint dispatch and child-start contribution) | [`Essentials/src/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md`](Essentials/src/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md) |
| DispatchWorkflow design (options and executable-pin contribution) | [`Essentials/src/Activities/DispatchWorkflow/Design/EXTENSION_POINTS.md`](Essentials/src/Activities/DispatchWorkflow/Design/EXTENSION_POINTS.md) |
| Activities HTTP (synchronous-response sink seam; HttpEndpoint/WriteHttpResponse + inbound middleware) | [`Essentials/src/Activities/Http/EXTENSION_POINTS.md`](Essentials/src/Activities/Http/EXTENSION_POINTS.md) |
| Activities sequence (runtime composite activity contracts) | [`Essentials/src/Activities/Sequence/EXTENSION_POINTS.md`](Essentials/src/Activities/Sequence/EXTENSION_POINTS.md) |
| Activities compensation — CompensationScope (step and compensation slots, `Done`/`Compensated` outcomes, scheduling metadata; structure handler and Draft validator contributions) | [`Essentials/src/Activities/Compensation/EXTENSION_POINTS.md`](Essentials/src/Activities/Compensation/EXTENSION_POINTS.md) |
| Activities control-flow — If branch (Then/Else slots, True/False outcomes; `IActivityStructureHandler` contribution) | [`Essentials/src/Activities/ControlFlow/If/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/If/EXTENSION_POINTS.md) |
| Activities control-flow — Switch branches (per-case + default slots; `IActivityStructureHandler` contribution) | [`Essentials/src/Activities/ControlFlow/Switch/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/Switch/EXTENSION_POINTS.md) |
| Activities control-flow — While loop (body slot, `Done` outcome, per-iteration `IterationId`; structure handler contribution) | [`Essentials/src/Activities/ControlFlow/While/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/While/EXTENSION_POINTS.md) |
| Activities control-flow — Do loop (body slot, `Break`/`Done` outcomes, per-iteration `IterationId`; structure handler contribution) | [`Essentials/src/Activities/ControlFlow/Do/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/Do/EXTENSION_POINTS.md) |
| Activities control-flow — For loop (counted iteration; structure handler + consumed runtime extensions) | [`Essentials/src/Activities/ControlFlow/For/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/For/EXTENSION_POINTS.md) |
| Activities control-flow — ForEach loop (collection iteration; structure handler + runtime seam dependencies) | [`Essentials/src/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md) |
| Activities control-flow — Parallel branches (fan-out/join; consumed runtime extension points + structure handler) | [`Essentials/src/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md`](Essentials/src/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md) |
| Activities design — reconciliation sources | [`Essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](Essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md) |
| Activities design — CLR reconciliation source contribution | [`Essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md`](Essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md) |
| Activities design — JSON reconciliation source contribution | [`Essentials/src/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md`](Essentials/src/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md) |
| Activities design — persistence replacement (Entity Framework Core) | [`Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Activities design API — catalog and availability services | [`Essentials/src/Activities/Design/Api/EXTENSION_POINTS.md`](Essentials/src/Activities/Design/Api/EXTENSION_POINTS.md) |
| Activities graph design — authoring provider, compiler, dependency discovery, and reference rewriting | [`Essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md`](Essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md) |
| Activities graph runtime — compiled graph activity consumer and transient activation | [`Essentials/src/Activities/Graph/Runtime/EXTENSION_POINTS.md`](Essentials/src/Activities/Graph/Runtime/EXTENSION_POINTS.md) |
| Activities BPMN (scoped execution seam, element behavior contributors) | [`Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/EXTENSION_POINTS.md`](Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/EXTENSION_POINTS.md) |
| Activities BPMN interchange (document importer and exporter replacement) | [`Essentials/src/Activities/Bpmn/Interchange/EXTENSION_POINTS.md`](Essentials/src/Activities/Bpmn/Interchange/EXTENSION_POINTS.md) |

### Workflows

| Domain | Catalog |
|---|---|
| Workflows design — model, activity-input option providers, mutation events, commands, diff engine | [`Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`](Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md) |
| Workflows design — draft validators | [`Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md) |
| Workflows design — reconciliation sources | [`Essentials/src/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md`](Essentials/src/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md) |
| Workflows design — persistence commands and stores (opt-in EF Core) | [`Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflows publishing engine — compiler, slot authority/policy/preflight, publication activation, compilation fan-in, and activity-template registries | [`Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md`](Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md) |
| Workflows publishing API — HTTP endpoints, transport authorization, and activity-draft publish/test-run | [`Essentials/src/Workflows/Publishing/Api/EXTENSION_POINTS.md`](Essentials/src/Workflows/Publishing/Api/EXTENSION_POINTS.md) |
| Workflows publishing core — authority and projection contracts | [`Essentials/src/Workflows/Publishing/Core/EXTENSION_POINTS.md`](Essentials/src/Workflows/Publishing/Core/EXTENSION_POINTS.md) |
| Workflows runtime (canonical domain catalog: Core contracts, composition root, engine implementations, trigger publication, recurring schedules) | [`Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) |
| Workflows runtime API — executable inspection and diagnostics services | [`Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md) |
| Workflows runtime distributed EF Core placement persistence | [`Essentials/src/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflows runtime — HTTP endpoint behaviour | [`Essentials/src/Workflows/Runtime/Http/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Http/EXTENSION_POINTS.md) |
| Workflows runtime — durable resumption (sweep service, recurring pump, feature gating) | [`Essentials/src/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](Essentials/src/Workflows/Runtime/Resumption/EXTENSION_POINTS.md) |

### Rules

| Domain | Catalog |
|---|---|
| Rules compilation, invocation preparation and deployment identity | [`Extensions/Rules/src/Core/EXTENSION_POINTS.md`](Extensions/Rules/src/Core/EXTENSION_POINTS.md) |

### ETL

| Domain | Catalog |
|---|---|
| Safe scoped binding discovery | [`Extensions/Etl/src/Core/EXTENSION_POINTS.md`](Extensions/Etl/src/Core/EXTENSION_POINTS.md) |
| Draft and immutable publication metadata | [`Extensions/Etl/src/Design/Elsa.Etl.Design/EXTENSION_POINTS.md`](Extensions/Etl/src/Design/Elsa.Etl.Design/EXTENSION_POINTS.md) |
| Shared ETL API transport | [`Extensions/Etl/src/Api/EXTENSION_POINTS.md`](Extensions/Etl/src/Api/EXTENSION_POINTS.md) |
| Runtime inspection and cancellation API | [`Extensions/Etl/src/Runtime/Api/EXTENSION_POINTS.md`](Extensions/Etl/src/Runtime/Api/EXTENSION_POINTS.md) |
| Authoring API operations and host composition | [`Extensions/Etl/src/Design/Api/EXTENSION_POINTS.md`](Extensions/Etl/src/Design/Api/EXTENSION_POINTS.md) |
| Studio authoring, inspection and published selection | [`Extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md`](Extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md) |

### Legacy

| Domain | Catalog |
|---|---|
| Elsa3 activities import (JSON source for legacy activity definitions) | [`Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/EXTENSION_POINTS.md`](Extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/EXTENSION_POINTS.md) |
| Elsa3 activities import publishing (adapter to the activity publisher) | [`Extensions/Elsa3/src/Activities/Design/Import/Publishing/EXTENSION_POINTS.md`](Extensions/Elsa3/src/Activities/Design/Import/Publishing/EXTENSION_POINTS.md) |
| Elsa3 mapping (workflow definition import boundary) | [`Extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md`](Extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md) |

### Diagnostics

| Domain | Catalog |
|---|---|
| Diagnostics persistence lifecycle (bounded drain, durable acknowledgement, readiness, observability) | [`Essentials/src/Diagnostics/Persistence/EXTENSION_POINTS.md`](Essentials/src/Diagnostics/Persistence/EXTENSION_POINTS.md) |
| Structured logs (log store, live feed, sink, source provider) | [`Essentials/src/Diagnostics/StructuredLogs/Elsa.Diagnostics.StructuredLogs/EXTENSION_POINTS.md`](Essentials/src/Diagnostics/StructuredLogs/Elsa.Diagnostics.StructuredLogs/EXTENSION_POINTS.md) |
| OpenTelemetry (telemetry store, live feed, ingestor, redactor, trusted OTLP authentication/context, source registry, provider, collector config) | [`Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/EXTENSION_POINTS.md`](Essentials/src/Diagnostics/OpenTelemetry/Elsa.Diagnostics.OpenTelemetry/EXTENSION_POINTS.md) |

---

## Constitutional basis

- §2.6.1 — the single `IEvent` concept + contribution sub-pattern; intra-domain vs cross-domain contributions; Source vs Contributor naming; sanctioned action-named suffixes (`…Validator`, `…Handler`).
- §2.22.1 — per-domain extension-points catalog (overridable contracts + implementable contributor interfaces + Events section); layer badge convention; Known implementations with intra/cross-domain tags; mandatory maintenance obligation.
- §2.22.2 — this repo-wide extension-points index (pure links, no inline entries).
- §2.24.2 — contributor interface + single aggregating handler (the sanctioned pattern catalog).
