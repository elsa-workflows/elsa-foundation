# Extension points — activity graph Design provider

This project owns the Design-time provider for reusable activity graphs. The canonical behavior and boundary are specified by [spec 092](../../../../../../specs/092-reusable-activity-definitions/spec.md) and its [provider/runtime contract](../../../../../../specs/092-reusable-activity-definitions/contracts/provider-runtime-seams.md).

## Registration inventory

| Contribution | Stable identity | Registration | Status |
|---|---|---|---|
| Activity graph Design provider | `elsa.activity-graph` | `GraphActivityProvider : IActivityProvider`; feature registration remains in T033 | Implemented by T026; registration planned by T033 |
| Provider manifest schema | `1` | `ActivityGraphManifest` parsing and canonical serialization | Implemented by T024 |

The provider may reference provider-neutral Design and Runtime Core contracts to compile executable material. It must not reference the graph Runtime implementation, Runtime API, or a concrete persistence provider.

## Implementable contributor interfaces

- `IActivityProvider` owns contract proposal, validation, deterministic compilation, resource measurement, and manifest migration for one stable provider key.
- `IActivityProviderRegistry.Add(IActivityProvider)` establishes one owner for a provider key; duplicate owners fail.
- `IActivityProviderRegistry.Resolve(providerKey, manifestSchemaVersion)` resolves an exact provider/schema pair and returns a guarded provider adapter.
- `ActivityDiagnosticOrderer.Order(IEnumerable<ActivityDiagnostic>)` applies the stable public diagnostic ordering before results leave the provider boundary.

`ActivityTemplateCompilation.ExecutableRoot` is currently a `JsonElement`. The graph provider returns a canonical authored root through that seam; Publishing must reconcile it to the reviewed typed executable-node model before the seam is considered final.
