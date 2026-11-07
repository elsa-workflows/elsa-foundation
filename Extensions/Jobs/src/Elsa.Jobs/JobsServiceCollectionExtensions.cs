using Elsa.Jobs.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs;

/// <summary>Ordinary .NET composition without workflow, Studio or ASP.NET dependencies.</summary>
public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddElsaJobs(this IServiceCollection services, Action<JobsOptions>? configure = null) =>
        JobRegistration.AddServices(services, configure);

    public static IServiceCollection AddJobHandler<THandler, TInput, TResult>(this IServiceCollection services, JobHandlerSupport support)
        where THandler : class, IJobHandler<TInput, TResult> =>
        JobRegistration.AddHandler<THandler, TInput, TResult>(services, support);
}
