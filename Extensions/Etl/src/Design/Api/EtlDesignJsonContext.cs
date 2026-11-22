using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlCamelCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
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
    internal static EtlDesignJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.Converters.Add(new EtlCamelCaseEnumConverter());
        options.Converters.Add(new EtlPipelineV2JsonConverter());
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
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
        JsonSerializer.Serialize(writer, EtlArtifactV2Json.Write(value), Elsa.Serialization.Core.JsonBaseline.Options);
}
