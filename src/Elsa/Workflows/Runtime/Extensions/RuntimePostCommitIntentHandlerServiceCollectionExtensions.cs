using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

public static class RuntimePostCommitIntentHandlerServiceCollectionExtensions
{
    /// <summary>Contributes the scoped handler for one stable runtime post-commit intent kind.</summary>
    public static IServiceCollection AddRuntimePostCommitIntentHandler<THandler>(
        this IServiceCollection services,
        string intentKind)
        where THandler : class, IRuntimePostCommitIntentHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);

        var handlerType = typeof(THandler);
        var matchingKind = services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimePostCommitIntentHandlerContribution>()
            .Where(contribution => StringComparer.Ordinal.Equals(contribution.IntentKind, intentKind))
            .ToArray();

        if (matchingKind.Any(contribution => contribution.HandlerType != handlerType))
        {
            var handlerIdentities = matchingKind
                .Select(contribution => contribution.HandlerType)
                .Append(handlerType)
                .Distinct()
                .Select(HandlerIdentity)
                .Order(StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Runtime post-commit intent kind '{intentKind}' has conflicting handler contributions: {string.Join(", ", handlerIdentities)}.");
        }

        services.TryAddScoped<THandler>();
        if (matchingKind.Length == 0)
            services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(intentKind, handlerType));

        return services;
    }

    private static string HandlerIdentity(Type handlerType) => handlerType.FullName ?? handlerType.Name;
}
