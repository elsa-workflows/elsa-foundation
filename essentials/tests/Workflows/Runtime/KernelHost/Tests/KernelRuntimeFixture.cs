using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

internal static class KernelRuntimeFixture
{
    internal static void Add(IServiceCollection services, string provider, string connection)
    {
        var signingKey = new string('r', 32);
        WorkflowPublicationDefaults.Add(services);
        services.TryAddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
        new RuntimeArtifactsEntityFrameworkCoreFeature
        {
            Provider = provider, ConnectionString = connection, RecoveryContinuationSigningKey = signingKey
        }.ConfigureServices(services);
        new RuntimePublicationEntityFrameworkCoreFeature().ConfigureServices(services);
        new RuntimeWorkflowTestScopeEntityFrameworkCoreFeature
        {
            Provider = provider, ConnectionString = connection, RecoveryContinuationSigningKey = signingKey
        }.ConfigureServices(services);
    }

    internal static Task<bool> HasClassicExecutionAsync(IServiceProvider services, string id, CancellationToken token)
    {
        var encoded = EfRelationalIdentity.Encode(id);
        return services.GetRequiredService<RuntimeDbContext>().WorkflowExecutionStates.AnyAsync(row => row.WorkflowExecutionId == encoded, token);
    }
}
