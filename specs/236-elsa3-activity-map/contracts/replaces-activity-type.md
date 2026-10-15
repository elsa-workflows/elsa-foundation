# Contract: `ReplacesActivityTypeAttribute` and its catalog facet (spec 236 FR-009)

## Attribute (Essentials, `Elsa.Activities.Runtime.Core`)

```csharp
namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares a type name from another workflow system or catalog that this activity stands in for. Importers use it to
/// resolve imported nodes to this activity. Adding or removing a declaration changes the activity's catalog content, so
/// it needs a new activity version.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ReplacesActivityTypeAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = string.IsNullOrWhiteSpace(typeName)
        ? throw new ArgumentException("A replaced activity type name cannot be blank.", nameof(typeName))
        : typeName;
}
```

It is generic and names no external system (§E2.7). Only `Elsa3.Mapping` interprets it today.

## Facet written by the CLR scanner

| Field | Value |
|---|---|
| Kind | `elsa.replaced-activity-types` |
| Schema version | `1` |
| Payload | `{ "typeNames": [ "Acme.SendInvoice" ] }` (trimmed, distinct, ordinal-sorted) |

**Rules**:

- The scanner reads declarations on the class itself, not its base types.
- It emits no facet when there is no declaration, so existing catalog hashes do not change.
- The payload never contains `ports`.

## Author workflow

1. Rebuild the Elsa 3 custom activity as an Elsa 4 CLR activity, with properties named like the Elsa 3 members (matched
   case-insensitively).
2. Add `[ReplacesActivityType("Acme.SendInvoice")]` and bump the activity version (`[Version]` or the assembly version).
3. Reconcile. Converting Elsa 3 nodes of type `Acme.SendInvoice` now resolves to the highest version of this activity.
4. When migration ends, remove the attribute in a new version. Older versions keep their facet, but the converter reads
   only each definition's highest version, so the alias is gone for new conversions.
