using CShells.Features;
using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator;

[ShellFeature(
    name: "Mediator"
)]
public class MediatorFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Requests + commands — synchronous, awaited, single-handler dispatch.
        services
            .AddScoped<IRequestSender, RequestSender>()
            .AddScoped<ICommandSender, CommandSender>()
            .AddSingleton<IRequestPipeline, RequestPipeline>()
            .AddSingleton<ICommandPipeline, CommandPipeline>();
    }
}
