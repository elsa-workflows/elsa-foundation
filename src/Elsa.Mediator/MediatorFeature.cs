using CShells.Features;
using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.DomainEvents;
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
        services
            .AddScoped<IRequestSender, RequestSender>()
            .AddScoped<ICommandSender, CommandSender>()
            .AddScoped<IDomainEventSender, DomainEventSender>()
            .AddSingleton<IRequestPipeline, RequestPipeline>()
            .AddSingleton<ICommandPipeline, CommandPipeline>()
            .AddSingleton<IDomainEventPipeline, DomainEventPipeline>();
    }
}
