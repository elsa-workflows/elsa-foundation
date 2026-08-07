using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The single point where the matrix runner's argv meets the host.
///
/// Kept separate from the CLI so it can be exercised offline: the runner serializes the request with
/// <c>ArtifactStore.JsonOptions</c>, which is PascalCase and registers no string-enum converter, so
/// <c>ProcessKind</c> travels as a number. A host that guessed those options would either reject every
/// request or — far worse — parse a warmup request as measured and emit timed samples the harness would
/// then reject four processes later.
/// </summary>
internal static class RunRequestWire
{
    public static string Serialize(RunRequest request) => JsonSerializer.Serialize(request, ArtifactStore.JsonOptions);

    public static RunRequest Parse(string json)
    {
        try
        {
            using (var document = JsonDocument.Parse(json)) ArtifactStore.RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<RunRequest>(json, ArtifactStore.JsonOptions)
                   ?? throw new PerformanceContractException("The matrix run request is invalid.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"The matrix run request JSON is invalid: {exception.Message}");
        }
    }
}
