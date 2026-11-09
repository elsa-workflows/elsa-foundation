# Contract: `IReusableActivityImportPublisher`

**Owner**: `Elsa3.Activities.Design.Import` (Imp). **Kind**: replacement contract (framework §2.6.2), exactly one
implementation per shell, **no default**. **Implementation**: `Elsa3.Activities.Design.Import.Publishing`
(feature `Elsa3ImportActivitiesPublishing`, depends on `Elsa3ImportJsonActivities` and `WorkflowsPublishingApi`).

```csharp
namespace Elsa3.Activities.Design.Import.Contracts;

/// Publishes one imported reusable activity version from its draft through the host's regular publish path.
public interface IReusableActivityImportPublisher
{
    ValueTask<ReusableActivityImportPublishOutcome> PublishAsync(
        ReusableActivityImportPublishRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReusableActivityImportPublishRequest(
    string DraftId,
    int DraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version,                 // "{n}.0.0"
    string RequestedVersionId,      // the plan's deterministic id
    string IdempotencyKey,          // "import:{receiptId}:{versionId}"
    string? TenantId);              // the import's tenant; must equal the publisher's

public sealed record ReusableActivityImportPublishOutcome(
    bool Published,
    string? ErrorCode,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);
```

## Behaviour (bridge implementation)

1. Refuse with `activity.tenant.reference-denied` when `request.TenantId` differs from
   `IActivityPublishingAuthorizationContext.TenantId`. Nothing is called.
2. Call `IActivityDefinitionPublisher.PreflightAsync(new(DraftId, DraftRevision, ExpectedDefinitionHeadVersionId)
   { Version = Version })`.
   - Not publishable → `Published = false` with the preflight diagnostics and their first error code.
3. Call `PublishReviewedAsync(new(DraftId, DraftRevision, ExpectedDefinitionHeadVersionId, Version, preflight.ReviewToken,
   IdempotencyKey, RequestedVersionId))`.
   - Receipt `Applied` → `Published = true`.
   - `ActivityPublicationRejectedException` or a non-`Applied` receipt → `Published = false` with its code and
     diagnostics.
4. Never throws for a publication refusal. Infrastructure failures propagate; the publish phase records them as
   `Refused` with `activity.publication.failed`.

## Rules for callers (the publish phase)

- Called only after the import commit succeeded, never inside its transaction.
- Called in dependency order, then ascending version.
- Not called for a version that is already published or whose dependency is not published.
- If no implementation is registered, the apply refuses before commit with `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`
  whenever the selection contains a reusable version.

## `PublishActivityDefinitionRequest` change (Elsa.Workflows.Publishing.Api)

```csharp
public sealed record PublishActivityDefinitionRequest(
    string DraftId,
    int ExpectedDraftRevision,
    string? ExpectedDefinitionHeadVersionId,
    string Version,
    string ReviewToken,
    string IdempotencyKey,
    string? RequestedVersionId = null);   // new
```

- Absent: unchanged behaviour (SC-006).
- Present: blank → `RequestInvalid`. In use by another version, or already published → `PublicationConflict`
  (existing).
- Part of `ActivityPublicationRequestFingerprint`. A replay under the same idempotency key with a different value →
  `activity.publication.idempotency-conflict`.
- Not exposed on the REST publish endpoint's body. Studio and REST keep choosing ids as today (spec Out of Scope).
