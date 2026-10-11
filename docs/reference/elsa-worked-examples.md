# Elsa Worked Examples

These examples instantiate constitutional rules using concrete `Elsa.*` names. They are reference material, not the enforceable gate surface. The enforceable rules remain in `.specify/memory/constitution.md` and `.specify/memory/constitution-framework.md`.

## elsa-core baseline case study

The framework was distilled from a structural analysis of the **elsa-core** codebase (`github.com/elsa-workflows/elsa-core`). elsa-core is preserved here as a worked case study - a real-world example of the structural problems the framework is designed to prevent ([anti-patterns](architecture-rationale.md#anti-patterns)).

elsa-core exhibited every [anti-pattern](architecture-rationale.md#anti-patterns) at once:

1. **God packages.** `Elsa.Workflows.Core` accumulated contracts and implementations across runtime, design, persistence, and serialization concerns.
2. **Framework leakage into domain code.** ASP.NET Core types, expression engines, and HTTP-specific abstractions surfaced inside packages that should have been transport-agnostic.
3. **Forced heavy dependencies.** Distributed locking (Medallion), expression engines (Jint, Fluid), EF Core providers, message-broker SDKs, and HTTP clients were all transitively reachable from the consumable contract layer. Every consumer pulled the whole tree whether they needed it or not.
4. **Infrastructure locked into the lowest layer.** Persistence base contexts, specific lock implementations, and HTTP framework choices baked into the contracts.
5. **Inverted dependency direction.** Domain code referencing infrastructure; consumer code reaching into provider internals.
6. **Silent DI resolution.** `Elsa.Common` was the vector through which `IronCompress`, `DistributedLock.Core`, and configuration types bled into every consumer; multiple registrations against the same contract overwrote each other without diagnostic.
7. **No naming convention.** `Elsa.Features.*`, `Elsa.Modules.*`, `Elsa.Core.Common`, `Elsa.Core.Serialization.Contracts` - layer-marker buckets that communicated nothing the domain hierarchy did not already say.

The Elsa refactor replaces those failure modes with the rules in framework §2 and the Elsa-specific decomposition in Elsa constitution §E2.

Refactor work in this constitution's scope is governed by framework §2.21.1, the golden rule of refactoring. Existing tests on the implementations being refactored must continue to succeed across the reorganization; the subject under test and objective are preserved even when test setup, dependencies, or location change. Removing a test requires explicit recorded approval from at least one architect.

## Cross-Core composition

Instantiates framework §2.1.

There is no shared `Elsa.Workflows.Core` parent package. Design and Runtime are independent sub-domain Cores, each standing on its own, consistent with the Elsa §E2.2 bounded-context split. Cross-`.Core` composition still happens through unrelated top-level Cores that both sub-domains may consume.

Top-level domain Cores in play:

- Runtime persistence access and scope contracts now live in `Elsa.Workflows.Runtime.Core`; design
  operation identity, failure contracts, and named store ports live in
  `Elsa.Workflows.Design.Persistence.Core`.
- `Elsa.Serialization.Core` - serialization contracts.

Workflows sub-domain Cores:

- `Elsa.Workflows.Design.Core` - design-time contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, and related types.
- `Elsa.Workflows.Runtime.Core` - runtime contracts. Specifics are deferred to the workflow execution seam follow-up. It does not reference `Elsa.Workflows.Design.Core`.

The observable cross-`.Core` reference today is in Design's sub-sub-domain Cores:

- `Elsa.Workflows.Design.Persistence.Core` - references `Elsa.Workflows.Design.Core` and owns the
  provider-neutral design persistence ports.

Implementations:

- `Elsa.Workflows.Design.Persistence.EntityFrameworkCore` - EF Core implementation of the design-persistence sub-sub-domain (the only shipped design-persistence provider; the former EF Core implementation was removed by spec 093).

Impl-to-impl carve-out: implementations across unrelated sub-domains never reference each other. Implementations within the same provider family may reference each other — for example a provider-specific package extending a shared base implementation within the same persistence family. The former `Elsa.Persistence.EFCore.Sqlite` / `Elsa.Persistence.EFCore` pair illustrated this rule but is no longer shipped.

## Adapter pattern: Elsa.Locking

Instantiates framework §2.7 and §2.20.

`Elsa.Locking` follows provider module decomposition:

- `Elsa.Locking.Core` defines `IDistributedLockProvider` with zero external dependencies.
- `Elsa.Locking.FileSystem` registers a `DistributedLockProviderAdaptor` that wraps `Medallion.Threading.FileSystem`. The Medallion package is not visible to any consumer of `Elsa.Locking.Core`.

Replacing file-system locks with Redis means shipping `Elsa.Locking.Redis` as a separate module.

When Elsa.Locking only had a FileSystem provider, the umbrella `Elsa.Locking` was retired and everything consolidated into `Elsa.Locking.FileSystem` (validated 2026-05-10). When a second provider arrives and real shared adapter logic emerges, a provider-family package may be extracted under the framework §2.1 impl-to-impl carve-out.

`DistributedLock 2.8.1`, the meta-package fronting eleven `DistributedLock.<Provider>` sub-packages, was replaced with a direct `DistributedLock.FileSystem` reference. The MongoDB sub-package's transitive dependencies had known CVEs and were unused by Elsa.Locking. This is the framework §2.20 Rule 2 application.

## Event contribution with sync access: JsonConverter registry

Instantiates framework §2.6.1.

The `JsonPayloadSerializer` runs `System.Text.Json` `JsonConverter` callbacks synchronously and cannot await async dispatch at converter resolution time. The contribution still flows through the event pipeline; access is sync because population happened earlier via the Registry + StartUp Task sub-pattern.

The event follows the contributor-interface + single-handler sub-pattern: features implement a return-style `IJsonConverterSource` and one `RegisterJsonConverters` handler aggregates. The event is published Sequential so the StartUp task can read the contributed converters back.

`Elsa.Serialization.Core` defines:

- `JsonPayloadConverterRegistry`.
- `JsonPayloadConvertersInitializing`, an `IEvent` exposing a directly-accessible `ICollection<JsonConverter> Converters`.
- `IJsonConverterSource`, the return-style contributor interface.

```csharp
public sealed class JsonPayloadConvertersInitializing : IEvent
{
    public ICollection<JsonConverter> Converters { get; } = [];
}

public interface IJsonConverterSource
{
    IEnumerable<JsonConverter> GetConverters();
}
```

`Elsa.Serialization.<Provider>` registers the StartUp task and the single `RegisterJsonConverters` handler:

```csharp
public sealed class RegisterJsonConverters(IEnumerable<IJsonConverterSource> sources)
    : IEventHandler<JsonPayloadConvertersInitializing>
{
    public Task Handle(JsonPayloadConvertersInitializing e, CancellationToken ct)
    {
        foreach (var source in sources)
            foreach (var converter in source.GetConverters())
                e.Converters.Add(converter);
        return Task.CompletedTask;
    }
}

var @event = new JsonPayloadConvertersInitializing();
await eventPublisher.Publish(@event);
registry.RegisterAll(@event.Converters);
```

`Elsa.Expressions` and other contributing features extend serialization by implementing `IJsonConverterSource` and registering it via DI. They do not register their own event handler, and neither feature references the other.

At runtime, `JsonPayloadSerializer` sync code accesses the populated `JsonPayloadConverterRegistry` directly.

The removed `src/essentials/Persistence/EFCore/EXTENSION_POINTS.md` previously documented more examples of this contributor-interface + single-aggregating-handler shape; the path is retained here only as historical provenance.

Legacy state: the historical implementation used `IPayloadSerializerConverterProvider`. Migration to the pattern above is tracked in the Unit A follow-up.

## Historical feature inheritance example

The types in this section were removed with the earlier persistence implementation. The example is
retained only to explain the framework pattern; it does not describe a current extension point or API.

Instantiates framework §2.5.

The earlier persistence stack inherited across three levels:

```text
PersistenceShellFeatureBase<TDbContext>
    -> EFCoreWorkflowsPersistenceFeatureBase
            -> SqliteWorkflowDefinitionPersistenceShellFeature
```

Each level added to or specialised the level above it through compile-time inheritance, never through peer references. The leaf was the activated feature; intermediate levels were abstract.

## Dual-integration smell: Elsa.Http and Elsa.Expressions.JavaScript

Instantiates framework §2.14.

The former Elsa HTTP module directly brought in JavaScript-engine dependencies because some HTTP functionality exposed JavaScript functions that belonged to the HTTP domain.

That violates framework §2.14: a consumption-shape that depends on two external systems is a boundary smell. The JS-functions-in-HTTP code became its own consumption-shape module:

- `Elsa.Http` - HTTP integration.
- `Elsa.Expressions.JavaScript` - JavaScript expression integration.
- `Elsa.Http.JavaScript` - consumption-shape that exposes HTTP-specific functions to JavaScript.

Consumers who want HTTP without JavaScript reference only `Elsa.Http`.

Status: resolved in the 2026-05-19 refactor session by extracting `Elsa.Http.JavaScript` under the framework §2.2 secondary-domain naming rule.

## Adapter pattern: IJavaScriptExecutionContext over Jint

Instantiates framework §2.7.

`IJavaScriptExecutionContext` is defined in `Elsa.Expressions.JavaScript.Core` with zero Jint reference. Consumers of the JavaScript expression domain depend only on `IJavaScriptExecutionContext`.

`Elsa.Expressions.JavaScript.Jint` holds a `JintJavaScriptExecutionContext` adapter that wraps Jint's engine, options, and runtime types. Jint stays entirely inside the implementation package.

Replacing Jint with a different JavaScript engine means shipping a new feature module that supplies a different `IJavaScriptExecutionContext` adapter.

## Design-time vs runtime contract split: JavaScript declarations and functions

Instantiates framework §2.6.4.

The JavaScript expression domain has two distinct consumers of contributed function data:

- Design-time consumer: rendering and intellisense need function shape.
- Runtime consumer: evaluator needs actual function bindings.

A unified provider would force every contributing feature to satisfy both consumers. The split is:

| Phase | Event | Contributor interface (`.Core`) | Kind | Single handler | Where impls live |
|---|---|---|---|---|---|
| Design-time | `DeclarationsDocumentGenerating` | `IJavaScriptDeclarationContributor` | Contributor | `BuildDeclarationsDocument` | Design-time contributors |
| Runtime before | `OnEvaluatingScript` | `IScriptPreProcessor` | PreProcessor | `PreProcessScript` | Runtime contributors |
| Runtime after | `OnScriptEvaluated` | `IScriptPostProcessor` | PostProcessor | `PostProcessScript` | Runtime post-processors |

Both phases may carry a shared `.Core` data record describing a contributed function shape. Each event binds to its own consumer and all are published Sequential.

The declarations cluster uses the Contributor kind: contributors receive `IJavaScriptDeclarationsContributionContext` and act on it.

The script-evaluation cluster uses PreProcessor/PostProcessor because `OnEvaluatingScript` and `OnScriptEvaluated` are a before/after pair. Both act on the live `IJavaScriptExecutionContext`.

A single feature may implement several of these interfaces, such as `Elsa.Http.JavaScript` implementing both design-time declarations and runtime bindings.

## Elsa.Http.JavaScript naming walkthrough

Instantiates framework §2.2.

The decision was to name the cross-cutting module `Elsa.Http.JavaScript`, not `Elsa.JavaScript.Http`.

The cross-cutting module contributes function declarations and function bindings for HTTP-domain concepts. Those are HTTP models. The JavaScript side ships only consumer machinery.

The model-owning domain wins the prefix: HTTP owns the models and JavaScript is the consumer, so the name is `Elsa.Http.JavaScript`.

The reverse form would force `Elsa.JavaScript` to grow one sub-branch per model-owning domain it exposes to JavaScript. That is the junk-drawer anti-pattern framework §2.2 prevents.

## Sync contributor pattern: IEntityModelCreatingHandler

Historical example: the earlier first-party EF implementation and these interfaces were removed. This
section explains the original rationale, not currently available APIs; see the current
[EF Core Persistence scope](../program-goals/ef-core-persistence.md).

Instantiates framework §2.6.5.

EF Core's `OnModelCreating` lifecycle hook needs to invoke contributing handlers synchronously at the moment EF Core builds the model. Async event dispatch cannot apply because `OnModelCreating` is intrinsically sync.

`Elsa.Persistence.EFCore` declares `IEntityModelCreatingHandler` with `void Handle(ElsaDbContextBase dbContext, ModelBuilder modelBuilder, IMutableEntityType entityType)`.

Features that need to customise the EF model register `IEntityModelCreatingHandler` implementations via DI. `ElsaDbContextBase.ApplyEntityModelCreatingHandlers` resolves and invokes them synchronously per registered entity type.

Why framework §2.6.5 applies:

1. EF Core's `OnModelCreating(ModelBuilder)` is sync.
2. Each handler mutates the shared `ModelBuilder`; it does not return data the caller collects.
3. Registry + StartUp Task does not apply because the `ModelBuilder` exists only at the EF Core lifecycle moment.

This is not a license to use sync contributor interfaces broadly. Reviewers must challenge every §2.6.5 invocation.

## Three-segment secondary-domain naming with phase split

Instantiates framework §2.2 and Elsa §E2.2.

Status: provisional pending the 2026-06-01 architecture review meeting, agenda Item 6.

`Elsa.Http.Activities.<Phase>` extends the two-segment secondary-domain naming pattern to a three-segment case where the consumer domain has a Design/Runtime phase split.

HTTP contributes activities. Activities have both a design-time variant and a runtime variant. Per the Elsa §E2.2 hard rule, Design and Runtime cannot live in the same implementation module.

```text
Elsa.Activities.Design.Core
Elsa.Activities.Runtime.Core

Elsa.Http.Activities.Design
  references Elsa.Activities.Design.Core
  references Elsa.Workflows.Design.Validations.Core
  references Elsa.Http

Elsa.Http.Activities.Runtime
  references Elsa.Activities.Runtime.Core
  references Elsa.Http
```

The same pattern generalises to every model-owning domain contributing activities, such as Email or Slack.

Key reasoning:

- The model-owning domain wins the prefix.
- The reverse form is a junk drawer.
- Three segments express the consumer-domain plus phase pair.
- The Elsa §E2.2 hard rule is preserved at implementation level.
- No empty `Elsa.Http.Activities` umbrella is created unless real shared code emerges.
- The design-time contract surface is consumed by the design-time module and the runtime contract surface by the runtime module.

Activity-specific validators co-locate with their activity's design-time module. The validator is an `IDraftValidator` registered by the `Elsa.Http.Activities.Design` feature; it is not its own event handler.

This is not a license to over-elaborate every cross-domain contribution. The three-segment composition activates only when the consumer domain also has an internal phase axis that must appear at the package boundary.

## Design and Runtime packages

Supports Elsa §E2.2. Moved verbatim from Elsa constitution v5.0.0.

**The seam between Design and Runtime is deferred.** The mechanism by which a workflow flows from Design into Runtime for execution — the carrier type, the activity-contract surfacing, the role of publication, the implications for an `ActivityRegistry` — is **not pinned by this constitution**. It is scheduled for the workflow execution seam follow-up and resurfaces when the Runtime refactor begins; current repo-local findings live in [unfinished work](../reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

### §E2.2.1 Design sub-domain — the designed contract

Design owns the *designed contract* of a workflow: input/output definitions, activity tree, expression bindings, plus the persistence layer that stores them.

Packages:

- `Elsa.Workflows.Design.Core` — contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, etc.
- `Elsa.Workflows.Design.Persistence.Core` — design-time persistence contracts.
- `Elsa.Workflows.Design.Persistence.EntityFrameworkCore` — EF Core implementation. One project carries all four
  supported providers (SQLite, SQL Server, PostgreSQL, MySQL) with per-provider migrations, rather than a
  project per provider.

### §E2.2.2 Runtime sub-domain — the runtime representation

Runtime owns the *runtime representation* of workflow execution and its own dedicated persistence layer, separate from Design.

Packages (the specific runtime contracts and entities remain governed by the workflow execution seam follow-up recorded in [unfinished work](../reports/unfinished-work.md)):

- `Elsa.Workflows.Runtime.Core` — runtime contracts and the execution engine.
- `Elsa.Workflows.Runtime.Api`, `Elsa.Workflows.Runtime.Http`, `Elsa.Workflows.Runtime.JavaScript` — runtime surface modules.

Runtime does **not** reference `Elsa.Workflows.Design.Core`, except for the single tracked allow-list exception recorded in §E2.2.

## Elsa.Primitives decomposition

Supports Elsa §E2.3 and framework §2.3. Moved verbatim from Elsa constitution v5.0.0.

**framework §2.3 — Elsa specialization.** `Elsa.Primitives` is the narrow
domainless-primitives package that replaced the historical `Elsa.Common` leakage
surface identified by §E1.

**Current charter:**

- `Elsa.Primitives` carries only truly domainless building blocks: `Result<T>`, `Page<T>`, base entity abstractions, guard helpers.
- Zero external NuGet dependencies. Without exception.
- Three-repetition rule applies.

**Anticipated further decomposition.** As code reviews land, additional concerns are split out per framework §2.3:

- `Elsa.Serialization` — already present.
- `Elsa.Events.Core` / `Elsa.Events.Strategies` / `Elsa.Events` — the single in-process event concept over the shared `Elsa.Pipelines.Core` engine.
- `Elsa.Mediator.Core` / `Elsa.Mediator` — command + request dispatch only. It shares `Elsa.Pipelines.Core` with `Elsa.Events.Core`; the two do not reference each other.

**`Elsa.Foundation.Core` is held back.** Elsa does not eagerly create a framework-foundation `.Core` package. If a coherent set of framework-foundation contracts emerges that does not fit in existing packages, the package can be introduced at that point. 

## Runtime contract

Supports Elsa §E2.6. Moved verbatim from Elsa constitution v5.0.0.

Elsa imposes two coupled invariants on its runtime contract. Together they make the Runtime sub-domain self-sufficient and predictable: given a published runnable artifact, the runtime always runs it; given that artifact, the runtime needs nothing else.

### §E2.6.1 Executable-always-runs

If an artifact is published as a runnable representation of a workflow, the runtime MUST be able to load and execute it. **No condition internal to the runtime system** — missing activity types, missing module installation, in-memory registry drift, version misconfiguration of runtime infrastructure — may break this contract.

**Whether** an artifact is allowed to run in a given context — per tenant, per environment, per role, per workflow-business state — is a **domain/business gate**, implemented in domain code. The runtime's ability to load and run is a **storage/system contract** that is not subject to those gates.

The separation:

- Domain gates may deny execution; they may not destroy executability.
- System failures to execute (missing types, broken loaders, infrastructure errors) are bugs, not features. They violate the contract.

The runtime artifact format carries enough information to be executed independently of any non-runtime concern. The specific artifact name and shape are settled in the entity-design pass (follow-up `2026-05-08_entity_design.md`).

### §E2.6.2 Artifact-only runtime

The Runtime sub-domain depends on **only** two things:

1. The **runnable artifact** (the entity carrying the structured runtime-oriented representation produced by Build/Compile).
2. The **configured runtime features** that interpret that artifact's format.

Source artefacts (the design-time workflow definition the artifact was built from), authoring history, draft revisions, designer layout metadata, and visualisation projections live in the Design sub-domain and adjacent application-layer projections. They are reachable from the runtime artifact via foreign keys, **but the runtime does not require them to execute**.

Visualisation of an executed instance happens at the application layer, traversing the FK chain from the executed-instance entity → runnable artifact → source-design entities. The runtime sub-domain is not aware of, and does not load, the source side.

**Hard rule.** A runtime that needs to load design-side data to execute is a §E2.2 hard-rule violation. The seam between Design and Runtime is the runnable artifact; nothing else crosses it at execution time.

**See also §E2.9** — `WorkflowExecutable` is named in the architectural triplet `WorkflowDefinitionState` ↔ read models/projections ↔ `WorkflowExecutable`. State is the source; `WorkflowExecutable` is the derived runnable form the Runtime sub-domain consumes per this section's artifact-only contract.

## Elsa 3 import

Supports Elsa §E2.7. Moved verbatim from Elsa constitution v5.0.0.

Elsa 4's compatibility with Elsa 3 is bounded to **import**. Dedicated adapter modules — today `Elsa3.Activities.Design.Import`, supported by `Elsa3.Models` and `Elsa3.Mapping`, with siblings added as needed for workflows, instances, or other Elsa 3 artefacts — map Elsa 3 workflow definitions, activity descriptors, and persistence schemas into the Elsa 4 entity model. Once imported, Elsa 4 runs them natively through its own runtime.

**In scope:**

- One-way one-time mapping: read Elsa 3 source, produce Elsa 4 entities, persist.
- Adapter modules named `Elsa3.<Domain>.Import` per the Elsa-3-side concern they map. Shared support that every adapter uses is not an adapter and does not take the suffix: `Elsa3.Models` holds the Elsa 3 source shapes and `Elsa3.Mapping` holds the converters.

**Out of scope:**

- **Dual-run support.** Elsa 3 and Elsa 4 do not run side-by-side from the same process. A migrating consumer imports, then switches.
- **Ongoing viewmodel mapping** for Elsa-3-shaped endpoints in `elsa-studio`. The Elsa 4 API surface is the API; elsa-studio adapts to it, not the other way around.
- **Round-trip translation** back to Elsa 3 entity shapes after import. Imports are terminal.

The compatibility surface is **"one-way, one-time"** by design. Mapping details are tracked outside the constitution as migration/entity-design work.

## Activity catalog

Supports Elsa §E2.8. Moved verbatim from Elsa constitution v5.0.0: the full section, including the activity-versioning and Model X reconciliation detail, both still draft or provisional.

**Rule.** If an activity is visible in the design-time picker, it has a persisted catalog entry. The picker / design-time API surface MUST query the catalog store; it MUST NOT enumerate live providers, scan loaded assemblies, or otherwise produce picker entries that have no corresponding `ActivityDefinition` row.

**Cross-references:**

- Framework §2.6.4 (design-time vs runtime contract split): the picker reads design-time contracts; runtime construction happens elsewhere.
- Sipke item 7 (2026-05-26): "the catalog is the source-of-truth for picker visibility" — adopted verbatim.

**In scope of this rule (must follow):**

- Every entry the picker can return is a `IActivityDefinition` row, with provenance fields populated (`SourceKind`, `SourceId`, `ProvisionedAt`, `ProvisionedBy`).
- Activities contributed from a CLR module, a workflow definition, a JSON file, a script source, etc. all reach the picker through the catalog (the reconciler-with-source-modules pattern; Unit B implementation).
- Non-CLR activities (Workflow descriptors, script descriptors) are first-class catalog citizens — the descriptor's `Kind` discriminator on each version row is the runtime resolver lookup key (Unit B §E2.6.1-style domain-failure path on unknown kinds).

**Out of scope (deferred to a separate policy layer):**

- **Context-aware visibility filtering** — tenant scoping, role-based access, feature flags, licensing gates, instance-level overrides. These are visibility refinements over the catalog; they reduce the catalog's output for a given context. They do NOT generate picker entries themselves.

**Removed surface:**

- `IsBrowsable` on `ActivityDefinition` is **not** the visibility mechanism. It does not exist. Visibility = catalog presence. The "should this row appear in the picker?" question has no per-row toggle; it is structurally derived from catalog membership.

**Activity versioning *(Unit 3 2026-06-04; draft pending ratification)*.** The activity version is an author-controlled **string semantic version (SemVer 2.0.0)**, not an engine-assigned integer. The author owns it: it is sourced from the declaring assembly's version and may be overridden per-activity by a `[Version("…")]` attribute. A CLR assembly-scanning `IActivityReconciliationSource` (in `Elsa.Activities.Design.Reconciliation.Clr`) reads the attribute (falling back to the assembly version) and supplies it as the version when the reconciler calls the source — the same DI-source pattern Unit B established (framework §2.6.1). The scanner reads no UI metadata (no display name, description, category from CLR); the only author-intent attribute it honours beyond `[Version]` is `[Required]` on an input (→ `InputDefinition.IsRequired`). The `[Version]` attribute and the version-resolution contract live in the zero-dep `Elsa.Activities.*.Core` so an author can annotate without a heavy dependency.

**Reconciliation policy — Model X *(Unit C 2026-05-28; pending 2026-06-01 architecture review)*.** The activity catalog is reconciled from trusted sources at creation time only. There is **no operational sibling entity**, no `LastSeenAt` heartbeat, no `IsStale` drift flag, no `RemovedAt` source-disappearance tracking. The immutable content hash for a version lives directly on `IActivityDefinitionVersion.ReconcilliationHash` and is the basis of the duplicate-detection path:

- Lookup by `(DefinitionId, Version)`, **build-metadata-insensitive**: the match is on the normalised SemVer sort key (`SemVer.ToSortKey` — a zero-padded comparable form that excludes build metadata), so `1.0.0` and `1.0.0+build` resolve as the same logical version. If absent → create with immutable provenance.
- If present and hash differs → throw `ActivityVersionHashMismatchException` (the source is broken — same identity, different content).
- If present and hash matches → skip or throw per the reconciliation source's duplicate-handling configuration.

"Latest version" / ordering queries sort by the same SemVer sort key descending (a release sorts above its prereleases); there is no integer ordering. Source disappearance is intentionally not tracked at the entity layer; versions are never deleted. Context-aware visibility (tenant / role / feature-flag) is a separate policy layer that filters the catalog for a given context; it is not a reconciliation concern. This codification is **provisional** pending architecture-review ratification.

This section codifies the rule for the activity catalog. The same shape generalises to other catalogs as Elsa accrues them (workflow catalog, script catalog, expression-evaluator catalog); each will get its own catalog-as-source-of-truth section as that catalog matures.

## Workflow definition state

Supports Elsa §E2.9, provisional pending architecture-review ratification. Moved verbatim from Elsa constitution v5.0.0: the full section and its unit history.

`WorkflowDefinitionState` is persisted as the `StateSource` shadow JSON on `WorkflowDefinitionVersion` (immutable) and `WorkflowDefinitionDraft` (mutable) inside the `Elsa.Workflows.Design` sub-domain. It is **the canonical authored document of a workflow definition** — the structured shape an author produces and the system promotes through Draft → Version. Pinning its scope explicitly prevents the god-object failure mode flagged in Sipke's 2026-05-26 entity-design review (item 2): as Units D–G crystallize, `WorkflowDefinitionState` is the natural dumping ground for any workflow-related concern unless its boundary is constitutional.

### §E2.9.1 In scope of `WorkflowDefinitionState`

Members of State carry **authored content** — the structured representation of what the author drew, declared, and configured:

- Variables (the workflow's variable declarations).
- The workflow root activity: one authored `RootActivity`. The root can be any activity kind, including a primitive activity, `Sequence`, `Flowchart`, `StateMachine`, or another composite activity. Child activities, flowchart edges, state transitions, start nodes, and similar composition details belong to the owning activity's authored contract; they are not workflow-level members.
- Workflow-level input/output declarations (`Inputs`, `Outputs`).
- Workflow-level authored options (`WorkflowActivityOptions`, `StrategyOptions`).

This root-activity rule supersedes the earlier provisional `Activities` + `ActivityConnections` workflow-level graph shape. That earlier shape accidentally made every workflow definition a flowchart and is corrected by the Runtime Execution Seam root-activity work unit.

`ActivityNode` itself is not a universal composition carrier. It MUST NOT expose a generic child collection, generic connection list, generic start-child member, or generic executable composition mirror. Activity-specific child ownership is expressed through the activity's own contract: for example `Sequence` owns ordered child activity slots, `If` owns `Then` / `Else`, `ForEach` owns `Body`, `Composite` owns `Root`, and `Flowchart` owns its activities, connections, start selection, and join rules. Core may provide opaque named child slot records so infrastructure can traverse persisted activity trees, but core MUST NOT define framework-wide slot-name constants, metadata keys, port endpoint records, connection records, or executable edge records for activity-specific concepts such as `Body`, `Then`, `Else`, `Root`, or flowchart start/edge selection. Design validation, publishing, and runtime lookup tables may discover children through opaque slot records or activity-specific adapters, but they must not make every activity look like a flowchart-shaped container.

### §E2.9.2 Out of scope of `WorkflowDefinitionState`

Members that are NOT authored content live elsewhere. Categories explicitly excluded:

- **Instance / runtime / operational state.** Workflow instances, execution log, current activity-execution state, runtime variable bindings, scheduled activations. Owned by the Runtime sub-domain per §E2.2 + §E2.6.
- **Executable / build metadata.** Compiled runtime artifact, build pipeline outputs, materialised executables. Owned by `WorkflowExecutable` (see §E2.9.3) — Units E/G's territory.
- **Publication / deployment state.** Publication status, deployment target, environment-specific configuration overlays. A separate concern with its own entity surface; never folded into State.
- **Search / listing-projection types.** Listing views, dashboard projections, full-text indexes. Derived read models (see §E2.9.3); never fields on State.
- **Security / ownership types.** Tenant ownership, permission grants, audit-of-author identifiers. Carried by ambient `TenantEntity` columns and a separate security model; not authored content.
- **Designer layout metadata.** Canvas positions, sizes, visual node grouping, designer-only annotations. Owned by the sibling entities `WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout` (Unit C FR-006), unified by `IWorkflowDefinitionLayout` (FR-007); never nested into `ActivityNode` and never reachable through `WorkflowDefinitionState`.
- **Validation errors.** Owned by the sibling entity `WorkflowDefinitionDraftValidation` (Unit C FR-021) — derived from State, not part of it.

A property newly proposed for `WorkflowDefinitionState` whose category is genuinely ambiguous between authored content and one of these out-of-State categories surfaces as an architecture-meeting escalation; resolution is constitutional (amend this section), not silent.

### §E2.9.3 Architectural triplet

`WorkflowDefinitionState` participates in an irreducible triplet that names the three artefacts every workflow definition produces in the system:

1. **`WorkflowDefinitionState`** — the canonical authored document (above).
2. **Read models / projections** — derived views over State for listing, search, dashboarding, and any non-authoring read need. These live in `Elsa.Workflows.Design.Api` or downstream domains' query layers; they are projection-shaped, not authoring-shaped. They are never persisted back into State.
3. **`WorkflowExecutable`** — the compiled runtime artifact (substance owned by Units E/G; named here so the triplet is complete). Build/Compile produces it from an immutable `WorkflowDefinitionVersion.State`; the Runtime sub-domain executes it per §E2.6's artifact-only contract. State is the source; `WorkflowExecutable` is the derived runnable form.

The three sit at separate scopes — **authoring**, **reading**, **executing** — and **must not be merged**. Conflating authoring and projection collapses the read side back into State and creates the god-object Sipke flagged. Conflating authoring and executable conflates source with output, breaking §E2.6's artifact-only rule. The triplet is the load-bearing structural rule that lets Design and Runtime stay separable per §E2.2.

### §E2.9.4 Enforcement

The in-State / out-of-State boundary is enforced by:

1. **The XML documentation header** on the `WorkflowDefinitionState` record (Unit C FR-003), quoting the scope and pointing at this section.
2. **PR review discipline** against this constitutional rule — reviewers reject creep.

Automated compile-/build-time enforcement (scope-policy static analyser) is **deferred to a future *Code Analysers* epic** that approaches the platform's static analysis as a unified bundle, rather than shipping ad-hoc per-rule micro-validators. The list of categories in §E2.9.2 will inform the eventual analyser when that epic opens; current repo-local findings live in [unfinished work](../reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

### §E2.9.5 Reconciliation policy applies here too

`WorkflowDefinition` / `WorkflowDefinitionVersion` / `WorkflowDefinitionDraft` reconciliation follows **Model X** — the same immutable-provenance, skip-or-throw-with-hash-safety-net policy codified for the activity catalog at the end of §E2.8. No per-pass mutating fields (no `LastSeenAt`, no `IsStale`, no `RemovedAt`) live on any workflow-design entity; reconciliation is transactional at creation time only. Where the provenance fields (`SourceKind` / `SourceId` / `SourceVersion` / `ProvisioningHash` / `ProvisionedAt` / `ProvisionedBy`) ultimately live on workflow-design entities is Unit D's allocation pass; Unit C codifies the policy and leaves the field allocation to Unit D.

### §E2.9.6 Status

**Provisional pending architecture-review ratification.** If the review revises the surrounding activity-reconciliation or workflow-definition scope rules, this section revises in tandem.

**Cross-references:** §E2.2 (Design ↔ Runtime split — the triplet operates within Design and seams into Runtime via `WorkflowExecutable`); §E2.6 (artifact-only runtime — the seam terminates at `WorkflowExecutable`, not at State); §E2.8 (Model X reconciliation policy, applies symmetrically per §E2.9.5).

### §E2.9.7 Draft-mutation command surface *(Unit 2 2026-06-03; provisional, pending architecture-review ratification)*

The canonical command surface for **mutating** a `WorkflowDefinitionDraft` is a **single coarse, diff-based command** — `IUpdateDraftCommand` — not a family of granular per-concept mutation commands.

- **One mutation command.** `IUpdateDraftCommand.Execute(UpdateDraftRequest)` accepts the **complete desired** `WorkflowDefinitionState` (+ its layout sibling, carried beside State per §E2.9.2 — never inside it). Full-state-always: there is no patch API. Inside the per-Draft distributed lock (`workflow-draft:{DraftId}`) it loads the stored state, wholesale-assigns the desired state (last-writer-wins — no version check), **diffs** stored vs desired per concept (Variables/Inputs/Outputs by `ReferenceKey`, Activities and layout by `NodeId`, activity I/O by (`NodeId`,`ReferenceKey`), connections by endpoint tuple), runs the in-lock validation gate, persists atomically, then publishes **one event per detected difference**.
- **The event surface is preserved, not collapsed.** The diff emits the same 20 per-concept mutation events the former granular commands published (catalogued in the Events section of `Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`); their *types* and catalog headings are unchanged — only the publication site moved onto `IUpdateDraftCommand`. This keeps the event-sourcing seam open for a later event-sourcing unit (Unit H): subscribers observe the per-diff stream regardless of whether the mutation arrived via 20 commands or one.
- **Lifecycle commands remain distinct.** `ICreateDraftCommand`, `ICloneDraftFromVersionCommand`, `IDiscardDraftCommand`, and `IPromoteDraftToVersionCommand` are **not** mutations of an existing Draft's content and stay as separate commands with their own lifecycle events (`DraftCreated`, `DraftDiscarded`). `IUpdateDraftCommand` emits none of these.
- **One origination event, not two.** A cloned Draft and a fresh Draft share the single origination event `DraftCreated`; there is **no** separate `DraftClonedFromVersion`. `ICloneDraftFromVersionCommand` delegates to `ICreateDraftCommand` (the single origination path), and clone-vs-fresh is distinguished solely by the immutable optional `WorkflowDefinitionDraft.SourceVersionId` — a plain provenance column (no navigation property) surfaced on `DraftCreated.SourceVersionId` (`null` for a fresh Draft).
- **Reads route through the named read ports.** Commands that only read (no change tracking) — e.g. `ICloneDraftFromVersionCommand` loading the source Version + layout — use the named per-aggregate read ports (`IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionVersionLayoutStore`, …). Provider adapters own query execution and any read-side hydration; a command opens its own tracked context only when it queries, mutates, and saves the *same* entity.
- **Validation pair unchanged.** The `DraftValidating` (Sequential, in-lock gate) / `DraftValidated` (Background, outcome) pair is published by the command exactly as before.

This supersedes Unit C's Phase-7 granular-command surface for Draft mutation. The framework-level CQS separation remains unaffected — this rule narrows only the **Draft-mutation** surface within the Design domain.

**Status:** Provisional pending architecture-review ratification. Current repo-local findings live in [unfinished work](../reports/unfinished-work.md), and planned durable work routes through the shared program-goals planner.

**Cross-references:** §E2.9.1/§E2.9.2 (what State carries — the diff operates over exactly those in-scope fields, layout stays beside State); §E2.6.6 (Sequential vs Background delivery strategies the command uses for the gate vs the per-diff stream).

## Elsa packaging

Supports Elsa §E5. Moved verbatim from Elsa constitution v5.0.0. The two-version-line rules that followed this paragraph were deleted by owner decision D5 (#2578); [ADR 0067](../adr/0067-package-versioning-uses-two-lines-with-computed-patch.md) keeps their reasoning and #2576 retires Line A.

**framework §2.13 — Elsa specialization.** Elsa's current packaging is the snapshot in §E2.4 above. The framework rule (packaging cohesion follows dependency cohesion; packaging is application-level and revisable) governs. Framework §2.13 places the choice of how versions are shared with the application; Elsa's choice is recorded below.

**Reversibility.** If, e.g., `Elsa.Serialization.Newtonsoft` and `Elsa.Serialization.SystemText` become demanded by applications outside Elsa, they could graduate into separately published features that Elsa's other features pull in via NuGet. The packaging is reversible per framework §2.16 (refactor-cost test) — preserving NuGet identity insulates consumers from the restructuring.

**Minimum project size (framework §2.16.1 — Elsa interpretive note).** Elsa's tree intentionally contains many sub-100-LoC projects; the 2026-07-04 audit ([MD-5 amendment report](../reports/elsa-4-w21-md5-minimum-project-size-amendment.md)) found all 13 of them exempt under the §2.16.1 exemption test, so the guidance ratifies the current shape rather than triggering a merge campaign. An exemption permits a small project; it does not require one: the 2026-09 maintainability pass merged eight of them into their parents where no dependency-direction or packaging reason held them apart. Worked examples per exception class: contracts-only `.Core` seams (`Elsa.Locking.Core`, `Elsa.Caching.Core`), primitives projects (`Elsa.Primitives.Hosting`), provider leaves (`Elsa.Locking.FileSystem`), the §E2.7 migration boundary, Layer-2 helpers (`Elsa.Serialization.Newtonsoft`), and independently-composable `[ShellFeature]` units / cross-domain contribution seams (`Elsa.Http.JavaScript`). The migration-boundary class currently has no live example: the audit's example, `Elsa3.Activities.Design.Import`, measured 56 lines then and has since grown past two thousand, so it no longer needs the exemption, and no Elsa 3 project is under the threshold today. The class stays valid for future boundary projects. New sub-100-LoC projects that fit none of the six classes need the one-sentence justification of §2.16.1.
