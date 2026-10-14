using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Events.Core.Extensions;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Handlers;
using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Services;
using Elsa.Serialization.SystemText.Sources;
using Elsa.Serialization.SystemText.Startup;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Serialization.SystemText;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Serialization")]
[ShellFeature(
    name: "Serialization",
    DisplayName = "Serialization",
    Description = "Provides JSON-based payload serialization. Features contribute JsonConverters through IJsonConverterSource, collected once at startup.")]
public class SerializationFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services
            .AddSingleton<IPayloadSerializer, JsonPayloadSerializer>()
            .AddSingleton<IObjectConverter, ObjectConverter>()
            .AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();

        // The type discriminator converter (the go-forward alias-registry mechanism) needs a concrete DI
        // registration so BuiltInJsonConverterSource can request it via constructor injection. Open-object
        // polymorphism (PolymorphicObjectConverterFactory) and the System.Text.Json JSON-island handler are
        // retired: type identity is a registry alias only, and JsonNode serializes natively.
        services.AddSingleton<TypeJsonConverter>();

        // The converter registry is a singleton populated once at startup; the serializer
        // reads from it synchronously while building JsonSerializerOptions.
        services.AddSingleton<JsonPayloadConverterRegistry>();

        // Startup task: dispatch JsonPayloadConvertersInitializing → flush contributions
        // into the registry. Other features (e.g. Expressions) contribute by registering an
        // IJsonConverterSource; the single RegisterJsonConverters handler aggregates them.
        services.AddScoped<IStartupTask, JsonPayloadConvertersInitializingStartupTask>();

        // Startup bridge: seed the well-known type registry from the contributed type descriptors so
        // authored argument aliases (e.g. "String", "Int32") resolve to their CLR types.
        services.AddScoped<IStartupTask, SeedWellKnownTypesStartupTask>();

        services.AddEventHandler<JsonPayloadConvertersInitializing, RegisterJsonConverters>();
        services.AddScoped<IJsonConverterSource, BuiltInJsonConverterSource>();
    }
}