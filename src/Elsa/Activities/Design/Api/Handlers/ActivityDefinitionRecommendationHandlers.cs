using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ActivityDefinitionRecommendationService(
    IActivityDefinitionAuthoringStore authoringStore,
    IActivityDefinitionVersionPublicationStore publicationStore,
    ISetActivityDefinitionRecommendationCommand setRecommendation,
    IActivityAuthoringContext context,
    TimeProvider timeProvider)
{
    public async Task<ActivityDefinitionRecommendationView> SetAsync(
        SetRecommendedReusableActivityVersion command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw Invalid("A non-empty recommendation reason is required.");
        var hasTarget = command.RecommendedVersionId is not null;
        var hasLifecyclePrecondition = command.ExpectedRecommendedVersionLifecycle is not null;
        if (hasTarget != hasLifecyclePrecondition)
            throw Invalid("A recommendation target and its expected lifecycle must be supplied together.");

        var authoring = await authoringStore.FindAsync(command.DefinitionId, cancellationToken)
                        ?? throw NotFound();
        EnsureVisible(authoring.TenantId);
        EnsureExpected(authoring, command);
        if (command.RecommendedVersionId is not null)
            await EnsureTargetAsync(command, authoring, cancellationToken);

        ActivityDefinitionAuthoringState changed;
        var now = timeProvider.GetUtcNow();
        try
        {
            changed = await setRecommendation.ExecuteAsync(new(
                command.DefinitionId,
                context.TenantId,
                command.ExpectedDefinitionHeadVersionId,
                command.ExpectedRecommendedVersionId,
                command.RecommendedVersionId,
                command.ExpectedRecommendedVersionLifecycle,
                now), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            var latest = await authoringStore.FindAsync(command.DefinitionId, cancellationToken);
            if (latest is null)
                throw NotFound(exception);
            EnsureExpected(latest, command, exception);
            if (command.RecommendedVersionId is not null)
                await EnsureTargetAsync(command, latest, cancellationToken, exception);
            throw new ActivityAuthoringException(409, "activity.definition.recommendation-conflict", "Activity recommendation conflict", "The recommendation could not be changed under the reviewed preconditions.", innerException: exception);
        }

        return new(changed.DefinitionId, changed.HeadVersionId, changed.RecommendedVersionId, now, command.Reason);
    }

    private async Task EnsureTargetAsync(
        SetRecommendedReusableActivityVersion command,
        ActivityDefinitionAuthoringState authoring,
        CancellationToken cancellationToken,
        Exception? inner = null)
    {
        var target = await publicationStore.FindAsync(command.RecommendedVersionId!, cancellationToken);
        if (target is null ||
            !StringComparer.Ordinal.Equals(target.DefinitionId, command.DefinitionId) ||
            !StringComparer.Ordinal.Equals(target.TenantId, authoring.TenantId))
            throw NotFound(inner);
        EnsureVisible(target.TenantId);
        if (target.Lifecycle != command.ExpectedRecommendedVersionLifecycle || target.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            throw new ActivityAuthoringException(409, "activity.version.stale-lifecycle", "Activity version lifecycle is stale", "The recommendation target is not active under the reviewed lifecycle precondition.", innerException: inner);
    }

    private void EnsureVisible(string? tenantId)
    {
        if (tenantId is not null && !StringComparer.Ordinal.Equals(tenantId, context.TenantId))
            throw new ActivityAuthoringException(403, "activity.tenant.reference-denied", "Activity reference denied", "The requested activity identity is outside the caller's authorized scope.");
    }

    private static void EnsureExpected(
        ActivityDefinitionAuthoringState authoring,
        SetRecommendedReusableActivityVersion command,
        Exception? inner = null)
    {
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, command.ExpectedDefinitionHeadVersionId))
            throw new ActivityAuthoringException(409, "activity.definition.stale-head", "Activity definition head is stale", "The activity definition head changed after review.", innerException: inner);
        if (!StringComparer.Ordinal.Equals(authoring.RecommendedVersionId, command.ExpectedRecommendedVersionId))
            throw new ActivityAuthoringException(409, "activity.definition.stale-recommendation", "Activity definition recommendation is stale", "The activity definition recommendation changed after review.", innerException: inner);
    }

    private static ActivityAuthoringException Invalid(string message) =>
        new(400, ActivityErrorCodes.RequestInvalid, "Invalid recommendation request", message);

    private static ActivityAuthoringException NotFound(Exception? inner = null) =>
        new(404, "activity.version.not-found", "Activity version not found", "The requested activity version was not found for this definition.", innerException: inner);
}

public sealed class SetRecommendedReusableActivityVersionHandler(ActivityDefinitionRecommendationService service)
    : ICommandHandler<SetRecommendedReusableActivityVersion, ActivityDefinitionRecommendationView>
{
    public Task<ActivityDefinitionRecommendationView> Handle(SetRecommendedReusableActivityVersion command, CancellationToken cancellationToken) =>
        service.SetAsync(command, cancellationToken);
}
