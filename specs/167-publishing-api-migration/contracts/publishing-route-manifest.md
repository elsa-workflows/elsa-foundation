# Publishing Route Manifest

The immutable-before capture expands the production `RouteConstants` through FastEndpoints discovery
and verifies the effective templates against the 23 reviewed registrations below. The route strings
retain the reserved `drafts` regular-expression constraint so the historical host cannot silently
bind a draft test run as a versioned route.

| # | Method | Effective route | Request | Response | Action | Historical success |
|---:|---|---|---|---|---|---:|
| 1 | GET | `/publishing/activities` | `ListConstructableActivities` | `IEnumerable<ConstructableActivityView>` | read | 200 |
| 2 | GET | `/publishing/activities/{activityId}/construct` | `ConstructActivity` | `ConstructedActivityView` | read | 200 |
| 3 | GET | `/publishing/incident-strategies` | none | `IncidentStrategiesResponse` | read | 200 |
| 4 | GET | `/publishing/value-conversion/profiles` | none | `ValueConversionProfilesResponse` | read | 200 |
| 5 | POST | `/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight` | `PreflightWorkflowPublication` | `PublicationPreflightView` | read | 200 |
| 6 | POST | `/publishing/workflows/preflight` | `PreflightWorkflowPublicationSnapshot` | `PublicationSnapshotPreflightView` | read | 200 |
| 7 | GET | `/publishing/workflows/{definitionId}/slots` | `ListPublicationSlots` | `PublicationSlotListResponse` | read | 200 |
| 8 | GET | `/publishing/workflows/{definitionId}/slots/{slotName}` | `GetPublicationSlot` | `PublicationSlotView` | read | 200 |
| 9 | DELETE | `/publishing/workflows/{definitionId}/slots/{slotName}` | `UnpublishPublicationSlotRequest` | `PublicationSlotView` | manage | 200 |
| 10 | POST | `/publishing/workflows/{definitionId}/slots/{slotName}/restore` | `RestorePublicationSlotRequest` | `PublicationSlotView` | manage | 200 |
| 11 | GET | `/publishing/workflows/{definitionId}/policy` | `GetWorkflowPublicationPolicy` | `PublicationPolicyView` | read | 200 |
| 12 | PUT | `/publishing/workflows/{definitionId}/policy` | `SetWorkflowPublicationPolicy` | `PublicationPolicyView` | manage | 200 |
| 13 | POST | `/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish` | `PublishWorkflowRequest` | `PublishedWorkflowView` | manage | 200/201 |
| 14 | POST | `/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/test-runs` | `StartWorkflowTestRun` | `WorkflowTestRunView` | manage | 200 |
| 15 | POST | `/publishing/workflows/drafts/test-runs` | `StartWorkflowDraftTestRun` | `WorkflowTestRunView` | manage | 200 |
| 16 | POST | `/publishing/preflight` | `RunRuntimeRequirementPreflight` | `RuntimeRequirementPreflightView` | read | 200 |
| 17 | POST | `/design/activities/drafts/{draftId}/publication-preflight` | `PreflightActivityDraftPublication` | `ActivityPublicationPreflightView` | read | 200 |
| 18 | POST | `/design/activities/drafts/{draftId}/publish` | `PublishActivityDraft` | `ActivityPublicationReceiptView` | manage | 201 |
| 19 | GET | `/design/activities/publications/{idempotencyKey}` | `GetActivityPublicationReceipt` | `ActivityPublicationReceiptView` | read | 200 |
| 20 | POST | `/publishing/activity-drafts/{draftId}/test-runs` | `StartActivityDraftTestRun` | `ActivityDraftTestRunView` | manage | 202 |
| 21 | GET | `/publishing/activity-test-runs/{testRunId}` | none | `ActivityDraftTestRunView` | manage | 200 |
| 22 | GET | `/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}` | none | `ActivityDraftTestRunView` | manage | 200 |
| 23 | POST | `/publishing/activity-test-runs/{testRunId}/cancel` | none | `ActivityDraftTestRunView` | manage | 202 |

The source of truth for capture cases is `PublishingCompatibilityCases.Manifest`; the table is a
reviewable contract artifact, while the host/OpenAPI capture supplies the effective runtime route
identity and operation metadata.
