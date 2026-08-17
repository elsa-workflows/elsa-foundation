# Activities Design API contract compatibility inventory

This inventory is the Phase 2 contract boundary for the 38-registration Activities Design owner. The
historical FastEndpoints HTTP/OpenAPI fixtures remain the immutable wire oracle; this document records where
the public CLR contracts live while the endpoint authoring implementation changes.

## Disposition summary

| Surface | Count | Former assembly | Current owner | Compatibility disposition |
|---|---:|---|---|---|
| API model records | 73 | `Elsa.Activities.Design.Api` | `Elsa.Activities.Design.Api.Core` | Existing namespace and public members retained; old assembly forwards every type |
| API request/command records | 44 | `Elsa.Activities.Design.Api` | `Elsa.Activities.Design.Api.Core` | Existing namespace and public members retained; old assembly forwards every type |
| API enums | 3 | `Elsa.Activities.Design.Api` | `Elsa.Activities.Design.Api.Core` | Existing names and values retained; old assembly forwards every type |
| Implementation seams/helpers | 10 | `Elsa.Activities.Design.Api` | `Elsa.Activities.Design.Api` | Remain implementation-owned; no wire/OpenAPI contract type is rooted here |

The stable set is exactly 120 forwarded types (73 + 44 + 3). `ActivityManagementPageView<T>` is forwarded as its
open generic definition. The old API project continues to reference FastEndpoints only for the retained legacy
endpoint implementation until the later endpoint-removal task; `Elsa.Activities.Design.Api.Core` has no endpoint
framework reference.

## Stable API model records (73)

All rows below retain the existing `Elsa.Activities.Design.Api.Models` namespace and public constructor/property
surface and are compiled into `Elsa.Activities.Design.Api.Core`:

`ActivityActionAvailabilityView`, `ActivityAuthoringCapabilitiesView`, `ActivityAuthoringCatalogView`,
`ActivityAuthoringDescriptorView`, `ActivityAuthoringIntrinsicView`, `ActivityAuthoringProvenanceView`,
`ActivityAuthoringStructureView`, `ActivityAuthoringTemplateView`, `ActivityContractProposalChangeView`,
`ActivityContractProposalView`, `ActivityContractTypeCapabilityView`, `ActivityContractView`,
`ActivityDefinitionDetailsView`, `ActivityDefinitionIdentityView`, `ActivityDefinitionLifecycleSummaryView`,
`ActivityDefinitionRecommendationView`, `ActivityDefinitionReferenceView`, `ActivityDefinitionVersionDetailsView`,
`ActivityDefinitionVersionReferenceView`, `ActivityDefinitionView`, `ActivityDependencyConsistencyView`,
`ActivityDependencyItemView`, `ActivityDependencyOccurrenceView`, `ActivityDependencyPageView`,
`ActivityDependencyQueryView`, `ActivityDraftValidationView`, `ActivityForkAccessBindingView`,
`ActivityForkCandidateLifecycleView`, `ActivityForkContractChangeView`, `ActivityForkContractComparisonView`,
`ActivityForkOutcomeView`, `ActivityForkPresentationView`, `ActivityForkPreviewView`,
`ActivityForkProviderMigrationView`, `ActivityForkReceiptView`, `ActivityForkSourceView`, `ActivityForkTargetView`,
`ActivityInputContractView`, `ActivityInputDefaultView`, `ActivityInputDescriptorView`,
`ActivityManagementPageView<T>`, `ActivityManagementSnapshotView`, `ActivityOutcomeContractView`,
`ActivityOutputContractView`, `ActivityOutputDescriptorView`, `ActivityPortDescriptorView`,
`ActivityProblemDetailsView`, `ActivityProviderAuthoringCapabilityView`,
`ActivityProviderManifestSchemaCapabilityView`, `ActivityProviderManifestView`, `ActivityPublishedTemplateView`,
`ActivityRecoveryView`, `ActivityTypeReferenceView`, `ActivityUpgradeApplyReceiptView`,
`ActivityUpgradeApplyResultView`, `ActivityUpgradePlanView`, `ActivityUpgradeReplacementView`,
`ActivityUpgradeVersionIdentityView`, `ActivityVersionChangeSubjectView`, `ActivityVersionChangeView`,
`ActivityVersionDiffIdentityView`, `ActivityVersionDiffSummaryView`, `ActivityVersionDiffView`,
`ActivityVersionProviderDiffView`, `RecommendedActivityDefinitionPageView`, `RecommendedActivityDefinitionView`,
`ReusableActivityDefinitionManagementView`, `ReusableActivityDefinitionMutationView`,
`ReusableActivityDraftManagementView`, `ReusableActivityDraftSummaryView`, `ReusableActivityDraftView`,
`ReusableActivityVersionLifecycleView`, `ReusableActivityVersionManagementView`,
`ReusableActivityVersionSummaryView`, `ReusableActivityVersionView`.

