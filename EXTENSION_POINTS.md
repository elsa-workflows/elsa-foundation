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
| Events (substrate — `IEvent`, `IEventHandler<T>`, strategies) | [`server/essentials/src/Events/Elsa.Events/EXTENSION_POINTS.md`](server/essentials/src/Events/Elsa.Events/EXTENSION_POINTS.md) |
| Mediator (command / request pipelines) | [`server/essentials/src/Mediator/Elsa.Mediator/EXTENSION_POINTS.md`](server/essentials/src/Mediator/Elsa.Mediator/EXTENSION_POINTS.md) |
| Pipelines (middleware — Core-only, no feature project) | [`server/essentials/src/Pipelines/Core/EXTENSION_POINTS.md`](server/essentials/src/Pipelines/Core/EXTENSION_POINTS.md) |
| Tasks (startup / recurring / background tasks) | [`server/essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md`](server/essentials/src/Tasks/Elsa.Tasks/EXTENSION_POINTS.md) |
| Caching (cache manager + change-token signaling) | [`server/essentials/src/Caching/Memory/EXTENSION_POINTS.md`](server/essentials/src/Caching/Memory/EXTENSION_POINTS.md) |
| Serialization (JSON converter sources) | [`server/essentials/src/Serialization/SystemText/EXTENSION_POINTS.md`](server/essentials/src/Serialization/SystemText/EXTENSION_POINTS.md) |
| Locking (distributed lock provider) | [`server/essentials/src/Locking/FileSystem/EXTENSION_POINTS.md`](server/essentials/src/Locking/FileSystem/EXTENSION_POINTS.md) |
| Cluster (membership provider, member report sources and the shared schema dormancy check) | [`server/essentials/src/Cluster/Core/EXTENSION_POINTS.md`](server/essentials/src/Cluster/Core/EXTENSION_POINTS.md) |
| Modularity API (shell feature-management surface; feature-catalog contributors and activation guards) | [`server/essentials/src/Modularity/Api/EXTENSION_POINTS.md`](server/essentials/src/Modularity/Api/EXTENSION_POINTS.md) |

### Expressions

| Domain | Catalog |
|---|---|
| Expressions (evaluator + descriptor providers) | [`server/essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md`](server/essentials/src/Expressions/Elsa.Expressions/EXTENSION_POINTS.md) |
| Expressions API (descriptor projections and capability declaration) | [`server/essentials/src/Expressions/Api/EXTENSION_POINTS.md`](server/essentials/src/Expressions/Api/EXTENSION_POINTS.md) |
| JavaScript expressions (pre/post-processors) | [`server/essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](server/essentials/src/Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md) |
| JavaScript rendering (declaration contributors) | [`server/essentials/src/Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](server/essentials/src/Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md) |
| Liquid expressions (rendering lifecycle) | [`server/essentials/src/Expressions/Liquid/EXTENSION_POINTS.md`](server/essentials/src/Expressions/Liquid/EXTENSION_POINTS.md) |

### HTTP

| Domain | Catalog |
|---|---|
| HTTP (downloadable content handlers) | [`server/essentials/src/Http/Elsa.Http/EXTENSION_POINTS.md`](server/essentials/src/Http/Elsa.Http/EXTENSION_POINTS.md) |

### API

| Domain | Catalog |
|---|---|
| API Capabilities (typed operational Sources and deterministic catalog replacement) | [`server/essentials/src/Api/Capabilities/Elsa.Api.Capabilities/EXTENSION_POINTS.md`](server/essentials/src/Api/Capabilities/Elsa.Api.Capabilities/EXTENSION_POINTS.md) |
| API endpoint layer (failure pipeline, endpoint composition contracts, unload-safety enforcement) | [`server/essentials/src/Api/EXTENSION_POINTS.md`](server/essentials/src/Api/EXTENSION_POINTS.md) |

### Agent and Identity

