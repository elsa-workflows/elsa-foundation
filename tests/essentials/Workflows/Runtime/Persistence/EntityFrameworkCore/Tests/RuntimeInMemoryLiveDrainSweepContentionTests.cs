using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>The live-drain and sweep contention contract on the runtime's default in-memory stores.</summary>
public sealed class RuntimeInMemoryLiveDrainSweepContentionTests : LiveDrainSweepContentionContractTests
{
    protected override void ConfigureStore(IServiceCollection services)
    {
    }
}
