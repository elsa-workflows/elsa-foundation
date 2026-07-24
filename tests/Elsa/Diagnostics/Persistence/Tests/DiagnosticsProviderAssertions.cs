using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public static class DiagnosticsProviderAssertions
{
    public static void IsReady(
        DiagnosticsProviderLease lease,
        params DiagnosticsProviderCapability[] requiredCapabilities)
    {
        Assert.True(lease.IsReady, lease.Failure ?? $"{lease.Provider} is not ready.");
        Assert.False(string.IsNullOrWhiteSpace(lease.ConnectionString));
        Assert.False(string.IsNullOrWhiteSpace(lease.DatabaseName));
        foreach (var capability in requiredCapabilities)
            Assert.Contains(capability, lease.Capabilities);
    }
}