| Domain | Catalog |
|---|---|
| Weaver foundation (sessions, policy, context, proposals, provider facade, audit, session record store and durable recovery) | [`extensions/Weaver/src/Elsa.Weaver/EXTENSION_POINTS.md`](extensions/Weaver/src/Elsa.Weaver/EXTENSION_POINTS.md) |
| Weaver EF session record store and durable audit (opt-in Weaver recovery) | [`extensions/Weaver/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](extensions/Weaver/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Agent model providers | [`extensions/Agent/src/Core/EXTENSION_POINTS.md`](extensions/Agent/src/Core/EXTENSION_POINTS.md) |
| Connection-backed agent model contributors | [`extensions/Agent/src/Connections/EXTENSION_POINTS.md`](extensions/Agent/src/Connections/EXTENSION_POINTS.md) |
| Agent EF turn journal | [`extensions/Agent/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](extensions/Agent/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Anthropic agent connections | [`extensions/Agent/src/Anthropic/EXTENSION_POINTS.md`](extensions/Agent/src/Anthropic/EXTENSION_POINTS.md) |
| Azure OpenAI agent connections | [`extensions/Agent/src/AzureOpenAI/EXTENSION_POINTS.md`](extensions/Agent/src/AzureOpenAI/EXTENSION_POINTS.md) |
| OpenAI agent connections | [`extensions/Agent/src/OpenAI/EXTENSION_POINTS.md`](extensions/Agent/src/OpenAI/EXTENSION_POINTS.md) |
| Agent authoring and immutable compilation | [`extensions/Agent/src/Design/EXTENSION_POINTS.md`](extensions/Agent/src/Design/EXTENSION_POINTS.md) |
| Agent native execution | [`extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`](extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) |
| Human review of agent output | [`extensions/Agent/src/Review/Elsa.Agent.Review/EXTENSION_POINTS.md`](extensions/Agent/src/Review/Elsa.Agent.Review/EXTENSION_POINTS.md) |
| Agent human review API | [`extensions/Agent/src/Review/Api/EXTENSION_POINTS.md`](extensions/Agent/src/Review/Api/EXTENSION_POINTS.md) |
| Agent authoring API | [`extensions/Agent/src/Api/EXTENSION_POINTS.md`](extensions/Agent/src/Api/EXTENSION_POINTS.md) |
| Agent inspection API | [`extensions/Agent/src/Inspection/Api/EXTENSION_POINTS.md`](extensions/Agent/src/Inspection/Api/EXTENSION_POINTS.md) |
| Foundation Identity external authentication | [`extensions/Identity/src/Core/EXTENSION_POINTS.md`](extensions/Identity/src/Core/EXTENSION_POINTS.md) |
| Foundation Identity (authentication providers, IAM contracts, permissions, ownership capabilities) | [`server/essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md`](server/essentials/src/Foundation/Identity/Core/EXTENSION_POINTS.md) |
| Foundation Identity ASP.NET Core Identity EF adapter (opt-in framework stores, authentication, session invalidation, and seeding over the shared EF authority) | [`server/essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Foundation Identity EF persistence (opt-in complete IAM authority and provider-configuration backend; OpenIddict boundary) | [`server/essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/README.md`](server/essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/README.md) |
| Foundation Data Protection (the host's key ring: fixed application name, opt-in EF key store, certificate at rest, startup warnings) | [`server/essentials/src/Foundation/DataProtection/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Foundation/DataProtection/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Weaver workflow module (bounded graph explanation and reviewed designer position changes; broader authoring/troubleshooting tracked in #3442) | [`extensions/Weaver/src/Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md`](extensions/Weaver/src/Workflows/Elsa.Weaver.Workflows/EXTENSION_POINTS.md) |
| Weaver workflow runtime module (read-only Executable and Run context and tools, Runtime-side; spec 1193 slices F and G) | [`extensions/Weaver/src/Workflows/Runtime/EXTENSION_POINTS.md`](extensions/Weaver/src/Workflows/Runtime/EXTENSION_POINTS.md) |

### Integrations

| Domain | Catalog |
|---|---|
| Integrations SDK (connector activities, named HTTP client auth scope) | [`extensions/Integrations/src/Sdk/Elsa.Integrations.Sdk/EXTENSION_POINTS.md`](extensions/Integrations/src/Sdk/Elsa.Integrations.Sdk/EXTENSION_POINTS.md) |

### Secrets

| Domain | Catalog |
|---|---|
| Secrets core (contracts — manager, resolver, repository, store, type and scope providers, audit sink) | [`server/essentials/src/Secrets/Core/EXTENSION_POINTS.md`](server/essentials/src/Secrets/Core/EXTENSION_POINTS.md) |
| Secrets (default services + shell feature registration) | [`server/essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md`](server/essentials/src/Secrets/Elsa.Secrets/EXTENSION_POINTS.md) |
| Secrets Entity Framework Core persistence (the Secrets persistence family under ADR 0073) | [`server/essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Secrets/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |

### Connections

| Domain | Catalog |
|---|---|
| Connections core (provider-neutral contracts — repository, manager, resolver, credential sources, auth schemes, `SensitiveValue`) | [`extensions/Connections/src/Core/EXTENSION_POINTS.md`](extensions/Connections/src/Core/EXTENSION_POINTS.md) |
| Connections (default services, http type, built-in schemes, named credentials) | [`extensions/Connections/src/Elsa.Connections/EXTENSION_POINTS.md`](extensions/Connections/src/Elsa.Connections/EXTENSION_POINTS.md) |
| Connections EF persistence (scoped metadata, version checks, secret references and consumer-owned contexts) | [`extensions/Connections/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](extensions/Connections/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Connections API (CRUD, picker, OAuth callback) | [`extensions/Connections/src/Api/EXTENSION_POINTS.md`](extensions/Connections/src/Api/EXTENSION_POINTS.md) |
| Connections Design (publish gate) | [`extensions/Connections/src/Design/EXTENSION_POINTS.md`](extensions/Connections/src/Design/EXTENSION_POINTS.md) |
| Connections Workflows (http applicator + secret mask) | [`extensions/Connections/src/Workflows/EXTENSION_POINTS.md`](extensions/Connections/src/Workflows/EXTENSION_POINTS.md) |

### Persistence

| Domain | Catalog |
|---|---|
| Provider-neutral persistence access and scope selection | [`server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) |
| Entity Framework persistence policy (history table, provider guard, migrate vs validate; shared policy under ADR 0073) | [`server/essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md`](server/essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md) |

### Activities

| Domain | Catalog |
|---|---|
| Activities flowchart (scoped execution seam + runtime composite activity contracts + gateway policy contract) | [`server/essentials/src/Activities/Flowchart/EXTENSION_POINTS.md`](server/essentials/src/Activities/Flowchart/EXTENSION_POINTS.md) |
| Activities runtime (activity constructors + resume target declarations) | [`server/essentials/src/Activities/Runtime/Elsa.Activities.Runtime/EXTENSION_POINTS.md`](server/essentials/src/Activities/Runtime/Elsa.Activities.Runtime/EXTENSION_POINTS.md) |
| DispatchWorkflow runtime (staged checkpoint dispatch and child-start contribution) | [`server/essentials/src/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md`](server/essentials/src/Activities/DispatchWorkflow/Runtime/EXTENSION_POINTS.md) |
| DispatchWorkflow design (options and executable-pin contribution) | [`server/essentials/src/Activities/DispatchWorkflow/Design/EXTENSION_POINTS.md`](server/essentials/src/Activities/DispatchWorkflow/Design/EXTENSION_POINTS.md) |
| Activities HTTP (synchronous-response sink seam; HttpEndpoint/WriteHttpResponse + inbound middleware) | [`server/essentials/src/Activities/Http/EXTENSION_POINTS.md`](server/essentials/src/Activities/Http/EXTENSION_POINTS.md) |
| Activities sequence (runtime composite activity contracts) | [`server/essentials/src/Activities/Sequence/EXTENSION_POINTS.md`](server/essentials/src/Activities/Sequence/EXTENSION_POINTS.md) |
| Activities compensation — CompensationScope (step and compensation slots, `Done`/`Compensated` outcomes, scheduling metadata; structure handler and Draft validator contributions) | [`server/essentials/src/Activities/Compensation/EXTENSION_POINTS.md`](server/essentials/src/Activities/Compensation/EXTENSION_POINTS.md) |
| Activities control-flow — If branch (Then/Else slots, True/False outcomes; `IActivityStructureHandler` contribution) | [`server/essentials/src/Activities/ControlFlow/If/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/If/EXTENSION_POINTS.md) |
| Activities control-flow — Switch branches (per-case + default slots; `IActivityStructureHandler` contribution) | [`server/essentials/src/Activities/ControlFlow/Switch/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/Switch/EXTENSION_POINTS.md) |
| Activities control-flow — While loop (body slot, `Done` outcome, per-iteration `IterationId`; structure handler contribution) | [`server/essentials/src/Activities/ControlFlow/While/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/While/EXTENSION_POINTS.md) |
| Activities control-flow — Do loop (body slot, `Break`/`Done` outcomes, per-iteration `IterationId`; structure handler contribution) | [`server/essentials/src/Activities/ControlFlow/Do/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/Do/EXTENSION_POINTS.md) |
| Activities control-flow — For loop (counted iteration; structure handler + consumed runtime extensions) | [`server/essentials/src/Activities/ControlFlow/For/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/For/EXTENSION_POINTS.md) |
| Activities control-flow — ForEach loop (collection iteration; structure handler + runtime seam dependencies) | [`server/essentials/src/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/ForEach/EXTENSION_POINTS.md) |
| Activities control-flow — Parallel branches (fan-out/join; consumed runtime extension points + structure handler) | [`server/essentials/src/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md`](server/essentials/src/Activities/ControlFlow/Parallel/EXTENSION_POINTS.md) |
| Activities design — reconciliation sources | [`server/essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](server/essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md) |
| Activities design — CLR reconciliation source contribution | [`server/essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md`](server/essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md) |
| Activities design — JSON reconciliation source contribution | [`server/essentials/src/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md`](server/essentials/src/Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md) |
| Activities design — persistence replacement (Entity Framework Core) | [`server/essentials/src/Activities/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Activities/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Activities design API — catalog, availability and static public provider diagnostics | [`server/essentials/src/Activities/Design/Api/EXTENSION_POINTS.md`](server/essentials/src/Activities/Design/Api/EXTENSION_POINTS.md) |
| Activities graph design — authoring provider, compiler, dependency discovery, and reference rewriting | [`server/essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md`](server/essentials/src/Activities/Graph/Design/EXTENSION_POINTS.md) |
| Activities graph runtime — compiled graph activity consumer and transient activation | [`server/essentials/src/Activities/Graph/Runtime/EXTENSION_POINTS.md`](server/essentials/src/Activities/Graph/Runtime/EXTENSION_POINTS.md) |
| Activities graph release inspection — compiler-free captured provider, plan and overlay verification | [`server/essentials/src/Activities/Graph/Releases/EXTENSION_POINTS.md`](server/essentials/src/Activities/Graph/Releases/EXTENSION_POINTS.md) |
| Primitive release inspection — explicit WriteLine CLR owner profile | [`server/essentials/src/Activities/Primitives/Releases/EXTENSION_POINTS.md`](server/essentials/src/Activities/Primitives/Releases/EXTENSION_POINTS.md) |
| Activities BPMN (released-semantics host adapter, direct model hosting and native RunBpmn) | [`extensions/Bpmn/src/Elsa.Activities.Bpmn/EXTENSION_POINTS.md`](extensions/Bpmn/src/Elsa.Activities.Bpmn/EXTENSION_POINTS.md) |
| Activities BPMN hosting contracts (scoped publication/execution/inspection replacement) | [`extensions/Bpmn/src/Core/EXTENSION_POINTS.md`](extensions/Bpmn/src/Core/EXTENSION_POINTS.md) |
| Activities BPMN design (authored structure lowering and publication pinning) | [`extensions/Bpmn/src/Design/EXTENSION_POINTS.md`](extensions/Bpmn/src/Design/EXTENSION_POINTS.md) |
| Activities BPMN hosting API (authenticated direct-model commands, cancellation, incident retry and inspection) | [`extensions/Bpmn/src/Api/EXTENSION_POINTS.md`](extensions/Bpmn/src/Api/EXTENSION_POINTS.md) |
| Activities BPMN interchange (document importer and exporter replacement) | [`extensions/Bpmn/src/Interchange/EXTENSION_POINTS.md`](extensions/Bpmn/src/Interchange/EXTENSION_POINTS.md) |

### Workflows

| Domain | Catalog |
|---|---|
| Workflows design — model, activity-input option providers, mutation events, commands, diff engine, safe folder deletion | [`server/essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md) |
| Workflows design — draft validators | [`server/essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md) |
| Workflows design — reconciliation sources | [`server/essentials/src/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Design/Reconciliation/Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md) |
| Workflows design — persistence commands and stores (opt-in EF Core) | [`server/essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflows publishing engine — compiler, slot authority/policy/preflight, publication activation, compilation fan-in, and activity-template registries | [`server/essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md) |
| Workflows publishing API — HTTP endpoints, transport authorization, and activity-draft publish/test-run | [`server/essentials/src/Workflows/Publishing/Api/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Api/EXTENSION_POINTS.md) |
| Workflows publishing core — authority and projection contracts | [`server/essentials/src/Workflows/Publishing/Core/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Core/EXTENSION_POINTS.md) |
| Workflow release transport and verification — compiler-free codec and exact owner strategies | [`server/essentials/src/Workflows/Publishing/Releases/Elsa.Workflows.Publishing.Releases/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Elsa.Workflows.Publishing.Releases/EXTENSION_POINTS.md) |
| Workflow release core — lightweight transport and owner verification contracts | [`server/essentials/src/Workflows/Publishing/Releases/Core/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Core/EXTENSION_POINTS.md) |
| Workflow release Runtime contracts — retained owner codecs and captured occurrence views | [`server/essentials/src/Workflows/Publishing/Releases/Runtime/Core/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Runtime/Core/EXTENSION_POINTS.md) |
| Workflow release Runtime profile — exact native/template/model pins and mandatory owner inspection | [`server/essentials/src/Workflows/Publishing/Releases/Runtime/Elsa.Workflows.Publishing.Releases.Runtime/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Runtime/Elsa.Workflows.Publishing.Releases.Runtime/EXTENSION_POINTS.md) |
| Workflow release Runtime source closure — explicit source policy and bounded dependency acquisition | [`server/essentials/src/Workflows/Publishing/Releases/Runtime/Source/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Runtime/Source/EXTENSION_POINTS.md) |
| Workflow release source capture — retained acquisition and verified durable capture | [`server/essentials/src/Workflows/Publishing/Releases/Capture/Elsa.Workflows.Publishing.Releases.Capture/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Publishing/Releases/Capture/Elsa.Workflows.Publishing.Releases.Capture/EXTENSION_POINTS.md) |
| Workflow release archive contracts — scoped frozen transport captures | [`Archive Core`](server/essentials/src/Workflows/Publishing/Releases/Persistence/Core/EXTENSION_POINTS.md) |
| Workflow release archive persistence — consumer-context SQLite storage | [`Archive EF Core`](server/essentials/src/Workflows/Publishing/Releases/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| User Tasks (durable human work, journal/retention/eligibility/form integration and recovery cadence) | [`Runtime domain User Tasks`](server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#user-tasks) |
| Workflows runtime (canonical domain catalog: Core contracts, composition root, engine implementations, trigger publication, recurring schedules) | [`server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md) |
| Runtime diagnostic history (bounded reads, delivery/retention ports and immutable owner inputs; spec 1211) | [`Runtime history provider ports`](server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#runtime-history-provider-ports), [`owner-input contracts`](server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#runtime-history-owner-input-contracts), [`EF history and timer ownership`](server/essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflows runtime API — executable inspection and diagnostics services | [`server/essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Api/EXTENSION_POINTS.md) |
| Workflows runtime distributed EF Core placement persistence | [`server/essentials/src/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) |
| Workflows runtime — HTTP endpoint behaviour | [`server/essentials/src/Workflows/Runtime/Http/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Http/EXTENSION_POINTS.md) |
| Workflows runtime — kernel host (publications on the clean-slate kernel: coordinator, trigger extractor, schedule actors, shell feature) | [`server/essentials/src/Workflows/Runtime/KernelHost/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/KernelHost/EXTENSION_POINTS.md) |
| Workflows runtime — kernel API (the runtime API's run list from the clean-slate kernel on a kernel shell) | [`server/essentials/src/Workflows/Runtime/KernelApi/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/KernelApi/EXTENSION_POINTS.md) |
| Workflows runtime — durable resumption (sweep service, recurring pump, feature gating) | [`server/essentials/src/Workflows/Runtime/Resumption/EXTENSION_POINTS.md`](server/essentials/src/Workflows/Runtime/Resumption/EXTENSION_POINTS.md) |

### Rules

| Domain | Catalog |
|---|---|
| Rules compilation, invocation preparation and deployment identity | [`extensions/Rules/src/Core/EXTENSION_POINTS.md`](extensions/Rules/src/Core/EXTENSION_POINTS.md) |

### ETL

| Domain | Catalog |
|---|---|
| Safe scoped binding discovery | [`extensions/Etl/src/Core/EXTENSION_POINTS.md`](extensions/Etl/src/Core/EXTENSION_POINTS.md) |
| Draft and immutable publication metadata | [`extensions/Etl/src/Design/Elsa.Etl.Design/EXTENSION_POINTS.md`](extensions/Etl/src/Design/Elsa.Etl.Design/EXTENSION_POINTS.md) |
| Shared ETL API transport | [`extensions/Etl/src/Api/EXTENSION_POINTS.md`](extensions/Etl/src/Api/EXTENSION_POINTS.md) |
| Runtime inspection and cancellation API | [`extensions/Etl/src/Runtime/Api/EXTENSION_POINTS.md`](extensions/Etl/src/Runtime/Api/EXTENSION_POINTS.md) |
| Authoring API operations and host composition | [`extensions/Etl/src/Design/Api/EXTENSION_POINTS.md`](extensions/Etl/src/Design/Api/EXTENSION_POINTS.md) |
| Studio authoring, inspection and published selection | [`extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md`](extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md) |

### Decisions

| Domain | Catalog |
|---|---|
| Decision compilation and evaluation | [Decisions evaluator](extensions/Decisions/src/Elsa.Decisions/EXTENSION_POINTS.md) |
| Decision persistence ports | [Decisions persistence](extensions/Decisions/src/Persistence/Core/EXTENSION_POINTS.md) |
| Decision authoring and completed tests | [Decisions design](extensions/Decisions/src/Design/EXTENSION_POINTS.md) |
| Decision API and permission contributions | [Decisions API](extensions/Decisions/src/Api/EXTENSION_POINTS.md) |
| Workflow publication | [Decisions workflow design](extensions/Decisions/src/Workflows/Design/EXTENSION_POINTS.md) |
| Workflow execution authority | [Decisions workflow runtime](extensions/Decisions/src/Workflows/Runtime/EXTENSION_POINTS.md) |
| Workflow disclosure | [Decisions workflow API](extensions/Decisions/src/Workflows/Api/EXTENSION_POINTS.md) |
| Studio authoring and version selection | [Decisions Studio](extensions/Decisions/studio/Elsa.Studio.Decisions/EXTENSION_POINTS.md) |

### Legacy

| Domain | Catalog |
|---|---|
| Elsa3 activities import (JSON source for legacy activity definitions) | [`extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/EXTENSION_POINTS.md`](extensions/Elsa3/src/Activities/Design/Import/Elsa3.Activities.Design.Import/EXTENSION_POINTS.md) |
| Elsa3 activities import publishing (adapter to the activity publisher) | [`extensions/Elsa3/src/Activities/Design/Import/Publishing/EXTENSION_POINTS.md`](extensions/Elsa3/src/Activities/Design/Import/Publishing/EXTENSION_POINTS.md) |
| Elsa3 JSON reconciliation (folder import at engine start) | [`extensions/Elsa3/src/Activities/Design/Import/Reconciliation/EXTENSION_POINTS.md`](extensions/Elsa3/src/Activities/Design/Import/Reconciliation/EXTENSION_POINTS.md) |
| Elsa3 mapping (workflow definition import boundary) | [`extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md`](extensions/Elsa3/src/Mapping/EXTENSION_POINTS.md) |

### Diagnostics

| Domain | Catalog |
|---|---|
| Diagnostics persistence lifecycle (bounded drain, durable acknowledgement, readiness, observability) | [`extensions/Diagnostics/src/Persistence/EXTENSION_POINTS.md`](extensions/Diagnostics/src/Persistence/EXTENSION_POINTS.md) |
| Structured logs (log store, live feed, sink, source provider) | [`extensions/Diagnostics.StructuredLogs/src/Elsa.Diagnostics.StructuredLogs/EXTENSION_POINTS.md`](extensions/Diagnostics.StructuredLogs/src/Elsa.Diagnostics.StructuredLogs/EXTENSION_POINTS.md) |
| OpenTelemetry (telemetry store, live feed, ingestor, redactor, trusted OTLP authentication/context, source registry, provider, collector config) | [`extensions/Diagnostics.OpenTelemetry/src/Elsa.Diagnostics.OpenTelemetry/EXTENSION_POINTS.md`](extensions/Diagnostics.OpenTelemetry/src/Elsa.Diagnostics.OpenTelemetry/EXTENSION_POINTS.md) |

---

## Constitutional basis

- §2.6.1 — the single `IEvent` concept + contribution sub-pattern; intra-domain vs cross-domain contributions; Source vs Contributor naming; sanctioned action-named suffixes (`…Validator`, `…Handler`).
- §2.22.1 — per-domain extension-points catalog (overridable contracts + implementable contributor interfaces + Events section); layer badge convention; Known implementations with intra/cross-domain tags; mandatory maintenance obligation.
- §2.22.2 — this repo-wide extension-points index (pure links, no inline entries).
- §2.24.2 — contributor interface + single aggregating handler (the sanctioned pattern catalog).


## Studio

- [Dashboard native API](extensions/Dashboard/src/Api/EXTENSION_POINTS.md)

- [Dashboard definition persistence](extensions/Dashboard/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md)

- [Dashboard native management](extensions/Dashboard/src/Management/EXTENSION_POINTS.md)

- [Dashboard query, resource and authoring contracts](extensions/Dashboard/src/Core/EXTENSION_POINTS.md)

- [External sign-in Studio module and standalone management client](extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/README.md)

- [Studio server contracts and frontend lifecycle catalog](studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md)
- [Language-owned expression scope preservation](studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md#expression-scope-preservation-sdk--elsastudiohosting)
- [API capability contributor catalog](server/essentials/src/Api/Capabilities/Elsa.Api.Capabilities/EXTENSION_POINTS.md)

- [Workflow inspection authorization](server/essentials/src/Workflows/Runtime/Inspection/Core/EXTENSION_POINTS.md)

- [Workflow Dashboard source](extensions/Dashboard/src/Workflows/Queries/EXTENSION_POINTS.md)

- [Studio Dashboard routes and Weaver contributions](extensions/Dashboard/studio/Elsa.Studio.Dashboard/EXTENSION_POINTS.md)

- [Weaver Dashboard authoring](extensions/Weaver/src/Dashboard/EXTENSION_POINTS.md)

- [Copilot SDK adapter boundary](extensions/Weaver/src/GitHubCopilot/EXTENSION_POINTS.md)

- [Independent Acme Processing dataset module](extensions/Examples/src/Acme.Processing/Module/EXTENSION_POINTS.md)
