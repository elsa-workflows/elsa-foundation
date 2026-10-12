using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivitiesRuntimeFeatureTests
{
    // Kept beside the composition test: scope validation. CShells builds shell providers without ValidateScopes,
    // so only this test catches a captive dependency in the activator chain (the resolver consumes a scoped store).
    [Fact]
    public async Task ComposesTheActivatorWithItsSecretInputResolver()
    {
        // The collaborator is a required constructor dependency, so the activator resolves only when the feature registers it.
        var services = new ServiceCollection().AddWorkflowRuntime();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ActivityActivator>(scope.ServiceProvider.GetRequiredService<IActivityActivator>());
        scope.ServiceProvider.GetRequiredService<ActivitySecretInputResolver>();
    }
}
