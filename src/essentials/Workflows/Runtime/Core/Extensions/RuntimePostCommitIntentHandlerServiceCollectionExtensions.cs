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
        where THandler : class, IRuntimePostCommitIntentHandler =>
        services.AddRuntimePostCommitIntentHandler<THandler>(intentKind, RuntimePostCommitRetryPolicy.None);

    /// <summary>Contributes the scoped handler and durable retry policy for one stable intent kind.</summary>
    public static IServiceCollection AddRuntimePostCommitIntentHandler<THandler>(
        this IServiceCollection services,
        string intentKind,
        RuntimePostCommitRetryPolicy retryPolicy)
        where THandler : class, IRuntimePostCommitIntentHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        var handlerType = typeof(THandler);
        var matchingKind = services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimePostCommitIntentHandlerContribution>()
            .Where(contribution => StringComparer.Ordinal.Equals(contribution.IntentKind, intentKind))
            .ToArray();

        if (matchingKind.Any(contribution =>
                contribution.HandlerType != handlerType ||
                !contribution.RetryPolicy.IsEquivalentTo(retryPolicy)))
        {
            var contributionIdentities = matchingKind
                .Select(ContributionIdentity)
                .Append(ContributionIdentity(new RuntimePostCommitIntentHandlerContribution(intentKind, handlerType, retryPolicy)))
                .Distinct()
                .Order(StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Runtime post-commit intent kind '{intentKind}' has conflicting handler or retry-policy contributions: {string.Join(", ", contributionIdentities)}.");
        }

        services.TryAddScoped<THandler>();
        if (matchingKind.Length == 0)
            services.AddSingleton(new RuntimePostCommitIntentHandlerContribution(intentKind, handlerType, retryPolicy));

        return services;
    }

    private static string HandlerIdentity(Type handlerType) => handlerType.FullName ?? handlerType.Name;

    private static string ContributionIdentity(RuntimePostCommitIntentHandlerContribution contribution) =>
        $"{HandlerIdentity(contribution.HandlerType)} " +
        $"[maxAttempts={contribution.RetryPolicy.MaxAttempts}, delay={contribution.RetryPolicy.Delay}, " +
        $"retryUntilAcknowledged={contribution.RetryPolicy.RetryUntilAcknowledged}]";
}
