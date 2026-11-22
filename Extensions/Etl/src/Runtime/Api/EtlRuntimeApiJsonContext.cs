using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Etl.Runtime.Api.Models;

namespace Elsa.Etl.Runtime.Api;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EtlRunPageRequest))]
[JsonSerializable(typeof(EtlRunPartitionsRequest))]
[JsonSerializable(typeof(EtlRunRequest))]
[JsonSerializable(typeof(EtlRunPage<EtlRunSummaryView>))]
[JsonSerializable(typeof(EtlRunPage<EtlRunPartitionView>))]
[JsonSerializable(typeof(EtlRunPage<EtlRunConnectorPartitionView>))]
[JsonSerializable(typeof(EtlRunSummaryView))]
[JsonSerializable(typeof(EtlRunDetailView))]
[JsonSerializable(typeof(EtlRunCancellationView))]
[JsonSerializable(typeof(EtlRunCompletionView))]
internal partial class EtlRuntimeApiJsonContext : JsonSerializerContext
{
    internal static EtlRuntimeApiJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }


}
