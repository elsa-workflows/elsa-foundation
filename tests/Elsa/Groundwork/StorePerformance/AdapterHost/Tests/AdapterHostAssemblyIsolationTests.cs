using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class AdapterHostAssemblyIsolationTests
{
    [Fact]
    public void Disables_parallel_execution_for_process_global_explain_capture()
    {
        var collectionBehavior = typeof(AdapterHostAssemblyIsolationTests).Assembly
            .GetCustomAttributes(typeof(CollectionBehaviorAttribute), inherit: false)
            .OfType<CollectionBehaviorAttribute>()
            .SingleOrDefault();

        Assert.NotNull(collectionBehavior);
        Assert.True(collectionBehavior!.DisableTestParallelization);
    }
}
