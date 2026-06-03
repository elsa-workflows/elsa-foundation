# Extension Points — repo-wide index

The codebase-wide **index** of the sanctioned ways to extend Elsa without modifying the framework (framework §2.6.1, §2.24.2, §2.22.2). This is the map; the authoritative per-domain detail lives in each domain's own `EXTENSION_POINTS.md`.

## Two axes: override vs. extend

Every seam below is one of two kinds (framework §2.22.1):

| Axis | What you do | Mechanism |
|---|---|---|
| **Override** | *Replace* a default implementation of a `.Core` contract — "bring my own data access / my own commands". | One implementation wins: `services.Replace(...)` / register-your-own. You can override one contract and keep the rest (e.g. swap the commands, keep the built-in queries). |
| **Extend** | *Add* an implementation alongside the built-ins. | A single aggregating handler resolves **all** registered implementations and runs each. Adding one never removes another. |

## The doc layering

- **per-feature READMEs** — what THIS feature registers/provides.
- **per-domain `EXTENSION_POINTS.md`** — the authoritative catalog for THAT domain: its overridable contracts, its implementable contributor interfaces, and (as an **Events** section) the events it publishes. Folds in what used to be a separate `EVENTS.md` (framework §2.22.1, 2026-06-03).
- **this file** — the repo-wide index that points into each per-domain catalog and inlines the seams for domains that do not yet ship their own.

### Per-domain catalogs (authoritative detail)

