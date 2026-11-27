using Elsa.Diagnostics.StructuredLogs.Sources;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class LocalStructuredLogSourceProviderTests
{
    [Fact]
    public void LocalSourceCarriesHostShape()
    {
        var provider = new LocalStructuredLogSourceProvider("elsa-server", "Elsa.Server");

        var source = provider.GetLocalSource();

        Assert.Equal("elsa-server", source.Id);
        Assert.Equal("Elsa.Server", source.DisplayName);
        Assert.Equal("elsa-server", source.ServiceName);
        Assert.False(string.IsNullOrWhiteSpace(source.MachineName));
        Assert.NotNull(source.ProcessId);
    }

    [Fact]
    public void KnownSourcesContainsTheLocalSource()
    {
        var provider = new LocalStructuredLogSourceProvider("svc");

        var known = provider.GetKnownSources();

        Assert.Single(known);
        Assert.Equal(provider.GetLocalSource(), known[0]);
    }

    [Fact]
    public void DefaultsToProcessNameWhenServiceNameMissing()
    {
        var provider = new LocalStructuredLogSourceProvider();

        Assert.False(string.IsNullOrWhiteSpace(provider.GetLocalSource().ServiceName));
    }
}
