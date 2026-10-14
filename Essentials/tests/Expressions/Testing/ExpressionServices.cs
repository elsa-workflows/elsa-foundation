using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Expressions.Testing;

/// <summary>
/// Expression services as the public <see cref="ExpressionsFeature"/> and <see cref="JavaScriptFeature"/> compose them,
/// for tests in any domain.
/// </summary>
/// <remarks>
/// One container serves the whole test process and is never disposed. The services handed out from it are stateless,
/// so sharing them across tests changes nothing, and whatever the container holds is released with the process.
/// </remarks>
public static class ExpressionServices
{
    private static readonly ServiceProvider Services = Compose().BuildServiceProvider();

    /// <summary>An untyped variable, as the expressions feature's variable factory creates it.</summary>
    public static IVariable Variable(string name, object? value = null) =>
        Services.GetRequiredService<IVariableFactory>().Create(name, value);

    /// <summary>The JavaScript authoring tooling the JavaScript feature contributes.</summary>
    public static IExpressionToolingProvider JavaScriptToolingProvider() =>
        Services.GetServices<IExpressionToolingProvider>()
            .Single(provider => provider.ExpressionType == JavaScriptExpressionDescriptor.JavaScriptExpressionTypeName);

    private static ServiceCollection Compose()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        return services;
    }
}
