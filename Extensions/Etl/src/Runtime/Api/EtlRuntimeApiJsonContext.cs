using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Etl.Runtime.Api.Models;

namespace Elsa.Etl.Runtime.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
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
internal partial class EtlRuntimeApiJsonContext : JsonSerializerContext
{
    internal static EtlRuntimeApiJsonContext WireContext => Wire.Context;

    private static class Wire
    {
        internal static readonly EtlRuntimeApiJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}
