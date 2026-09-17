using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>The waited-child store contract on the runtime's default in-memory stores.</summary>
public sealed class RuntimeInMemoryDispatchWorkflowTests : DispatchWorkflowStoreContractTests
{
    protected override void ConfigureStore(IServiceCollection services)
    {
    }
}
