using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.TestScopes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Process-local provider retained only for public direct-construction compatibility.</summary>
internal static class TestRunCompatibilityScope
{
    private static readonly ServiceProvider Provider;

    static TestRunCompatibilityScope()
    {
        Store = new InMemoryWorkflowTestScopeLifecycleStore();
        Provider = new ServiceCollection()
            .AddSingleton<IWorkflowTestScopeStore>(Store)
            .BuildServiceProvider();
    }

    public static IWorkflowTestScopeStore Store { get; }
    public static IServiceScopeFactory ScopeFactory => Provider.GetRequiredService<IServiceScopeFactory>();
}
