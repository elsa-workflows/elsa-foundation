using CShells.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.JsonConverters;
using Elsa.Serialization.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization
{
    [ShellFeature(
        name: "Serialization",
        DisplayName = "Serialization",
        Description = "Provides JSON-based payload serialization and pluggable JsonConverter contributions via IPayloadSerializerConverterProvider.")]
    public class SerializationFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddSingleton<IPayloadSerializer, JsonPayloadSerializer>()
                .AddSingleton<IObjectConverter, ObjectConverter>()
                .AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();

            RegisterJsonPayloadSerializerConverterProvider(services, () => new JsonStringEnumConverter());
            RegisterJsonPayloadSerializerConverterProvider(services, () => JsonMetadataServices.TimeSpanConverter);
            RegisterJsonPayloadSerializerConverterProvider(services, GetService<PolymorphicObjectConverterFactory>);
            RegisterJsonPayloadSerializerConverterProvider(services, GetService<TypeJsonConverter>);
        }

        private static void RegisterJsonPayloadSerializerConverterProvider(IServiceCollection services, Func<JsonConverter> factory)
        {
            services.AddSingleton<IPayloadSerializerConverterProvider>(sp => new DefaultPayloadSerializerConverterProvider(factory));
        }

        private static void RegisterJsonPayloadSerializerConverterProvider(IServiceCollection services, Func<IServiceProvider, JsonConverter> factory)
        {
            services.AddSingleton<IPayloadSerializerConverterProvider>(sp => new DefaultPayloadSerializerConverterProvider(() => factory(sp)));
        }

        private static T GetService<T>(IServiceProvider serviceProvider) where T : notnull
         => ActivatorUtilities.GetServiceOrCreateInstance<T>(serviceProvider);
    }
}
