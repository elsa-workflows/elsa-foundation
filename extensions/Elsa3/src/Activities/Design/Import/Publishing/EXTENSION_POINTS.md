# Extension points — Elsa 3 Activity Import Publishing

The adapter between the Elsa 3 import and Workflows Publishing (spec 246). It exposes no contract of its own.

---

## Implemented replacement contracts

### `IReusableActivityImportPublisher` *(owned by `Elsa3.Activities.Design.Import`)*

- **Implementation:** `PublisherReusableActivityImportPublisher` (internal), registered with `Replace` by
  `Elsa3ImportActivitiesPublishingFeature`, so a shell holds exactly one.
- **Behaviour:** checks that the activity publisher resolves the import's tenant, then calls
  `IActivityDefinitionPublisher.PreflightAsync` and `PublishReviewedAsync` with the review token, the plan's version id
  (`RequestedVersionId`) and the phase's idempotency key. A preflight that is not publishable, a rejection or a
  non-applied receipt becomes a refused outcome with the publisher's code and diagnostics.

## Registered services

- `ReusableActivityImportPublishPhase` (`TryAddScoped`): the import runs it after its commit when the host composes it.

## Feature

- `Elsa3ImportActivitiesPublishing` depends on `Elsa3ImportJsonActivities` and `WorkflowsPublishingApi`. No event
  handlers, contributors or tasks.

The bridge registers an `IActivityDraftPublicationIdentity` for the ordinary reviewed activity publisher. For imported draft IDs it reads the tenant-owned publication reservation. Studio authors can therefore review and publish later while satisfying the exact references in their imported callers. Preflight and publish check earlier active reservations in the same tenant and definition before advancing its version.
