using CShells.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.Newtonsoft.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Serialization.Newtonsoft;

[ShellFeature(
    name: "Serialization.Newtonsoft",
    DisplayName = "Serialization - Newtonsoft",
    Description = "Contributes Newtonsoft JSON island handling for the payload serializer.")]
public sealed class NewtonsoftSerializationFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonIslandTypeHandler, NewtonsoftJsonIslandTypeHandler>();
    }
}
