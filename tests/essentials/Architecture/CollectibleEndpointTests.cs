using Elsa.Api.Compatibility.Testing.Collectibility;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class CollectibleEndpointTests
{
    [Fact]
    public void Repeated_collectible_endpoint_cycles_produce_weak_reference_evidence()
    {
        var evidence = Enumerable.Range(0, 10)
            .Select(_ => CollectibleEndpointFixture.Create().VerifyCollection())
            .ToArray();

        Assert.Equal(10, evidence.Length);
        Assert.All(evidence, item =>
        {
            Assert.True(item.Collected, item.Diagnostic);
            Assert.Equal(RetentionStage.Clean, item.Stage);
            Assert.Null(item.Diagnostic);
            Assert.False(item.LoadContext.IsAlive);
            Assert.False(item.Assembly.IsAlive);
            Assert.False(item.EndpointType.IsAlive);
        });
    }
}
