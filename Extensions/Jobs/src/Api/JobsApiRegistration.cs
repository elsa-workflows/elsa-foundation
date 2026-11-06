using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Jobs.Api;

public static class JobsApiRegistration
{
    /// <summary>Registers Jobs operations over the host's selected command/query authority and current identity directory.</summary>
    public static IServiceCollection AddJobsApi(this IServiceCollection services, Action<JobsApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<JobsApiOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddElsaEndpoints();
        services.AddApiCapabilities();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.jobs", 1,
            [new("jobs", "jobs"), new("queues", "jobs/queues"), new("schedules", "jobs/schedules"),
             new("holds", "jobs/holds"), new("capacity", "jobs/capacity"), new("access", "jobs/access"), new("operations", "jobs/operations")], "JobsApi"));
        services.AddPermissionContributor<JobsPermissionContributor>();
        services.TryAddScoped<JobsRequestAccess>(); services.TryAddScoped<JobsPageCursors>(); services.TryAddScoped<JobsOperations>();
        return services;
    }
}