## Stable request and command records (44)

All rows below retain their existing `Elsa.Activities.Design.Api.Requests` or
`Elsa.Activities.Design.Api.Commands` namespace and public constructor/property surface and are compiled into
`Elsa.Activities.Design.Api.Core`:

`AddDefinition`, `AddVersion`, `ApplyActivityUpgradePlan`, `ApplyReusableActivityContractProposal`,
`ApplyReusableActivityFork`, `CompareActivityVersions`, `CreateActivityUpgradePlan`,
`CreateReusableActivityDefinition`, `CreateReusableActivityDraft`, `CreateReusableActivityDraftConflictCopy`,
`DiscardReusableActivityDraft`, `GetActivityAuthoringCapabilities`, `GetActivityAvailabilitySettings`,
`GetActivityDependencies`, `GetActivityUpgradeApplyReceipt`, `GetActivityUpgradePlan`, `GetDefinition`,
`GetReusableActivityDefinition`, `GetReusableActivityDraft`, `GetReusableActivityForkStatus`,
`GetReusableActivityVersion`, `GetVersion`, `ListActivityAuthoringCatalog`, `ListActivityAvailabilityDiagnostics`,
`ListDefinitionVersions`, `ListDefinitions`, `ListRecommendedActivityDefinitions`,
`ListReusableActivityDefinitions`, `ListReusableActivityDrafts`, `ListReusableActivityVersions`,
`MigrateReusableActivityDraft`, `PreviewActivityDraftDiff`, `PreviewReusableActivityFork`,
`ProposeReusableActivityContract`, `RefreshActivityUpgradePlan`, `ReplaceReusableActivityDraft`,
`RestoreReusableActivityVersion`, `RetireReusableActivityVersion`, `RevokeReusableActivityVersion`,
`SaveActivityAvailabilitySettings`, `SetRecommendedReusableActivityVersion`, `UpdateReusableActivityDefinition`,
`UpdateReusableActivityDraftPresentation`, `ValidateReusableActivityDraft`.

The former FastEndpoints `RouteParam` marker was not part of the public wire contract. It is removed from the
stable records while the existing `JsonIgnore` route-field disposition remains. The later Minimal API mapper owns
route-over-body precedence explicitly; the historical route/body behavior is already frozen in the before fixture.

## Stable enums (3)

`ActivityCatalogAvailability`, `ActivityForkCandidateLifecycleView`, and `ActivityForkOutcomeView` retain their
existing names, values, and string serialization contract and are compiled into `Elsa.Activities.Design.Api.Core`.

## Implementation-owned public seams (10)

These types are not accepted or produced as API/OpenAPI contracts and therefore remain in the implementation
assembly for now:

| Type | Reason |
|---|---|
| `ActivityAuthoringException` | Handler-to-transport error seam; the stable `ActivityProblemDetailsView` is forwarded |
| `ActivityProblemDetails` | HTTP context/result writer helper |
| `ActivityContractViewMappings` | Domain/API projection adapter |
| `ActivityDependencyViewMappings` | Domain/API projection adapter |
| `ActivityUpgradeViewMappings` | Domain/store/API projection adapter |
| `ActivityVersionDiffViewMappings` | Domain/API projection adapter |
| `IActivityAuthoringContext` | Legacy host adapter, not a wire contract |
| `IActivityAuthoringContextAsync` | Runtime authorization adapter, not a wire contract |
| `IActivityVersionSelectionPolicy` | Domain selection policy seam |
| `DefaultActivityVersionSelectionPolicy` | Domain selection policy implementation |

The implementation helpers retain their original namespace and public name so source consumers do not break while
the owner migration is in progress. They are deliberately excluded from the 120-type forwarder set and from
native endpoint request/response metadata.

## Executable compatibility gates

`tests/Elsa/Activities/Design/Tests/Api/ActivitiesDesignApiContractCompatibilityTests.cs` proves:

- all 120 expected types compile against the stable Core assembly;
- the former API assembly forwards exactly those 120 names, including the open generic page type;
- every forwarded type resolves to the same Core type and retains its complete public member surface; and
- Core has no `Elsa.Api.FastEndpoints`, `FastEndpoints`, or `FastEndpoints.Attributes` assembly reference.

The test pins a SHA-256 over the ordered public member signatures. Any member removal, addition, or signature drift
requires an explicit compatibility review and an intentional inventory/hash update.
