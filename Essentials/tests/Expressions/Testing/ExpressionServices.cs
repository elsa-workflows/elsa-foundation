using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Testing;

/// <summary>
/// Expression services as the public features compose them, for tests in any domain.
/// </summary>
/// <remarks>
/// The container holds <see cref="SerializationServices.Compose"/> (the public SerializationFeature, logging to
/// nowhere), then <see cref="ExpressionsFeature"/> and <see cref="JavaScriptFeature"/>. No startup task runs, so no
/// converter is contributed and no type alias is seeded. The container refuses to hand out a scoped service from its
/// root. It serves the whole test process and is never disposed: the services handed out from it are stateless, and
/// whatever it holds is released with the process.
/// </remarks>
public static class ExpressionServices
{
    // The persisted expression type name of JavaScript; the descriptor that declares it is internal to its package.
    private const string JavaScriptExpressionType = "JavaScript";

    private static readonly ServiceProvider Services = Compose().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    /// <summary>
    /// An untyped variable declaration named <paramref name="name"/>, with <paramref name="value"/> as its default value
    /// and its name as its id. The expressions feature's own variable types are internal, so this implements the public
    /// <see cref="IVariable"/> contract directly.
    /// </summary>
    public static IVariable Variable(string name, object? value = null) => new DeclaredVariable { Id = name, Name = name, DefaultValue = value };

    /// <summary>The JavaScript authoring tooling the JavaScript feature contributes.</summary>
    public static IExpressionToolingProvider JavaScriptToolingProvider() =>
        Services.GetServices<IExpressionToolingProvider>()
            .Single(provider => provider.ExpressionType == JavaScriptExpressionType);

    private static ServiceCollection Compose()
    {
        var services = SerializationServices.Compose();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        return services;
    }

    private sealed class DeclaredVariable : IVariable
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public object? DefaultValue { get; set; }
        public Type? StorageDriverType { get; set; }
    }
}
