using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Weaver.Workflows.Services;
using Elsa.Weaver.Services;

namespace Elsa.Weaver.Tests;

public sealed class WeaverProviderRegistryTests
{
    [Fact]
    public void No_provider_means_no_active_harness()
    {
        var registry = new DefaultWeaverProviderRegistry([]);

        Assert.Null(registry.Active);
    }

    [Fact]
    public void One_provider_is_the_active_harness()
    {
        var provider = new DeterministicWeaverProvider();
        var registry = new DefaultWeaverProviderRegistry([provider]);

        Assert.Same(provider, registry.Active);
    }

    [Fact]
    public void More_than_one_provider_fails_fast_with_both_ids()
    {
        var ex = Assert.Throws<WeaverHarnessConflictException>(() =>
            new DefaultWeaverProviderRegistry([new DeterministicWeaverProvider(), new DeterministicWorkflowWeaverProvider()]));

        Assert.Contains(DeterministicWeaverProvider.Id, ex.ProviderIds);
        Assert.Contains(DeterministicWorkflowWeaverProvider.Id, ex.ProviderIds);
        Assert.Contains("Enable exactly one", ex.Message);
    }
}
