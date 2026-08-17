# Publishing Route Manifest

The immutable before capture expands `RouteConstants` at runtime and pins the exact effective templates. `workflow-publishing.read` appears on 12 routes and `workflow-publishing.manage` on 11. Minimal endpoint metadata names only that catalog action; evaluator-level wildcard compatibility is not route ownership metadata.

| # | Method | Effective route | Request | Response | Action |
|---:|---|---|---|---|---|
| 1 | GET | `publishing/activities` | `ListConstructableActivities` | `IEnumerable<ConstructableActivityView>` | read |
| 2 | GET | `publishing/activities/{activityId}/construct` | `ConstructActivity` | `ConstructedActivityView` | read |
| 3 | GET | `publishing/incident-strategies` | none | `IncidentStrategiesResponse` | read |
| 4 | GET | `publishing/value-conversion/profiles` | none | `ValueConversionProfilesResponse` | read |
| 5 | POST | `publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight` | `PreflightWorkflowPublication` | `PublicationPreflightView` | read |
| 6 | POST | `publishing/workflows/preflight` | `PreflightWorkflowPublicationSnapshot` | `PublicationSnapshotPreflightView` | read |
| 7 | GET | `publishing/workflows/{definitionId}/slots` | `ListPublicationSlots` | `PublicationSlotListResponse` | read |
| 8 | GET | `publishing/workflows/{definitionId}/slots/{slotName}` | `GetPublicationSlot` | `PublicationSlotView` | read |
| 9 | DELETE | `publishing/workflows/{definitionId}/slots/{slotName}` | `UnpublishPublicationSlotRequest` | `PublicationSlotView` | manage |
| 10 | POST | `publishing/workflows/{definitionId}/slots/{slotName}/restore` | `RestorePublicationSlotRequest` | `PublicationSlotView` | manage |
| 11 | GET | `publishing/workflows/{definitionId}/policy` | `GetWorkflowPublicationPolicy` | `PublicationPolicyView` | read |
| 12 | PUT | `publishing/workflows/{definitionId}/policy` | `SetWorkflowPublicationPolicy` | `PublicationPolicyView` | manage |
| 13 | POST | `publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish` | `PublishWorkflowRequest` | `PublishedWorkflowView` | manage |
| 14 | POST | `publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/test-runs` | `StartWorkflowTestRun` | `WorkflowTestRunView` | manage |
| 15 | POST | `publishing/workflows/drafts/test-runs` | `StartWorkflowDraftTestRun` | `WorkflowTestRunView` | manage |
| 16 | POST | `publishing/preflight` | `RunRuntimeRequirementPreflight` | `RuntimeRequirementPreflightView` | read |
| 17 | POST | `design/activities/drafts/{draftId}/publication-preflight` | `PreflightActivityDraftPublication` | `ActivityPublicationPreflightView` | read |
| 18 | POST | `design/activities/drafts/{draftId}/publish` | `PublishActivityDraft` | `ActivityPublicationReceiptView` | manage |
| 19 | GET | `design/activities/publications/{idempotencyKey}` | route key | `ActivityPublicationReceiptView` | read |
| 20 | POST | `publishing/activity-drafts/{draftId}/test-runs` | `StartActivityDraftTestRun` | `ActivityDraftTestRunView` | manage |
| 21 | GET | `publishing/activity-test-runs/{testRunId}` | route key | `ActivityDraftTestRunView` | manage |
| 22 | GET | `publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}` | route keys | `ActivityDraftTestRunView` | manage |
| 23 | POST | `publishing/activity-test-runs/{testRunId}/cancel` | route key | `ActivityDraftTestRunView` | manage |

## Required route invariants

- The reserved literal `drafts` never binds as a workflow `versionId`.
- Activity draft IDs in preflight, publish, and test-run start are route-authoritative over JSON.
- Activity routes under `design/activities` remain owned by Publishing and use Publishing actions.
- Shared GET/DELETE slot templates remain distinct by HTTP method.
- Stable operation IDs, Publishing owner/tag/authoring metadata, typed request/response metadata, and 401/403 dispositions are present on every mapped operation.
