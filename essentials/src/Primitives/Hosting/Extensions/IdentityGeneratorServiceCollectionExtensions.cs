using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Primitives.Hosting.Extensions;

/// <summary>
/// Registration helpers for selecting the <see cref="IIdentityGenerator"/> implementation used to generate entity ids.
/// </summary>
public static class IdentityGeneratorServiceCollectionExtensions
{
    /// <summary>
    /// Replaces any registered <see cref="IIdentityGenerator"/> with the selected built-in strategy. Call this after the
    /// persistence feature(s) have registered their defaults so the chosen strategy wins.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="kind">The identifier strategy to use.</param>
    /// <param name="configureSnowflake">
    /// Configures the worker id and epoch when <paramref name="kind"/> is <see cref="IdentityGeneratorKind.Snowflake"/>.
    /// Required for Snowflake so that each node is assigned a distinct worker id.
    /// </param>
    public static IServiceCollection AddIdentityGenerator(
        this IServiceCollection services,
        IdentityGeneratorKind kind,
        Action<SnowflakeIdentityGeneratorOptions>? configureSnowflake = null)
    {
        services.RemoveAll<IIdentityGenerator>();

        switch (kind)
        {
            case IdentityGeneratorKind.UuidV7:
                services.AddScoped<IIdentityGenerator, UuidV7IdentityGenerator>();
                break;
            case IdentityGeneratorKind.Short:
                services.AddScoped<IIdentityGenerator, ShortIdentityGenerator>();
                break;
            case IdentityGeneratorKind.Snowflake:
                var options = new SnowflakeIdentityGeneratorOptions();
                configureSnowflake?.Invoke(options);
                services.AddSingleton(new SnowflakeIdentitySequence(options));
                services.AddScoped<IIdentityGenerator, SnowflakeIdentityGenerator>();
                break;
            case IdentityGeneratorKind.Guid:
                services.AddScoped<IIdentityGenerator, GuidIdentityGenerator>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity generator kind.");
        }

        return services;
    }
}
