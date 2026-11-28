using CShells.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Uses the shared runtime database for task journal reads and reusable task forms.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeUserTaskArchiveEntityFrameworkCore",
    DisplayName = "User Task Journal and Forms EF Core Persistence",
    Description = "Reads the durable user task journal and stores reusable form drafts and versions.",
    DependsOn = new object[] { "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence" })]
public sealed class RuntimeUserTaskArchiveEntityFrameworkCoreFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IUserTaskStore) && !descriptor.IsKeyedService))
            throw new InvalidOperationException("The user task journal and forms require a single persistence owner.");
        services.AddScoped<IUserTaskStore>(provider =>
        {
            var context = provider.GetRequiredService<RuntimeDbContext>();
            var access = provider.GetRequiredService<IPersistenceAccessContextAccessor>();
            return provider.GetServices<IActorUserTaskRuntime>().SingleOrDefault() is null
                ? new EfUserTaskArchiveStore(context, access)
                : new EfActorUserTaskJournal(context, access);
        });
    }
}
