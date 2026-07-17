using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ActivityVersionLifecycleService(
    IActivityDefinitionVersionPublicationStore publications,
    IChangeActivityVersionLifecycleCommand changeLifecycle,
    IActivityAuthoringContext context,
    TimeProvider timeProvider)
{
    public Task<ReusableActivityVersionLifecycleView> RetireAsync(
        RetireReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Retired, command.Reason, cancellationToken);

    public Task<ReusableActivityVersionLifecycleView> RestoreAsync(
        RestoreReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Active, command.Reason, cancellationToken);

    public Task<ReusableActivityVersionLifecycleView> RevokeAsync(
        RevokeReusableActivityVersion command,
        CancellationToken cancellationToken) =>
        ChangeAsync(command.VersionId, command.ExpectedLifecycle, ActivityDefinitionVersionLifecycle.Revoked, command.Reason, cancellationToken);

    private async Task<ReusableActivityVersionLifecycleView> ChangeAsync(
        string versionId,
        ActivityDefinitionVersionLifecycle expectedLifecycle,
        ActivityDefinitionVersionLifecycle targetLifecycle,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ActivityAuthoringException(400, "activity.request.invalid", "Invalid lifecycle request", "A non-empty lifecycle reason is required.");

        var current = await publications.FindAsync(versionId, cancellationToken)
                      ?? throw new ActivityAuthoringException(404, "activity.version.not-found", "Activity version not found", "The requested activity version was not found.");
        if (current.TenantId is not null && !StringComparer.Ordinal.Equals(current.TenantId, context.TenantId))
            throw new ActivityAuthoringException(403, "activity.authorization.denied", "Activity lifecycle change is forbidden", "The requested activity version is outside the caller's tenant scope.");
        if (current.Lifecycle != expectedLifecycle)
            throw StaleLifecycle(current, expectedLifecycle);

        ActivityDefinitionVersionPublication changed;
        try
        {
            changed = await changeLifecycle.ExecuteAsync(
                new(versionId, expectedLifecycle, targetLifecycle, reason),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            var latest = await publications.FindAsync(versionId, cancellationToken) ?? current;
            if (latest.Lifecycle != expectedLifecycle)
                throw StaleLifecycle(latest, expectedLifecycle, exception);
            throw new ActivityAuthoringException(
                409,
                "activity.version.lifecycle-conflict",
                "Activity version lifecycle transition is invalid",
                $"The activity version cannot transition from {latest.Lifecycle} to {targetLifecycle}.",
                innerException: exception);
        }

        return new(changed.DefinitionVersionId, changed.Lifecycle, reason, timeProvider.GetUtcNow());
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
