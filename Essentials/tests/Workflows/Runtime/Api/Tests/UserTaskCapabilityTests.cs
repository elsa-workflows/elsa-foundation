using Elsa.Api.Capabilities.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Elsa.Workflows.Runtime.Api.Tests;
public sealed class UserTaskCapabilityTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    public async Task Discovery_requires_opt_in_and_a_qualified_exact_checkpoint_boundary(bool allowVolatile, int checkpoint, bool expected)
    {
        var services = new ServiceCollection(); services.AddLogging();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.Configure<UserTaskOptions>(options => options.AllowVolatileTasksForDevelopment = allowVolatile);
        if (checkpoint != 0) services.Replace(ServiceDescriptor.Singleton<IRuntimeCheckpointCommitStore>(new LegacyCheckpoint(checkpoint == 2)));
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
        var source = Assert.Single(scope.ServiceProvider.GetServices<IApiCapabilitySource>(), item => item.GetType().Name == "UserTaskCapabilitySource");
        var capabilities = await source.GetCapabilitiesAsync();
        Assert.Equal(expected, capabilities.Count != 0);
    }
    private sealed class LegacyCheckpoint(bool supportsExact) : IRuntimeCheckpointCommitStore
    {
        public bool SupportsExactBookmarkResume => supportsExact;
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
