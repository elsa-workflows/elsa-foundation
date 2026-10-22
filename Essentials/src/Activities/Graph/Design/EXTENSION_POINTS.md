# Extension points — activity graph Design provider

This project owns the Design-time provider for reusable activity graphs. The canonical behavior and boundary are specified by [spec 221](../../../../../specs/221-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../specs/221-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Activity graph Design provider | `elsa.activity-graph` | `GraphActivitiesDesignFeature` registers one `GraphActivityProvider` and exposes the same instance through `IActivityProvider`, `IActivityTemplateProviderCompiler`, and `IActivityTemplateDependencyDiscoverer` | Implemented |
| Provider reference rewriting | `elsa.activity-graph` | `GraphActivitiesDesignFeature` registers `GraphActivityProviderReferenceRewriter : IActivityProviderReferenceRewriter` | Implemented |
| Provider manifest schemas | `1`, `2` | `ActivityGraphManifest` parsing and canonical serialization; schema 2 adds `outcomeMappings` (spec 138) and accepts `Complete` nodes (spec 237) | Implemented |
| Complete-outside-graph draft rule | `Graph/CompleteOutsideGraph` | `GraphActivitiesDesignFeature` registers `CompleteOutsideGraphDraftValidator : IDraftValidator` (scoped) | Implemented |
| Runtime boundary contract | `elsa.graph-activity` / `1` | Pinned `RuntimeActivityDescriptor` and value-flow `ActivityContract` | Implemented |
| Closed value scope (spec 265) | compiler fingerprint `elsa.activity-graph/compiler/3.0.0` | The boundary descriptor carries `scopeModel: "closed"` and `variables[]` (`referenceKey`, `name`, `type`, `storageDriverKey`). Publishing's `ActivityTemplatePlacer` reads both to remap body references to the placement, compile body captures into graph variables, and mark the boundary `runtime.activityExecutionScope = closed`. Body occurrences carry their authored `outputs` in `ActivityTemplateOccurrenceCompilation.OutputBindings`. | Implemented |
| Body reference refusals (spec 265) | `activity.graph.body-input-unknown`, `-variable-unknown`, `-variable-scope-invisible`, `-producer-unknown`, `-capture-target-unknown`, `-capture-type-incompatible` | `GraphActivityProvider.ValidateAsync` and `CompileAsync` (`GraphBodyReferenceValidator`); the producer projection and capture type checks need resolved dependencies, so they run at compile only. See the [graph scope contract](../../../../../specs/265-graph-activity-scope-reads/contracts/graph-scope-references.md). | Implemented |

The provider may reference provider-neutral Design and Runtime Core contracts to compile executable material. It must not reference the graph Runtime implementation, Runtime API, or a concrete persistence provider.

## Overridable contracts

None. This feature contributes implementations keyed by provider identity; it does not install replaceable
single-owner defaults.

## Implementable contributor interfaces

| Contract | Owning layer | Registration and consumer | Known implementation |
|---|---|---|---|
| `IActivityProvider` | Activity Design Core | Provider features register one implementation per stable provider key. `IActivityProviderRegistry` and Activity Design authoring services consume it; duplicate provider keys fail. | `GraphActivityProvider` |
| `IActivityProviderReferenceRewriter` | Activity Design Core | Provider features register schema-aware rewriters as enumerable contributors. Publishing persistence consumes them when applying upgrade plans to opaque provider manifests. | `GraphActivityProviderReferenceRewriter` |
| `IActivityTemplateProviderCompiler` | Workflows Publishing Core | Provider features register one compiler per stable provider/schema pair. `IActivityTemplateProviderCompilerRegistry` and `ActivityTemplateCompiler` consume it. | `GraphActivityProvider` |
| `IActivityTemplateDependencyDiscoverer` | Workflows Publishing Core | Provider features register one discoverer per stable provider/schema pair. `IActivityTemplateDependencyDiscovererRegistry` and `ActivityTemplateCompiler` consume it. | `GraphActivityProvider` |
| `IDraftValidator` | Workflows Design Validations Core | Features register enumerable scoped validators; the draft validation gate runs them all and promotion refuses a Draft with errors. | `CompleteOutsideGraphDraftValidator` *(cross-domain)* |

At draft time the provider recognises `Complete` nodes through `IActivityDefinitionLookup`, resolved in a scope
created from `IServiceScopeFactory` because the provider is a singleton; without a registered lookup, draft
validation cannot credit `Complete` nodes and compilation remains the exact check.

The owning replacement and composition rules remain in the
[Activity Design API catalog](../../Design/Api/EXTENSION_POINTS.md) and
[Workflows Publishing catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md).
`ActivityDiagnosticOrderer.Order(IEnumerable<ActivityDiagnostic>)` applies the stable public diagnostic
ordering before results leave the provider boundary.

## Events

None.

## Registered handlers and startup work

- Mediator or notification handlers: none.
- Startup initializers, background tasks, or hosted services: none.

`ActivityTemplateCompilation.ExecutableRoot` is a typed `ExecutableNode`. The graph provider pins its
stable descriptor, value-flow contract, dynamic input/result projection schema, and exact runtime
requirements before Publishing hashes and places the immutable template.
