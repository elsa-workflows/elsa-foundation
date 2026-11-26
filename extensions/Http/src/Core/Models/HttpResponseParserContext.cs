namespace Elsa.Http.Core.Models;

/// <summary>
/// Represents the context in which an HTTP response is being parsed.
/// </summary>
public record HttpResponseParserContext(Stream Content, string ContentType, Type? ReturnType, IDictionary<string, string[]> Headers, CancellationToken CancellationToken)
{
    /// <summary>The owner's recording sink for diagnostics when a typed JSON response is requested.</summary>
    public Action<Elsa.Serialization.Core.JsonConversionDiagnostic>? ReportJsonDiagnostic { get; init; }
}
