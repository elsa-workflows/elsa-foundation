using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Attention.Api;

public static class AttentionServiceCollectionExtensions
{
    public static IServiceCollection AddAttentionCore(
        this IServiceCollection services,
        Action<AttentionOptions>? configure = null)
    {
        var options = services.AddOptions<AttentionOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.TryAddScoped<IAttentionPermissionEvaluator, FoundationAttentionPermissionEvaluator>();
        services.TryAddScoped<IAttentionContributorRegistry>(provider =>
            new AttentionContributorRegistry(provider.GetServices<IAttentionContributor>()));
        services.TryAddScoped<IAttentionAggregationService>(provider =>
            new AttentionAggregationService(
                provider.GetRequiredService<IAttentionContributorRegistry>(),
                provider.GetRequiredService<IAttentionPermissionEvaluator>(),
                provider.GetRequiredService<IOptions<AttentionOptions>>().Value));
        return services;
    }

    private sealed class FoundationAttentionPermissionEvaluator(IPermissionEvaluator evaluator)
        : IAttentionPermissionEvaluator
    {
        public async ValueTask<bool> IsAllowedAsync(
            System.Security.Claims.ClaimsPrincipal principal,
            string permission,
            string? tenantId,
            CancellationToken cancellationToken = default)
        {
            var result = await evaluator.EvaluateAsync(
                new PermissionEvaluationContext(principal, permission, tenantId),
                cancellationToken);
            return result.Succeeded;
        }
    }
}
