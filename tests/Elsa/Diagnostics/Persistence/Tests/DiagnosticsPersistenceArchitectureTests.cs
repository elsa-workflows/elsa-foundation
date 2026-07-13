using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPersistenceArchitectureTests
{
    [Fact]
    public void Diagnostics_core_and_shared_lifecycle_have_no_Groundwork_references()
    {
        var assemblies = new[]
        {
            typeof(IStructuredLogStore).Assembly,
            typeof(IOpenTelemetryStore).Assembly,
            typeof(DiagnosticsPersistenceRegistration).Assembly
        };

        foreach (var assembly in assemblies)
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Groundwork", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Replacement_helper_leaves_exactly_one_store_and_one_shared_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestStore, FirstStore>();
        services.AddSingleton<ITestStore, SecondStore>();

        services.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITestStore));
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ITestStore>(), provider.GetRequiredService<ReplacementStore>());
    }

    private interface ITestStore;
    private sealed class FirstStore : ITestStore;
    private sealed class SecondStore : ITestStore;
    private sealed class ReplacementStore : ITestStore;
}
