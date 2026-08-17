# Publishing Compatibility Evidence Contract

## Baseline provenance

- The before source is the exact green main commit immediately preceding Wave 8 production changes.
- The checked-in capture script, runner, project graph, root build inputs, and fixture hashes are recorded in a receipt and verified from committed blobs.
- The runner fails if its executing content differs from the pinned committed content.
- Two detached captures must produce byte-identical HTTP, projected OpenAPI, raw OpenAPI, approval, and receipt data.
- The frozen baseline contains exactly 23 registrations and 23 OpenAPI operations.

## Required HTTP coverage

Every route has an anonymous challenge and an authenticated case. Every endpoint family additionally has successful and representative binding/domain failures. The corpus must include:

- missing, zero-length, JSON `null`, malformed, wrong-content-type, and absent-content-type bodies where applicable;
- route/body conflicts and reserved `drafts` route selection;
- 200/201/202 and exact `Location` behavior;
- 400/403/404/409/422/500/501/503 ProblemDetails families;
- preflight token, review snapshot, policy revision, publication ID, idempotency key, and request fingerprint outcomes;
- slot unpublish/restore and compensation;
- activity receipt replay and fingerprint mismatch;
- workflow/activity test-run creation, lookup, expiry, cancellation, and non-invocation on invalid or denied requests;
- cancellation rethrow/identity where the existing endpoint promise exposes it.

## OpenAPI comparison

For each operation compare operation ID, tag, method/path, parameters, request body, response statuses, headers, content types, schemas, and security. Compare unchanged common response/schema facets deeply; removing an approved facet must not remove the surrounding operation from comparison.

## Approval registry

Each entry identifies one endpoint/case/operation facet and exact before/after values, reason, owner, review reference, and optional follow-up. Validation must fail for duplicate keys, unknown properties, unused entries, no-op values, one-sided values, stale keys, overly broad matches, or values absent from the real before/after artifacts. Dedicated mutation tests bite every rule and assert typed validation errors with exact keys/messages.

## Stable contract evidence

- Every API-visible request/response/error type is owned by `Elsa.Workflows.Publishing.Api.Core` or an existing stable shared Core assembly.
- Public namespaces and member signatures remain compatible; former implementation types resolve through forwarding where required.
- The stable Core dependency graph contains no ASP.NET Core, FastEndpoints, owner implementation, provider, store, handler, or serializer-context dependency.
- Effective HTTP JSON resolver order covers every top-level accepts/produces type through generated metadata before reflection fallback.

## Report truth

The final report records exact commands, counts, hashes, commits or durable Git object identities, fixture approvals, E2E source/build provenance, warnings, residual risks, rollback, and the handoff to #1376. Claims are updated after the final review and final reachable commit.

## Publishing wire-type inventory (T010)

The immutable FastEndpoints capture exposes 23 operations (21 distinct paths) and 63 pre-existing
public Publishing request/response contract types from the former
`Elsa.Workflows.Publishing.Api` assembly. Five stable error-contract types are added so every
serialized Publishing-owned success and problem graph has the same unload-safe lifetime boundary,
for 68 public API Core types in total. The table below is the compatibility disposition used by the
stable API Core seam.
The source files remain linked under their existing namespaces; callers resolve the same full type
names from `Elsa.Workflows.Publishing.Api.Core`, while the former implementation assembly forwards
each type with `TypeForwardedTo`.

