using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa3.Activities.Design.Import.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReusableActivityImportSelectionRequest))]
[JsonSerializable(typeof(ReusableActivityImportApplyHttpRequest))]
[JsonSerializable(typeof(ReusableActivityImportUploadResult))]
[JsonSerializable(typeof(ReusableActivityImportAnalysisPage))]
[JsonSerializable(typeof(ReusableActivityImportSelectionReadiness))]
[JsonSerializable(typeof(ReusableActivityImportReceipt))]
[JsonSerializable(typeof(ReusableActivityImportProblem))]
internal partial class ReusableActivityImportJsonContext : JsonSerializerContext
{
    internal static ReusableActivityImportJsonContext WireContext { get; } = new(CreateWireOptions());

    private static System.Text.Json.JsonSerializerOptions CreateWireOptions()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        return options;
    }

}

internal sealed record ReusableActivityImportProblem(
    int Status,
    string Type,
    string Title,
    string Detail,
    string Instance,
    string ErrorCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<Elsa3MigrationDiagnostic>? Diagnostics);
