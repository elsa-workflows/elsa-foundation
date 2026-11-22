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
        if (catalog is Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore store)
            services.AddSingleton(store);
        services.AddSingleton(TypeRegistry());
        services.AddSingleton(SerializationServices.ObjectConverter());
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
        // The primitives a host seeds from its type descriptors; a variable's starting value converts only to a known type.
        registry.RegisterType(typeof(int), "Int32");
        registry.RegisterType(typeof(long), "Int64");
        registry.RegisterType(typeof(double), "Double");
        registry.RegisterType(typeof(decimal), "Decimal");
        registry.RegisterType(typeof(bool), "Boolean");
        registry.RegisterType(typeof(DateTime), "DateTime");
        registry.RegisterType(typeof(Guid), "Guid");
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