| Contract family | API Core-owned declarations | Former assembly disposition |
|---|---|---|
| Activity draft test-run requests and views | `ActivityDraftTestRunInput`, `StartActivityDraftTestRun`, `GetActivityDraftTestRun`, `GetActivityDraftTestRunByIdempotencyKey`, `CancelActivityDraftTestRun`, `ActivityDraftTestRunView`, `ActivityDraftTestRunFailureView`, `ActivityDraftTestRunExpirationView`, `ActivityDraftTestRunCancellationView` | Linked from `Models/ActivityDraftTestRunViews.cs`; all forwarded |
| Activity publication requests and views | `PreflightActivityDraftPublication`, `GetActivityPublicationReceipt`, `PublishActivityDraft`, `ActivityPublicationDependencyEvidenceView`, `ActivityPublicationCapabilityReadinessView`, `ActivityPublicationPreflightView`, `ActivityPublicationReceiptView` | Linked from the existing request/model files; all forwarded |
| Catalog, construction, incident, and conversion | `ListConstructableActivities`, `ConstructActivity`, `ConstructableActivityView`, `ConstructedActivityView`, `ArgumentView`, `ListIncidentStrategies`, `IncidentStrategiesResponse`, `IncidentStrategyDescriptorView`, `IncidentStrategyReferenceView`, `ListValueConversionProfiles`, `ValueConversionProfilesResponse`, `ValueConversionProfileView`, `ValueConversionProfileReferenceView` | Linked from existing request/model files; all forwarded |
| Publication policy, preflight, slot, and workflow publication | `ListPublicationSlots`, `GetPublicationSlot`, `UnpublishPublicationSlotRequest`, `RestorePublicationSlotRequest`, `UnpublishPublicationSlot`, `RestorePublicationSlot`, `GetWorkflowPublicationPolicy`, `SetWorkflowPublicationPolicy`, `PreflightWorkflowPublication`, `PreflightWorkflowPublicationSnapshot`, `PublishWorkflowRequest`, `PublicationView`, `PublicationSlotView`, `PublicationSlotListResponse`, `PublicationPolicyView`, `PublicationPreflightView`, `PublicationSnapshotPreflightView`, `PublicationTriggerChangeView`, `PublicationTriggerConflictView`, `PublicationTriggerClaimView` | Linked from existing request/model files; all forwarded |
| Publication contract enums and conversion helpers | `PublicationActionView`, `PublicationPolicyDefaultActionView`, `PublicationPolicySourceView`, `PublicationTriggerChangeKindView`, `PublicationTriggerCardinalityView`, `PublicationContract`, `PublicationIntentContract`, `PublicationPolicyContract` | Linked from `Models/PublicationManagementViews.cs`; all forwarded |
| Runtime preflight and workflow test-run | `RunRuntimeRequirementPreflight`, `RuntimeRequirementPreflightView`, `RuntimeRequirementPreflightItemView`, `StartWorkflowTestRun`, `StartWorkflowDraftTestRun`, `WorkflowTestRunView` | Linked from existing request/model files; all forwarded |
| Stable problem responses | `ActivityPublishingDiagnosticView`, `ActivityPublishingProblemDetails`, `ExpressionPublicationValidationDiagnosticView`, `ExpressionPublicationValidationProblemDetails`, `RuntimePreflightProblemDetails` | Introduced in API Core from the former implementation-local wire shapes; forwarded by the facade so JSON metadata never owns an implementation type |

The following types are reachable from those top-level accepts/produces graphs but are already
owned by stable shared Core contracts and are not duplicated in Publishing API Core: activity
diagnostics/version-diff and presentation records from `Elsa.Activities.Design.Core`, workflow
definition state and metadata records from `Elsa.Workflows.Design.Core` / Design Persistence Core,
`ValueRepresentation` from `Elsa.Primitives`, and publication outcomes/failures, publication
status, and published workflow views from `Elsa.Workflows.Publishing.Core`, and runtime model
types from `Elsa.Workflows.Runtime.Core`. `JsonElement` is the intentionally opaque
standard-library payload root. Native OpenAPI may reference those existing stable assemblies, but must not reference API
implementation services, endpoint classes, stores, providers, or test-run resources.

The former `PublishedActivityDefinitionView` remains implementation-owned because it is not an
accepts/produces type in the frozen 23-operation capture; it is not forwarded or added to the
stable metadata graph. The internal runtime-preflight intermediate records and enums likewise
remain implementation-owned. Custom activity/workflow/runtime ProblemDetails records were
implementation-local runtime error adapters and did not appear in the historical OpenAPI document.
Their unchanged wire shapes now live in API Core so source-generated serialization cannot retain a
replaceable implementation; their HTTP behavior remains a Phase 3 compatibility obligation.

For auditability, the raw OpenAPI schema inventory is reproduced here (96 schemas, including
nested request/response roots). Entries in the first two groups are stable existing contracts; the
API Core declarations are the top-level types listed above and are intentionally not repeated as a
second ownership category.

