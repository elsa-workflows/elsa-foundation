# Extension points — Activities.Design.Persistence.Groundwork domain

Groundwork provider catalog for activity-design persistence replacement contracts. Contracts are defined in `Elsa.Activities.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IActivityDefinitionStore` | `GroundworkActivityDefinitionStore` |
| `IActivityDefinitionVersionStore` | `GroundworkActivityDefinitionVersionStore` |
| `IAddActivityDefinitionCommand` | `GroundworkAddActivityDefinitionCommand` |
| `IAddActivityDefinitionVersionCommand` | `GroundworkAddActivityDefinitionVersionCommand` |
| `IActivityDefinitionLookup` | Core `ActivityDefinitionLookup` |
| `IActivityAvailabilitySettingsStore` | `GroundworkActivityAvailabilitySettingsStore` |
| `IActivityDefinitionManagementProjectionStore` | `GroundworkActivityDefinitionManagementProjectionStore` |
| `IActivityDefinitionAuthoringStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionDraftStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionVersionPublicationStore` | `GroundworkReusableActivityStores` |
| `IRecommendedActivityDefinitionPickerStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionLayoutStore` | `GroundworkReusableActivityStores` |
| `IActivityDraftValidationStore` | `GroundworkReusableActivityStores` |
| `IActivityForkStore` | `GroundworkReusableActivityStores` |
| `IActivityDirectDependencyStore` | `GroundworkReusableActivityStores` |
| `IActivityDependencyProjectionStore` | `GroundworkActivityDependencyProjection` |
| `IActivityDependencyProjectionRebuilder` | `GroundworkActivityDependencyProjection` |
| `IActivityUpgradePlanStore` | `GroundworkActivityUpgradePlanStore` |
| `IActivityUpgradeApplyReceiptStore` | `GroundworkActivityUpgradePlanStore` |
| `ICreateActivityDefinitionCommand` | `GroundworkReusableActivityStores` |
| `ISaveActivityForkCandidateCommand` | `GroundworkReusableActivityStores` |
| `IPruneActivityForkCandidatesCommand` | `GroundworkReusableActivityStores` |
| `IApplyActivityForkCandidateCommand` | `GroundworkReusableActivityStores` |
| `IUpdateActivityDefinitionPresentationCommand` | `GroundworkReusableActivityStores` |
| `ICreateActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IUpdateActivityDraftPresentationCommand` | `GroundworkReusableActivityStores` |
| `ICreateActivityDraftConflictCopyCommand` | `GroundworkReusableActivityStores` |
| `IReplaceActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IApplyActivityContractProposalCommand` | `GroundworkReusableActivityStores` |
| `IDiscardActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IStoreActivityDraftValidationCommand` | `GroundworkReusableActivityStores` |
| `IChangeActivityVersionLifecycleCommand` | `GroundworkReusableActivityStores` |
| `ISetActivityDefinitionRecommendationCommand` | `GroundworkReusableActivityStores` |

`AddGroundworkActivitiesDesignStores()` removes existing registrations for these contracts before adding the Groundwork implementations, preserving the one-active-implementation replacement-contract rule.

## Feature specialization seam

`IDesignAtomicWriter` defaults to `GroundworkDesignAtomicWrite` and uses `TryAddScoped`, so a host
can register a specialization before composing the Groundwork activity-design stores. An
inheriting feature that specializes after its base registration must use
`services.Replace(ServiceDescriptor.Scoped<IDesignAtomicWriter, Implementation>())`; direct
`AddScoped` would create an invalid duplicate replacement registration. Both orders are covered by
registration tests. The contract owns replay-safe multi-document mutations, durable operation
markers, and uncertain-commit reconciliation for workflow and activity design commands.

## Cross-references

- EF Core provider catalog: [`../EFCore/EXTENSION_POINTS.md`](../EFCore/EXTENSION_POINTS.md)
- Activity reconciliation extension points: [`../../Reconciliation/EXTENSION_POINTS.md`](../../Reconciliation/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
