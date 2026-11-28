using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

internal static class WorkflowTestRunTransportServiceCollectionExtensions
{
    internal static void AddWorkflowTestRunTransport(this IServiceCollection services)
    {
        services.TryAddSingleton<MemoryTestRunTargetStore>();
        services.TryAddSingleton<ITestRunTargetStore>(provider => provider.GetRequiredService<MemoryTestRunTargetStore>());
        services.TryAddSingleton<TestRunHeldDispatches>();
        services.TryAddScoped<ITestRunTargetDispatcher, TestRunTargetDispatcher>();
        services.TryAddScoped<ITestRunTargetCleaner, TestRunTargetCleaner>();
    }
}
