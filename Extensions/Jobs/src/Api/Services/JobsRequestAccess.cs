using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Jobs.Api.Services;

internal sealed record JobApiActor(JobScope Scope, string SubjectId);

internal sealed class JobsRequestAccess(IHttpContextAccessor http, IOptions<JobsApiOptions> options,
    NormalizedPrincipalValidator? validator = null, ICurrentSubjectPermissionAuthorizer? permissions = null)
{
    internal JobApiActor Actor()
    {
        if (validator is null || permissions is null)
            throw new JobsApiException(503, "jobs.identity.unavailable", "Current Jobs authorization is unavailable.");
        if (http.HttpContext is not { } context || !validator.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new JobsApiException(401, "jobs.unauthorized", "Sign in to use Jobs operations.");
        var subjects = principal.FindAll(IdentityClaimTypes.UserId).Select(x => x.Value).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(x => x.Value).ToArray();
        if (subjects.Length != 1 || tenants.Length != 1 || !Valid(subjects[0]) || !Valid(tenants[0]))
            throw new JobsApiException(403, "jobs.forbidden", "A canonical tenant user is required.");
        if (!Valid(options.Value.PersistencePartition))
            throw new JobsApiException(503, "jobs.scope.unavailable", "The host's Jobs partition is unavailable.");
        return new(new(options.Value.PersistencePartition, tenants[0]), subjects[0]);
    }

    internal async ValueTask<bool> CanAsync(JobApiActor actor, string permission, JobApiResource? resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Actor();
        if (current != actor || resource is not null && resource.TenantId != actor.Scope.TenantId)
            throw new JobsApiException(403, "jobs.forbidden", "This Jobs operation is not permitted.");
        var decision = await permissions!.AuthorizeAsync(new(actor.Scope.TenantId!, actor.SubjectId, permission, resource), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision.Status == CurrentSubjectPermissionStatus.Unsupported)
            throw new JobsApiException(503, "jobs.identity.unavailable", "Current Jobs authorization is unavailable.");
        return decision.Status == CurrentSubjectPermissionStatus.Granted;
    }

    internal async ValueTask RequireAsync(JobApiActor actor, string permission, JobApiResource? resource,
        CancellationToken cancellationToken, bool conceal = false)
    {
        if (!await CanAsync(actor, permission, resource, cancellationToken))
            throw conceal
                ? new JobsApiException(404, "jobs.not-found", "The requested Jobs resource was not found.")
                : new JobsApiException(403, "jobs.forbidden", "This Jobs operation is not permitted.");
    }

    private static bool Valid(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
}

internal sealed class JobsApiException(int status, string code, string summary) : Exception(summary)
{
    internal int Status { get; } = status;
    internal string Code { get; } = code;
}
