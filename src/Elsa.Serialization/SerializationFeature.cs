using CShells.Features;
using Elsa.Mediator.Core.Extensions;
using Elsa.Serialization.Core;
using Elsa.Serialization.Handlers;
using Elsa.Serialization.JsonConverters;
using Elsa.Serialization.Services;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Serialization
{
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

            // Built-in converters require these as concrete DI registrations so the handler
            // can request them via constructor injection.
            services.AddSingleton<PolymorphicObjectConverterFactory>();
            services.AddSingleton<TypeJsonConverter>();

            // The converter registry is a singleton populated once at startup; the serializer
            // reads from it synchronously while building JsonSerializerOptions.
            services.AddSingleton<JsonPayloadConverterRegistry>();

            // Startup task: dispatch OnJsonPayloadConvertersInitializing → flush contributions
            // into the registry. Other features (e.g. Expressions) register handlers for the
            // event to contribute their own converters.
            services.AddScoped<IStartupTask, JsonPayloadConvertersInitializingStartupTask>();

            // Built-in converters subscribe via this feature's own handler.
            services.AddDomainEventHandler<OnJsonPayloadConvertersInitializing, BuiltInJsonConvertersHandler>();
        }
    }
}
