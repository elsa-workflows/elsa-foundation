using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlCamelCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = new[] { typeof(EtlCamelCaseEnumConverter), typeof(EtlPipelineV2JsonConverter) })]
[JsonSerializable(typeof(EtlPageRequest))]
[JsonSerializable(typeof(EtlDefinitionRequest))]
[JsonSerializable(typeof(EtlVersionsRequest))]
[JsonSerializable(typeof(EtlVersionRequest))]
[JsonSerializable(typeof(EtlCreateDefinitionRequest))]
[JsonSerializable(typeof(EtlConnectorCreateDefinitionRequest))]
[JsonSerializable(typeof(EtlUpdateDraftRequest))]
[JsonSerializable(typeof(EtlConnectorUpdateDraftRequest))]
[JsonSerializable(typeof(EtlValidateRequest))]
[JsonSerializable(typeof(EtlPublishRequest))]
[JsonSerializable(typeof(EtlPage<EtlBindingSummary>))]
[JsonSerializable(typeof(EtlPage<EtlConnectorBindingSummary>))]
[JsonSerializable(typeof(EtlPage<EtlDefinitionSummaryView>))]
[JsonSerializable(typeof(EtlPage<EtlPublishedVersionView>))]
[JsonSerializable(typeof(EtlDraftView))]
[JsonSerializable(typeof(EtlConnectorDraftView))]
[JsonSerializable(typeof(EtlPublishedArtifactView))]
[JsonSerializable(typeof(EtlConnectorPublishedArtifactView))]
[JsonSerializable(typeof(EtlValidationView))]
[JsonSerializable(typeof(EtlPublicationView))]
[JsonSerializable(typeof(EtlApiProblem))]
internal partial class EtlDesignJsonContext : JsonSerializerContext
{
    internal static EtlDesignJsonContext WireContext => Wire.Context;

    // Initialize after the generated context's own static defaults, irrespective of partial-file order.
    private static class Wire
    {
        internal static readonly EtlDesignJsonContext Context = new(new JsonSerializerOptions(Default.Options));
    }
}

/// <summary>Uses the bounded canonical v2 codec for API pipeline input and output.</summary>
internal sealed class EtlPipelineV2JsonConverter : JsonConverter<EtlPipelineV2>
{
    public override EtlPipelineV2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var extentReader = reader;
        extentReader.Skip();
        if (extentReader.BytesConsumed - reader.TokenStartIndex > EtlArtifactV2Json.MaximumByteLength)
            throw new JsonException("The ETL v2 pipeline document exceeds 256 KiB.");

        using var document = JsonDocument.ParseValue(ref reader);
        return EtlArtifactV2Json.Read(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, EtlPipelineV2 value, JsonSerializerOptions options) =>
        EtlArtifactV2Json.Write(value).WriteTo(writer);
}
