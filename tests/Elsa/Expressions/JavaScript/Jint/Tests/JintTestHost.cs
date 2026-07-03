using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Shared harness: build a DI container from the real <see cref="JintFeature"/> registration and
/// resolve <see cref="IJintEngineFactory"/> through the public surface (the factory itself is internal).
/// Tests configure the sandbox via the feature's ManifestSettings, exactly as a shell would.
/// </summary>
internal static class JintTestHost
{
    public static ServiceProvider Build(Action<JintFeature>? configureFeature = null, Action<IServiceCollection>? configureServices = null)
    {
        var feature = new JintFeature();
        configureFeature?.Invoke(feature);

        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public static IJintEngineFactory Factory(this IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IJintEngineFactory>();
}