**Existing stable shared schemas:** `ActivityDependencyPathItem`, `ActivityDiagnostic`,
`ActivityDiagnosticLocation`, `ActivityDiagnosticSeverity`, `ActivityDiagnosticSubject`,
`ActivityNode`, `ActivityNodeOrigin`, `ActivityNodeStructure`, `ActivityPresentationRecord`,
`ActivityPublicationOutcome`, `ActivityVersionBump`, `ActivityVersionChange`,
`ActivityVersionChangeArea`, `ActivityVersionChangeImpact`, `ActivityVersionChangeSubject`,
`ActivityVersionCompatibility`, `ActivityVersionDiffSummary`, `ActivityVersionIdentity`,
`ActivityVersionProviderDiff`, `ArgumentState`, `ArgumentValue`,
`AuthoredValueConversionLimits`, `AuthoredValueConversionMode`, `AuthoredValueConversionProfile`,
`AuthoredValueConversionRequest`, `AuthoredWorkflowIntrinsic`, `AuthoredWorkflowIntrinsicKind`,
`CollectionKind`, `DesignMetadataRecord`, `InputDefinition`, `JsonElement`, `OutputDefinition`,
`IncidentStrategyReference`, `PublicationFailure`, `PublicationStatusView`, `PublishedWorkflowView`, `TypeReference`, `ValueRepresentation`,
`VariableDefinition`, `VariableReference`, `WorkflowCheckpointCadenceOptions`,
`WorkflowDefinitionState`, `WorkflowStrategyOptions`.

**API Core schemas:** `ActivityDraftTestRunCancellationView`, `ActivityDraftTestRunExpirationView`,
`ActivityDraftTestRunFailureView`, `ActivityDraftTestRunInput`, `ActivityDraftTestRunView`,
`ActivityPublicationCapabilityReadinessView`, `ActivityPublicationDependencyEvidenceView`,
`ActivityPublicationPreflightView`, `ActivityPublicationReceiptView`, `ArgumentView`,
`ConstructActivity`, `ConstructableActivityView`, `ConstructedActivityView`,
`GetPublicationSlot`, `GetWorkflowPublicationPolicy`, `IncidentStrategiesResponse`,
`IncidentStrategyReferenceView`, `ListConstructableActivities`, `ListPublicationSlots`,
`PreflightActivityDraftPublication`, `PreflightWorkflowPublication`,
`PreflightWorkflowPublicationSnapshot`, `PublicationActionView`, `PublicationPolicyDefaultActionView`,
`PublicationPolicySourceView`, `PublicationPolicyView`, `PublicationPreflightView`,
`PublicationSlotListResponse`, `PublicationSlotView`, `PublicationSnapshotPreflightView`,
`PublicationTriggerCardinalityView`, `PublicationTriggerChangeKindView`,
`PublicationTriggerChangeView`, `PublicationTriggerClaimView`, `PublicationTriggerConflictView`,
`PublicationView`, `PublishActivityDraft`, `PublishWorkflowRequest`,
`RestorePublicationSlotRequest`, `RunRuntimeRequirementPreflight`, `SetWorkflowPublicationPolicy`,
`StartActivityDraftTestRun`, `StartWorkflowDraftTestRun`, `StartWorkflowTestRun`, `ValueConversionProfileReferenceView`,
`ValueConversionProfileView`, `ValueConversionProfilesResponse`, `WorkflowTestRunView`.

The raw document also contains the public helper/request roots `GetActivityPublicationReceipt`,
`GetActivityDraftTestRun`, `GetActivityDraftTestRunByIdempotencyKey`, `ListIncidentStrategies`,
`ListValueConversionProfiles`, `UnpublishPublicationSlot`, `UnpublishPublicationSlotRequest`,
`RestorePublicationSlot`, `CancelActivityDraftTestRun`, and the three conversion helpers
`PublicationContract`, `PublicationIntentContract`, `PublicationPolicyContract`; these are
represented in the manifest and stable assembly inventory even when API Explorer flattens them
into an operation's request or response schema graph.