| Domain | Catalog |
|---|---|
| EF Core persistence | [`src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md) |
| Workflows.Design model + mutation events | [`src/Elsa.Workflows.Design.Core/EXTENSION_POINTS.md`](src/Elsa.Workflows.Design.Core/EXTENSION_POINTS.md) |
| Workflow draft validation | [`src/Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md`](src/Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md) |

> Domains below **without** a linked per-domain catalog (serialization, JavaScript expressions, activities, reconciliation) are inlined here until they ship their own `EXTENSION_POINTS.md`.

## How to read the kinds

Per framework §2.6.1 contributor sub-pattern, an extension interface is one of:

| Kind | Shape | Naming |
|---|---|---|
| **Source** | *Returns* values (pull). `GetX()` / `Read()`. | `I…Source` |
| **Contributor** | *Receives* a context and acts on it (push). `Contribute(ctx)`. | `I…Contributor` |
| **PreProcessor / PostProcessor** | Receives a context and acts at a specific phase. | `I…PreProcessor` / `I…PostProcessor` |
| **Validator** | Action-named contributor: inspects and **returns** findings. | `I…Validator` |
| **entity Handler** | Action-named contributor: receives ctx + entity and **acts** at a persistence lifecycle point. | `I…Handler` |

The action-named suffixes (`…Validator`, `…Handler`) are semantically sanctioned alongside Source / Contributor / PreProcessor / PostProcessor (framework §2.6.1): the suffix names the specific action the interface performs on the received context. They are Contributor-kind (context-receiving); the single aggregating `IEventHandler<OnXxx>` still owns the event subscription.

**The universal rule:** features implement the interface and register it via DI as the interface type (or an assembly scan). They do **NOT** register their own `IEventHandler<OnXxx>`. Exactly one aggregating handler per event resolves `IEnumerable<TContributor>` (or reflects the typed contributor) and dispatches every implementation. See each row's "Consumed by".

---

## Serialization — `Elsa.Serialization.Core`

### `IJsonConverterSource`
- **Kind:** Source. **Lives in:** `Elsa.Serialization.Core`.
- **Signature:** `IEnumerable<JsonConverter> GetConverters();`
- **Returns** the `JsonConverter` instances this source contributes (does not receive a context).
- **Register:** `services.AddScoped<IJsonConverterSource, MyConverterSource>()`.
- **Consumed by:** the single `RegisterJsonConverters : IEventHandler<OnJsonPayloadConvertersInitializing>` (`Elsa.Serialization`), which injects `IEnumerable<IJsonConverterSource>` and aggregates.

---

## JavaScript expressions — `Elsa.Expressions.JavaScript.Core` / `.Rendering.Core`

### `IScriptPreProcessor`
- **Kind:** PreProcessor. **Lives in:** `Elsa.Expressions.JavaScript.Core`.
- **Signature:** `ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken);`
- **Receives** the execution contexts and acts (e.g. injects globals) before the script runs.
- **Register:** `services.AddScoped<IScriptPreProcessor, MyPreProcessor>()`.
- **Consumed by:** the single `PreProcessScript` handler (`Elsa.Expressions.JavaScript`), which injects `IEnumerable<IScriptPreProcessor>`.

### `IScriptPostProcessor`
- **Kind:** PostProcessor. **Lives in:** `Elsa.Expressions.JavaScript.Core`.
- **Signature:** `ValueTask PostProcess(IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken);`
- **Receives** the execution contexts and acts after the script runs.
- **Register:** `services.AddScoped<IScriptPostProcessor, MyPostProcessor>()`.
- **Consumed by:** the single `PostProcessScript` handler (`Elsa.Expressions.JavaScript`).

### `IJavaScriptDeclarationContributor`
- **Kind:** Contributor. **Lives in:** `Elsa.Expressions.JavaScript.Rendering.Core`.
- **Signature:** `ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken);`
- **Receives** the declarations context and pushes variable/type/function declarations onto it (`AddVariable` / `AddType` / `AddFunction`).
- **Register:** `services.AddScoped<IJavaScriptDeclarationContributor, MyContributor>()`.
- **Consumed by:** the single `BuildDeclarationsDocument` handler (`Elsa.Expressions.JavaScript.Rendering`), which injects `IEnumerable<IJavaScriptDeclarationContributor>`.

---

## Activities (runtime) — `Elsa.Activities.Runtime.Core`

### `IActivityImplementationResolverSource`
- **Kind:** Source. **Lives in:** `Elsa.Activities.Runtime.Core`.
- **Signature:** `IEnumerable<IActivityImplementationResolver> GetResolvers();`
- **Returns** the resolver(s) this source contributes.
- **Register:** `services.AddScoped<IActivityImplementationResolverSource, MySource>()`.
- **Consumed by:** the single `RegisterActivityImplementationResolvers : IEventHandler<OnActivityImplementationResolversInitializing>` (`Elsa.Activities.Runtime`).

---

## Activities (design) — `Elsa.Activities.Design.Core`

### `IImplementationDescriptorSource`
- **Kind:** Source. **Lives in:** `Elsa.Activities.Design.Core`.
- **Signature:** `IEnumerable<ImplementationDescriptorRegistration> GetRegistrations();`
- **Returns** the `(Kind, DescriptorType)` registration(s) this source contributes.
- **Register:** `services.AddScoped<IImplementationDescriptorSource, MySource>()`.
- **Consumed by:** the single `RegisterImplementationDescriptors : IEventHandler<OnImplementationDescriptorsInitializing>` (`Elsa.Activities.Runtime`).

---

## Reconciliation — `Elsa.Activities.Design.Reconciliation` / `Elsa.Workflows.Design.Reconciliation`

### `IActivityReconciliationSource`
- **Kind:** Source (carries `SourceId` + `SourceKind`). **Lives in:** `Elsa.Activities.Design.Reconciliation` (Contracts).
- **Signature:** `ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);` + `string SourceId { get; }` + `string SourceKind { get; }`.
- **Returns** the desired activity-version set for its source. Extend via inheritance of the abstract base feature, not via `.Json`-style sub-packages.
- **Register:** `services.AddScoped<IActivityReconciliationSource, MySource>()`.
- **Consumed by:** `ActivityVersionReconciler` (`Elsa.Activities.Design.Reconciliation`), which reads every registered source on each reconciliation pass.

### `IWorkflowReconciliationSource`
- **Kind:** Source (carries `SourceId` + `SourceKind`). **Lives in:** `Elsa.Workflows.Design.Reconciliation` (Contracts).
- **Signature:** `ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken);` + `string SourceId { get; }` + `string SourceKind { get; }`.
- **Register / Consumed by:** mirror of the activity source — `WorkflowsVersionReconciler` (`Elsa.Workflows.Design.Reconciliation`).

---

## Workflow draft validation — `Elsa.Workflows.Design.Validations.Core`

→ **Authoritative catalog:** [`src/Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md`](src/Elsa.Workflows.Design.Validations.Core/EXTENSION_POINTS.md).

- **`IDraftValidator`** — Validator (action-named contributor). `ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken ct)`. Register `services.AddScoped<IDraftValidator, MyValidator>()`. Consumed by the single `ExecuteValidations : IEventHandler<OnDraftValidating>`. *Extend axis.*

---

## Workflows.Design model + mutation/lifecycle commands — `Elsa.Workflows.Design.Core`

→ **Authoritative catalog:** [`src/Elsa.Workflows.Design.Core/EXTENSION_POINTS.md`](src/Elsa.Workflows.Design.Core/EXTENSION_POINTS.md).

- **Override:** `IWorkflowDefinitionLookup`, `IWorkflowDesignContextFactory`, the mutation/lifecycle commands (`IUpdateDraftCommand` + the 5 lifecycle commands, in `Elsa.Workflows.Design.Persistence.Core`), and `IDraftStateDiffEngine` (in `Elsa.Workflows.Design.Persistence.EFCore`). The canonical "swap the commands, keep the queries" example.
- **Events:** the 20 FR-018 mutation events + 2 lifecycle events (`OnDraftCreated` / `OnDraftDiscarded`), all Background.

---

## EF Core persistence — `Elsa.Persistence.EFCore`

→ **Authoritative catalog:** [`src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md).

