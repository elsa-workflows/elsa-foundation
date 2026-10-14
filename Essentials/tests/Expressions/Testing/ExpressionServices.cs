using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Testing;

/// <summary>
/// Expression services as the public features compose them, for tests in any domain.
/// </summary>
/// <remarks>
/// The container holds <see cref="SerializationServices.Compose"/> (the public SerializationFeature, logging to
/// nowhere), then <see cref="ExpressionsFeature"/> and <see cref="JavaScriptFeature"/>. No startup task runs, so no
/// converter is contributed and no type alias is seeded. Scoped services are resolved from a scope of their own, and
/// the container refuses to hand one out from its root. That container serves the whole test process and is never
/// disposed: the services handed out from it are stateless, and whatever it holds is released with the process.
/// </remarks>
public static class ExpressionServices
{
    private static readonly ServiceProvider Services = Compose().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    /// <summary>An untyped variable, as the expressions feature's variable factory creates it.</summary>
    public static IVariable Variable(string name, object? value = null)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IVariableFactory>().Create(name, value);
    }

    /// <summary>The JavaScript authoring tooling the JavaScript feature contributes.</summary>
    public static IExpressionToolingProvider JavaScriptToolingProvider() =>
        Services.GetServices<IExpressionToolingProvider>()
            .Single(provider => provider.ExpressionType == JavaScriptExpressionDescriptor.JavaScriptExpressionTypeName);

    private static ServiceCollection Compose()
    {
        var services = SerializationServices.Compose();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        return services;
    }
}
