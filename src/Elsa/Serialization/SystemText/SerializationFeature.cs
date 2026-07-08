using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
    Description = "Provides JSON-based payload serialization. Pluggable JsonConverter contributions flow through the OnJsonPayloadConvertersInitializing domain event (framework §2.6.1 Registry + StartUp Task sub-pattern; Elsa §E3.3 worked example).")]
public class SerializationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddSingleton<IPayloadSerializer, JsonPayloadSerializer>()
            .AddSingleton<IObjectConverter, ObjectConverter>()
            .AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();

        // The type discriminator converter (the go-forward alias-registry mechanism) needs a concrete DI
        // registration so BuiltInJsonConverterSource can request it via constructor injection. Open-object
        // polymorphism (PolymorphicObjectConverterFactory) and the System.Text.Json JSON-island handler are
        // retired: type identity is a registry alias only, and JsonNode serializes natively (ADR 0035 D2/D5).
        services.AddSingleton<TypeJsonConverter>();

        // The converter registry is a singleton populated once at startup; the serializer
        // reads from it synchronously while building JsonSerializerOptions.
        services.AddSingleton<JsonPayloadConverterRegistry>();

        // Startup task: dispatch OnJsonPayloadConvertersInitializing → flush contributions
        // into the registry. Other features (e.g. Expressions) contribute by registering an
        // IJsonConverterSource; the single RegisterJsonConverters handler aggregates them.
        services.AddScoped<IStartupTask, JsonPayloadConvertersInitializingStartupTask>();

        // Startup bridge: seed the well-known type registry from the contributed type descriptors so
        // authored argument aliases (e.g. "String", "Int32") resolve to their CLR types (research D5).
        services.AddScoped<IStartupTask, SeedWellKnownTypesStartupTask>();

        services.AddEventHandler<OnJsonPayloadConvertersInitializing, RegisterJsonConverters>();
        services.AddScoped<IJsonConverterSource, BuiltInJsonConverterSource>();
    }
}