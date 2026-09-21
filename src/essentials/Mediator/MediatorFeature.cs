using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Mediator")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "Mediator",
    DisplayName = "Mediator",
    Description = "Provides request and command dispatch pipelines for synchronous in-process mediation."
)]
public class MediatorFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Requests + commands — synchronous, awaited, single-handler dispatch over the shared pipeline.
        services
            .AddScoped<IRequestSender, RequestSender>()
            .AddScoped<ICommandSender, CommandSender>()
            .AddSingleton<RequestPipeline>()
            .AddSingleton<CommandPipeline>();
    }
}
