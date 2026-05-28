# Contracts — Read Surfaces

Tier-1 read contracts that design-time consumers (UI, tooling, future Validations.<Domain> features) reference without depending on `*.Persistence.Core`. Matches the entity-design summary §3.5 pattern landed in Unit B (read interface in `*.Core`; entity in `*.Persistence.Core` implements the interface).

---

## `IWorkflowDefinitionLayout` — `Elsa.Workflows.Design.Core/Contracts/` (FR-007)

```csharp
namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionLayout
{
    string Id { get; }
    IReadOnlyList<IDesignMetadataRecord> Records { get; }
}

public interface IDesignMetadataRecord
{
    string NodeId { get; }
    double X { get; }
    double Y { get; }
    double? Width { get; }
    double? Height { get; }
    IReadOnlyDictionary<string, object?>? AdditionalProperties { get; }
}
```

**Implemented by:** both `WorkflowDefinitionVersionLayout` and `WorkflowDefinitionDraftLayout` in `Elsa.Workflows.Design.Persistence.Core/Entities`.

**Consumers:** design-time UI (rendering the canvas), layout-aware tooling, the eventual Clone-from-Version command which deep-copies layout from a Version's layout sibling into a Draft's layout sibling. None of these consumers depends on `*.Persistence.Core`.

**Non-branching reads:** a consumer that has either a Version-layout or a Draft-layout doesn't need to discriminate between the two — `IWorkflowDefinitionLayout` is the common surface.

---

## `IWorkflowDefinitionDraftValidation` — `Elsa.Workflows.Design.Validations.Core/Contracts/` (FR-021 read side)

```csharp
namespace Elsa.Workflows.Design.Validations.Core.Contracts;

public interface IWorkflowDefinitionDraftValidation
{
    string Id { get; }
    string WorkflowDefinitionDraftId { get; }
    IReadOnlyList<ValidationError> Errors { get; }
}
```

**Implemented by:** `WorkflowDefinitionDraftValidation` in `Elsa.Workflows.Design.Persistence.Core/Entities`.

**Consumers:**
- The UI (renders the current error set, grouped by `(Path, Type)` per FR-022).
- The `IPromoteDraftToVersionCommand` (Unit D — gate-checks `Errors.IsEmpty` per FR-024).
- Any future consumer that wants to read the validation state without taking a persistence-side dependency.

---

## `IsRequired` contract addition — `Elsa.Activities.Design.Core/Models/` (FR-036)

Two records gain a single additive constructor parameter:

```csharp
namespace Elsa.Activities.Design.Core.Models;

public sealed record InputDefinition(
    string ReferenceKey,
    string Name,
    TypeInformation Type,
    TypeInformation? StorageDriverType,
    string DisplayName,
    string? Category,
    bool? IsBrowsable = null,
    bool? IsSerializable = null,
    string? Description = null,
    float Order = 0,
    string? UiHint = null,
    IDictionary<string, object>? PropertyInfo = null,
    IDictionary<string, object>? UISpecifications = null,
    bool IsRequired = false);                                   // <— NEW per FR-036

public sealed record OutputDefinition(
    string ReferenceKey,
    string Name,
    TypeInformation Type,
    TypeInformation? StorageDriverType,
    string DisplayName,
    string? Category,
    bool? IsBrowsable = null,
    bool? IsSerializable = null,
    string? Description = null,
    float Order = 0,
    string? UiHint = null,
    IDictionary<string, object>? PropertyInfo = null,
    IDictionary<string, object>? UISpecifications = null,
    bool IsRequired = false);                                   // <— NEW per FR-036
```

**Defaults to `false`** for full backward compatibility — existing construction sites that don't pass `IsRequired` continue to behave identically. Framework §2.21.1 preserved.

**Used by:**
- `RequiredInputOutputValidator` (one of the five baseline validators in `Elsa.Workflows.Design.Validations`) — reads `IsRequired` from each activity's input/output declarations (via the catalog's `IActivityDefinitionVersion.Inputs` / `Outputs`) AND from `WorkflowDefinitionState.Inputs` / `Outputs` (workflow-level).
- Persistence: `Elsa.Activities.Design.Persistence.EFCore` EF mapping gains an `IsRequired` column for the activity-side input/output tables.

---

## Migration / construction-site impact

Per R10: fresh init migration regenerates the SQLite schema for both `ActivitiesDesignDbContext` (gains `IsRequired` column) and `WorkflowsDesignDbContext` (removes `MetaData` column, adds three new entity tables).

Existing call sites that construct `InputDefinition` or `OutputDefinition` without `IsRequired` continue to compile and behave identically (default `false`); the field is opt-in additive.

---

## Cross-references

- `IWorkflowDefinitionLayout` consumers also reference data-model.md §2.6 for the implementation entity pair.
- `IWorkflowDefinitionDraftValidation` consumers also reference data-model.md §2.7.
- `IsRequired` consumers cross-reference FR-033's `RequiredInputOutputValidator` (baseline validator #4).
- Read-contract Tier 1 pattern precedent: `2026-05-24_ENTITY_DESIGN_SUMMARY_JOEY.md` §3.5 + Unit B's `IActivityDefinitionVersion`.