- **Override:** `IQueries<TEntity>` (default `EFCoreQueries<,>`), `IUpsertCommandGenerator`, `IElsaDbContextSchema`.
- **Extend (entity Handlers):**
  - **`IEntitySavingHandler<TDbContext, TEntity>`** — `ValueTask Handle(TDbContext, TEntity, CancellationToken)`. Register `services.AddEntitySavingHandler<…>()` / `AddEntitySavingHandlersFrom(assembly)`. Consumed by the single `ApplyEntitySavingHandlers : IEventHandler<OnEntitySaving>` (registered once by `EFCorePersistenceShellFeatureBase` via `TryAddEnumerable`).
  - **`IEntityLoadingHandler<TDbContext, TEntity>`** — `ValueTask Handle(TDbContext, TEntity, CancellationToken)`. Register `services.AddEntityLoadingHandler<…>()` / `AddEntityLoadingHandlersFrom(assembly)`. Consumed by the single `ApplyEntityLoadingHandlers : IEventHandler<OnEntityLoading>`.
- **Out-of-band hooks (NOT event-dispatched):**
  - **`IGlobalEntitySavingHandler`** — `ValueTask Handle(DbContext, EntityEntry, CancellationToken)`. Runs for **every** modified entity directly from `ElsaDbContextBase.ApplyGlobalSavingHandlers`.
  - **`IEntityModelCreatingHandler`** — `void Handle(ElsaDbContextBase, ModelBuilder, IMutableEntityType)`. Runs during `OnModelCreating`, dispatched by `ElsaDbContextBase.ApplyEntityModelCreatingHandlers`.

---

## Constitutional basis

- §2.6.1 — the single `IEvent` concept + contribution sub-pattern; Source vs Contributor naming; sanctioned action-named suffixes (`…Validator`, `…Handler`).
- §2.22.1 — per-domain extension-points catalog (overridable contracts + implementable contributor interfaces + Events section).
- §2.22.2 — this repo-wide extension-points index.
- §2.24.2 — contributor interface + single aggregating handler (the sanctioned pattern catalog).
