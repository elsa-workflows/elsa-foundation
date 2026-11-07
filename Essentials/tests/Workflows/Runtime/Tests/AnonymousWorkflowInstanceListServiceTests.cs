using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// #1409's remaining server-side filter: the list operation uses the HTTP inspection context, which
/// fail-closes without a trusted principal. The EF history query itself is unscoped by authority.
/// </summary>
public sealed class AnonymousWorkflowInstanceListServiceTests
{
    [Fact]
    public async Task Untrusted_http_inspection_hides_persisted_runs_that_allow_all_still_returns()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        var activities = new InMemoryActivityExecutionStateStore();
        var incidents = new InMemoryIncidentStateStore();
        await workflows.SaveAsync(Execution("wf-anonymous"));

        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };
        var untrusted = new HttpContextActivityExecutionInspectionAuthorizationContext(
            accessor,
            provider.GetRequiredService<IPermissionAuthorizationService>(),
            provider.GetRequiredService<NormalizedPrincipalValidator>());

        var hidden = await new WorkflowInstanceListService(workflows, activities, incidents, untrusted)
            .ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);
        var visible = await new WorkflowInstanceListService(
                workflows,
                activities,
                incidents,
                new AllowAllActivityExecutionInspectionAuthorizationContext())
            .ListAsync(new ListWorkflowInstances(null, null, null, 10), CancellationToken.None);

        Assert.Empty(hidden.Items);
        Assert.Equal(0, hidden.TotalCount);
        Assert.Equal("wf-anonymous", Assert.Single(visible.Items).WorkflowExecutionId);
    }

    private static WorkflowExecutionState Execution(string id) =>
        new(
            id,
            new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Completed,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            new Dictionary<string, string>());
}
