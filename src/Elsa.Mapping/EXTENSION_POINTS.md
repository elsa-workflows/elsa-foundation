# Extension points — Mapping domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Mapping` — the composition root where `MappingFeature` registers `ObjectMapper`. Two sections apply.

---

## Overridable contracts

### `IObjectMapper` *(Core — `Elsa.Mapping.Core`)*
- **Signature:** `TTarget Map<TSource, TTarget>(TSource source)`, `object Map(object source, Type sourceType, Type targetType)`.
- **Default impl:** `ObjectMapper` (this feature) — resolves `IObjectMapping<TSource,TTarget>` per type-pair from the DI container.
- **Override:** `services.Replace(ServiceDescriptor.Scoped<IObjectMapper, MyMapper>())`.

---

## Implementable contributor interfaces

### `IObjectMapping<TSource, TTarget>` *(Core — `Elsa.Mapping.Core`)*
- **Kind:** Contributor (implements a specific type-pair mapping).
- **Signature:** `TTarget Map(TSource source);`
- **Register:** `services.AddScoped<IObjectMapping<TSource, TTarget>, MyMapping>()`.
- **Consumed by:** `ObjectMapper` (this feature), which resolves the matching implementation per type-pair at call time. Not an event-driven contributor; no aggregating handler — `ObjectMapper` does the resolution directly.

**Known implementations (shipped):**
- `Elsa.Workflows.Design.Api` — `VersionToDetailsView`, `DefinitionToView`, `StateMappings` *(cross-domain — maps Workflows.Design entities to API view models)*
- `Elsa.Activities.Design.Api` — `ActivityDefinitionVersionToDetailsView`, `ActivityDefinitionToView` *(cross-domain — maps Activities.Design entities to API view models)*
- `Elsa3.Mapping` — `Elsa3WorkflowDefinitionToWorkflowDefinitionVersion`, `Elsa3WorkflowDefinitionToState`, `Elsa3ActivityToState`, `Elsa3ArgumentDefinitionToInputOutput` *(cross-domain — legacy import mappings)*

---

## Cross-references

- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1.
