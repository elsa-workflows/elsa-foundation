using Elsa.Mediator.Commands;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Requests;
using Microsoft.Extensions.DependencyInjection;
namespace Elsa.Mediator.Tests;

/// <summary>
/// Builds a service provider wired with the mediator core services, mirroring
/// <c>MediatorFeature.ConfigureServices</c> without pulling in the shell feature machinery.
/// </summary>
internal static class MediatorTestHost
{
    public static ServiceProvider Build(Action<IServiceCollection> configureHandlers)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services
            .AddScoped<IRequestSender, RequestSender>()
            .AddScoped<ICommandSender, CommandSender>()
            .AddSingleton<RequestPipeline>()
            .AddSingleton<CommandPipeline>();

        configureHandlers(services);

        return services.BuildServiceProvider();
    }
}
