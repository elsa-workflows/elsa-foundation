using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design.Api.Models;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlCamelCaseEnumConverter() : JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = new[] { typeof(EtlCamelCaseEnumConverter) })]
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
