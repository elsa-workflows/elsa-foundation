using Elsa.Activities.Design.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa3.Mapping.Conversion;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// The Elsa 3 conversion as the <c>Elsa3Mapping</c> feature registers it, over a given activity catalog. Tests resolve
/// the converter from these registrations, so they also prove the feature's wiring.
/// </summary>
internal static class ConversionServices
{
    public static ServiceProvider Create(IActivityDefinitionLookup catalog)
    {
        var services = new ServiceCollection();
        services.AddSingleton(catalog);
        services.AddSingleton(TypeRegistry());
        new Elsa3MappingFeature().ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Converts <paramref name="definition"/> against <paramref name="catalog"/> in a fresh conversion scope.</summary>
    public static async Task<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        IActivityDefinitionLookup catalog,
        Elsa3WorkflowDefinition definition,
        bool isReusable = false)
    {
        await using var provider = Create(catalog);
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>();
        return await converter.ConvertAsync(new Elsa3ConversionRequest(definition, isReusable));
    }

    public static IWellKnownTypeRegistry TypeRegistry()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(string), "String");
        registry.RegisterType(typeof(object), "Object");
        return registry;
    }

    /// <summary>A definition whose root is <paramref name="root"/>.</summary>
    public static Elsa3WorkflowDefinition Definition(
        Elsa3Activity root,
        string id = "definition-1",
        IEnumerable<Elsa3Variable>? variables = null,
        IEnumerable<Elsa3WorkflowArgumentDefinition>? outputs = null) => new()
    {
        Id = $"{id}-v1",
        DefinitionId = id,
        Name = id,
        Description = $"Conversion fixture {id}.",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Version = 1,
        Variables = (variables ?? []).ToList(),
        Outputs = (outputs ?? []).ToList(),
        Root = root,
    };
}
