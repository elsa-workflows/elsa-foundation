using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary><see cref="LiveDrainSweepContentionContract"/> on the runtime's default in-memory stores.</summary>
public sealed class RuntimeInMemoryLiveDrainSweepContentionTests
{
    public static TheoryData<string> Scenarios => LiveDrainSweepContentionContract.Scenarios;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task In_memory(string scenario) =>
        LiveDrainSweepContentionContract.RunAsync(scenario, LiveDrainSweepContentionContract.InMemory);
}
