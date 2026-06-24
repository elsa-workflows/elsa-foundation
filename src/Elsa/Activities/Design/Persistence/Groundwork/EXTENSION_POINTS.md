# Extension points — Activities.Design.Persistence.Groundwork domain

Groundwork provider catalog for activity-design persistence replacement contracts. Contracts are defined in `Elsa.Activities.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IActivityDefinitionStore` | `GroundworkActivityDefinitionStore` |
| `IActivityDefinitionVersionStore` | `GroundworkActivityDefinitionVersionStore` |
| `IAddActivityDefinitionCommand` | `GroundworkAddActivityDefinitionCommand` |
| `IActivityDefinitionLookup` | Core `ActivityDefinitionLookup` |

`AddGroundworkActivitiesDesignStores()` removes existing registrations for these contracts before adding the Groundwork implementations, preserving the one-active-implementation replacement-contract rule.

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Activity reconciliation extension points: [`../../Reconciliation/EXTENSION_POINTS.md`](../../Reconciliation/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
