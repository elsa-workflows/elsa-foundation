using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ActivityVersionLifecycleService(
    IActivityDefinitionVersionPublicationStore publications,
    IActivityDefinitionAuthoringStore authoringStore,
    IChangeActivityVersionLifecycleCommand changeLifecycle,
    IActivityAuthoringContext context,
    TimeProvider timeProvider)
{
    public Task<ReusableActivityVersionLifecycleView> RetireAsync(
        RetireReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Retired, command.Reason, command.RecommendationDecision, cancellationToken);

    public Task<ReusableActivityVersionLifecycleView> RestoreAsync(
        RestoreReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Active, command.Reason, null, cancellationToken);

    public Task<ReusableActivityVersionLifecycleView> RevokeAsync(
        RevokeReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Revoked, command.Reason, command.RecommendationDecision, cancellationToken);

    private async Task<ReusableActivityVersionLifecycleView> ChangeAsync(
        string versionId,
        ActivityDefinitionVersionLifecycle expectedLifecycle,
        ActivityDefinitionVersionLifecycle targetLifecycle,
        string reason,
        ActivityRecommendationDecision? recommendationDecision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ActivityAuthoringException(400, ActivityErrorCodes.RequestInvalid, "Invalid lifecycle request", "A non-empty lifecycle reason is required.");

        var current = await publications.FindAsync(versionId, cancellationToken)
                      ?? throw new ActivityAuthoringException(404, "activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
        if (current.TenantId is not null && !StringComparer.Ordinal.Equals(current.TenantId, context.TenantId))
            throw new ActivityAuthoringException(403, ActivityErrorCodes.AuthorizationDenied, "Activity lifecycle change is forbidden", "The requested activity version is outside the caller's tenant scope.");
        if (current.Lifecycle != expectedLifecycle)
            throw StaleLifecycle(current, expectedLifecycle);
        var authoring = await authoringStore.FindAsync(current.DefinitionId, cancellationToken)
                        ?? throw new ActivityAuthoringException(404, "activity.definition.not-found", "Activity definition not found", "The activity definition authoring state was not found.");
        if (authoring.TenantId is not null && !StringComparer.Ordinal.Equals(authoring.TenantId, context.TenantId))
            throw new ActivityAuthoringException(403, ActivityErrorCodes.AuthorizationDenied, "Activity lifecycle change is forbidden", "The requested activity definition is outside the caller's tenant scope.");
        await ValidateRecommendationDecisionAsync(current, authoring, targetLifecycle, recommendationDecision, cancellationToken);

        ActivityDefinitionVersionPublication changed;
        try
        {
            changed = await changeLifecycle.ExecuteAsync(
                new(versionId, expectedLifecycle, targetLifecycle, reason, context.TenantId, recommendationDecision),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            var latest = await publications.FindAsync(versionId, cancellationToken) ?? current;
            if (latest.Lifecycle != expectedLifecycle)
                throw StaleLifecycle(latest, expectedLifecycle, exception);
            var latestAuthoring = await authoringStore.FindAsync(current.DefinitionId, cancellationToken) ?? authoring;
            await ValidateRecommendationDecisionAsync(latest, latestAuthoring, targetLifecycle, recommendationDecision, cancellationToken, exception);
            throw new ActivityAuthoringException(
                409,
                "activity.version.lifecycle-conflict",
                "Activity version lifecycle transition is invalid",
                $"The activity version cannot transition from {latest.Lifecycle} to {targetLifecycle}.",
                innerException: exception);
        }

        return new(changed.DefinitionVersionId, changed.Lifecycle, reason, timeProvider.GetUtcNow());
    }

    private async Task ValidateRecommendationDecisionAsync(
        ActivityDefinitionVersionPublication current,
        ActivityDefinitionAuthoringState authoring,
        ActivityDefinitionVersionLifecycle targetLifecycle,
        ActivityRecommendationDecision? decision,
        CancellationToken cancellationToken,
        Exception? inner = null)
    {
        var invalidatesRecommendation = targetLifecycle is ActivityDefinitionVersionLifecycle.Retired or ActivityDefinitionVersionLifecycle.Revoked &&
                                        StringComparer.Ordinal.Equals(authoring.RecommendedVersionId, current.DefinitionVersionId);
        if (!invalidatesRecommendation)
        {
            if (decision is not null)
                throw new ActivityAuthoringException(400, "activity.definition.recommendation-unexpected", "Unexpected recommendation decision", "A recommendation decision is accepted only when retiring or revoking the recommended version.", innerException: inner);
            return;
        }
        if (decision is null)
            throw new ActivityAuthoringException(409, "activity.definition.recommendation-required", "Recommendation decision required", "Retiring or revoking the recommended version requires an explicit clear or replacement decision.", innerException: inner);
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, decision.ExpectedDefinitionHeadVersionId))
            throw new ActivityAuthoringException(409, "activity.definition.stale-head", "Activity definition head is stale", "The activity definition head changed after review.", innerException: inner);
        if (!StringComparer.Ordinal.Equals(authoring.RecommendedVersionId, decision.ExpectedRecommendedVersionId))
            throw new ActivityAuthoringException(409, "activity.definition.stale-recommendation", "Activity definition recommendation is stale", "The activity definition recommendation changed after review.", innerException: inner);

        if (decision.Disposition == ActivityRecommendationDisposition.Clear)
        {
            if (decision.ReplacementVersionId is not null || decision.ExpectedReplacementLifecycle is not null)
                throw new ActivityAuthoringException(400, ActivityErrorCodes.RequestInvalid, "Invalid recommendation decision", "A clear decision cannot include a replacement version.", innerException: inner);
            return;
        }
        if (decision.ReplacementVersionId is null || decision.ExpectedReplacementLifecycle is null)
            throw new ActivityAuthoringException(400, ActivityErrorCodes.RequestInvalid, "Invalid recommendation decision", "A replacement decision requires an exact version and expected lifecycle.", innerException: inner);
        var replacement = await publications.FindAsync(decision.ReplacementVersionId, cancellationToken);
        if (replacement is null ||
            !StringComparer.Ordinal.Equals(replacement.DefinitionId, current.DefinitionId) ||
            !StringComparer.Ordinal.Equals(replacement.TenantId, current.TenantId))
            throw new ActivityAuthoringException(404, "activity.version.not-found", "Activity version not found", "The replacement activity version was not found for this definition.", innerException: inner);
        if (replacement.Lifecycle != decision.ExpectedReplacementLifecycle || replacement.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            throw new ActivityAuthoringException(409, "activity.version.stale-lifecycle", "Activity version lifecycle is stale", "The replacement activity version is not active under the reviewed lifecycle precondition.", innerException: inner);
    }

    private static ActivityAuthoringException StaleLifecycle(
        ActivityDefinitionVersionPublication version,
        ActivityDefinitionVersionLifecycle expected,
        Exception? inner = null) => new(
        409,
        "activity.version.stale-lifecycle",
        "Activity version lifecycle is stale",
        "The activity version lifecycle changed after the submitted state was read.",
        [new ActivityDiagnostic(
            "activity.version.stale-lifecycle",
            ActivityDiagnosticSeverity.Error,
            $"Expected lifecycle {expected} but the current lifecycle is {version.Lifecycle}.",
            new("ActivityVersion", version.DefinitionVersionId, version.DefinitionId, version.DefinitionVersionId),
            Remediation: "Reload the activity version and reconsider the lifecycle transition.",
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["expectedLifecycle"] = expected.ToString(),
                ["actualLifecycle"] = version.Lifecycle.ToString()
            })],
        inner);
}

public sealed class RetireReusableActivityVersionHandler(ActivityVersionLifecycleService service)
    : ICommandHandler<RetireReusableActivityVersion, ReusableActivityVersionLifecycleView>
{
    public Task<ReusableActivityVersionLifecycleView> Handle(RetireReusableActivityVersion command, CancellationToken cancellationToken) =>
        service.RetireAsync(command, cancellationToken);
}

public sealed class RestoreReusableActivityVersionHandler(ActivityVersionLifecycleService service)
    : ICommandHandler<RestoreReusableActivityVersion, ReusableActivityVersionLifecycleView>
{
    public Task<ReusableActivityVersionLifecycleView> Handle(RestoreReusableActivityVersion command, CancellationToken cancellationToken) =>
        service.RestoreAsync(command, cancellationToken);
}

public sealed class RevokeReusableActivityVersionHandler(ActivityVersionLifecycleService service)
    : ICommandHandler<RevokeReusableActivityVersion, ReusableActivityVersionLifecycleView>
{
    public Task<ReusableActivityVersionLifecycleView> Handle(RevokeReusableActivityVersion command, CancellationToken cancellationToken) =>
        service.RevokeAsync(command, cancellationToken);
}
