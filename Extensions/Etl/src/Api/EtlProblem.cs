using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Api;

internal sealed record EtlProblem(int Status, string Code);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EtlProblem))]
internal partial class EtlProblemJsonContext : JsonSerializerContext
{
    internal static EtlProblemJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}
