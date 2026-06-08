# Elsa Worked Examples

These examples instantiate constitutional rules using concrete `Elsa.*` names. They are reference material, not the enforceable gate surface. The enforceable rules remain in `.specify/memory/constitution.md` and `.specify/memory/constitution-framework.md`.

## elsa-core baseline case study

The framework was distilled from a structural analysis of the **elsa-core** codebase (`github.com/elsa-workflows/elsa-core`). elsa-core is preserved here as a worked case study - a real-world example of the structural problems the framework is designed to prevent (framework §1).

elsa-core exhibited every anti-pattern in framework §1 at once:

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

- `Elsa.Persistence.Core` - generic persistence contracts such as `IAddCommand<T>` and `IQuery<T>`.
- `Elsa.Serialization.Core` - serialization contracts.

Workflows sub-domain Cores:

- `Elsa.Workflows.Design.Core` - design-time contracts: `IWorkflowDefinition`, `IInputDefinition`, `IOutputDefinition`, and related types.
- `Elsa.Workflows.Runtime.Core` - runtime contracts. Specifics are deferred to the workflow execution seam follow-up. It does not reference `Elsa.Workflows.Design.Core`.

The observable cross-`.Core` reference today is in Design's sub-sub-domain Cores:

- `Elsa.Workflows.Design.Persistence.Core` - references `Elsa.Workflows.Design.Core` and may reference `Elsa.Persistence.Core` as an explicit design choice when useful.

Implementations:

- `Elsa.Workflows.Design.Persistence.EFCore` - EF Core implementation of the design-persistence sub-sub-domain.

Impl-to-impl carve-out: implementations across unrelated sub-domains never reference each other. Implementations within the same provider family may, for example an `Elsa.Workflows.Design.Persistence.EFCore.SqlServer` provider package extending an `Elsa.Workflows.Design.Persistence.EFCore` base implementation.

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
- `OnJsonPayloadConvertersInitializing`, an `IEvent` exposing a directly-accessible `ICollection<JsonConverter> Converters`.
- `IJsonConverterSource`, the return-style contributor interface.

```csharp
public sealed class OnJsonPayloadConvertersInitializing : IEvent
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
    : IEventHandler<OnJsonPayloadConvertersInitializing>
{
    public Task Handle(OnJsonPayloadConvertersInitializing e, CancellationToken ct)
    {
        foreach (var source in sources)
            foreach (var converter in source.GetConverters())
                e.Converters.Add(converter);
        return Task.CompletedTask;
    }
}

var @event = new OnJsonPayloadConvertersInitializing();
await eventPublisher.Publish(@event);
registry.RegisterAll(@event.Converters);
```

`Elsa.Expressions` and other contributing features extend serialization by implementing `IJsonConverterSource` and registering it via DI. They do not register their own event handler, and neither feature references the other.

At runtime, `JsonPayloadSerializer` sync code accesses the populated `JsonPayloadConverterRegistry` directly.

Further examples of this contributor-interface + single-aggregating-handler shape are documented in `src/Elsa.Persistence.EFCore/EXTENSION_POINTS.md`.

Legacy state: the historical implementation used `IPayloadSerializerConverterProvider`. Migration to the pattern above is tracked in the Unit A follow-up.

## Feature inheritance

Instantiates framework §2.5.

Elsa's persistence stack inherits across three levels:

```text
PersistenceShellFeatureBase<TDbContext>
    -> EFCoreWorkflowsPersistenceFeatureBase
            -> SqliteWorkflowDefinitionPersistenceShellFeature
```

Each level adds to or specialises the level above it through compile-time inheritance, never through peer references. The leaf is the activated feature; intermediate levels are abstract.

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
| Design-time | `OnDeclarationsDocumentGenerating` | `IJavaScriptDeclarationContributor` | Contributor | `BuildDeclarationsDocument` | Design-time contributors |
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

