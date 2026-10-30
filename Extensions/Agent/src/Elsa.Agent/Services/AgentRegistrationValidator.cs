using CShells.Lifecycle;
using Elsa.Agent.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

/// <summary>Checks replacement metadata at shell activation and before default authorized paths resolve.</summary>
internal sealed class AgentRegistrationValidator(IServiceCollection registrations) : IShellInitializer
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public void Validate()
    {
        var count = registrations.Count(descriptor => descriptor.ServiceType == typeof(IAgentToolAuthorizer));
        if (count != 1)
            throw new InvalidOperationException($"Agent replacement contract '{typeof(IAgentToolAuthorizer).FullName}' requires exactly one registration, but found {count}. Use services.Replace(...) to replace its existing implementation.");
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate();
        return Task.CompletedTask;
    }

    internal static T Create<T>(IServiceProvider services) where T : class
    {
        services.GetRequiredService<AgentRegistrationValidator>().Validate();
        return ActivatorUtilities.CreateInstance<T>(services);
    }
}
