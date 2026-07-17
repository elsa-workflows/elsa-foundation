# Extension points — activity graph Design provider

This project owns the Design-time provider for reusable activity graphs. The canonical behavior and boundary are specified by [spec 092](../../../../../specs/092-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../specs/092-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Activity graph Design provider | `elsa.activity-graph` | `GraphActivitiesDesignFeature` registers one `GraphActivityProvider` and exposes the same instance through `IActivityProvider`, `IActivityTemplateProviderCompiler`, and `IActivityTemplateDependencyDiscoverer` | Implemented |
| Provider reference rewriting | `elsa.activity-graph` | `GraphActivitiesDesignFeature` registers `GraphActivityProviderReferenceRewriter : IActivityProviderReferenceRewriter` | Implemented |
| Provider manifest schema | `1` | `ActivityGraphManifest` parsing and canonical serialization | Implemented by T024 |

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

The owning replacement and composition rules remain in the
[Activity Design API catalog](../../Design/Api/EXTENSION_POINTS.md) and
[Workflows Publishing catalog](../../../Workflows/Publishing/Api/EXTENSION_POINTS.md).
`ActivityDiagnosticOrderer.Order(IEnumerable<ActivityDiagnostic>)` applies the stable public diagnostic
ordering before results leave the provider boundary.

`ActivityTemplateCompilation.ExecutableRoot` is currently a `JsonElement`. The graph provider returns a canonical authored root through that seam; Publishing must reconcile it to the reviewed typed executable-node model before the seam is considered final.

## Events

None.

## Registered handlers and startup work

- Mediator or notification handlers: none.
- Startup initializers, background tasks, or hosted services: none.
