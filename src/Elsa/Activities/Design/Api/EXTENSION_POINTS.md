# Extension points — Activity Design API

`ActivitiesDesignApiFeature` is the host-independent composition root for the supported Activity Design management-client routes documented in [README.md](README.md). It does not depend on `Elsa.Server`.

## Replacement contracts

| Contract | Default | Purpose |
|---|---|---|
| `IActivityDefinitionStore` | Persistence-provider implementation | Definition reads used by catalog, definition, and diagnostics endpoints. |
| `IActivityDefinitionVersionStore` | Persistence-provider implementation | Version and normalized descriptor reads. |
| `IActivityAvailabilityEvaluator` | `DefaultActivityAvailabilityEvaluator` | Applies host include/exclude policy to catalog entries. |
| `IActivityAvailabilityDiagnosticsProjector` | `DefaultActivityAvailabilityDiagnosticsProjector` | Produces stable explanations for unavailable activities. |
| `IActivityAvailabilitySettingsStore` | `InMemoryActivityAvailabilitySettingsStore` | Stores API-managed availability settings; durable providers may replace it. |

These are single-owner seams. Replace them through DI; do not register competing implementations and rely on resolution order.

## Sources and reconciliation

Activity definitions are populated by Activity Design reconciliation sources, not by `Elsa.Server`. Provider modules contribute installed activity metadata through the reconciliation contracts described in [`Reconciliation/EXTENSION_POINTS.md`](../Reconciliation/EXTENSION_POINTS.md). EF Core and Groundwork persistence-specific seams are documented in their own catalogs:

- [`Persistence/EFCore/EXTENSION_POINTS.md`](../Persistence/EFCore/EXTENSION_POINTS.md)
- [`Persistence/Groundwork/EXTENSION_POINTS.md`](../Persistence/Groundwork/EXTENSION_POINTS.md)

The catalog endpoint normalizes those stored definitions and applies availability evaluation. Context-sensitive input options deliberately belong to Workflow Design API because they require submitted workflow state and node context.

Canonical ownership is defined in the [domain-owned API spec](../../../../../specs/092-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../../docs/glossary/elsa.md).
