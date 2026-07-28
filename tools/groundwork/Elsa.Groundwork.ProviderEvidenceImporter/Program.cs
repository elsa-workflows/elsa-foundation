using Elsa.Groundwork.Tools;
using System.Text.Json;

try
{
    var arguments = ParseArguments(args);
    var result = await CheckpointFenceEvidenceImporter.ImportAsync(
        new CheckpointFenceEvidenceImportRequest(
            Required(arguments, "ledger"),
            Required(arguments, "staging-root"),
            Required(arguments, "source-repository"),
            Required(arguments, "provider-version"),
            new CheckpointFenceEvidenceProvenance(
                Required(arguments, "elsa-commit"),
                Required(arguments, "elsa-tree"),
                Required(arguments, "run-identity"))));
    Console.WriteLine($"Imported {result.ImportedRecordCount} retained checkpoint/fence records from external staging.");
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or JsonException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal) ||
            !values.TryAdd(arguments[index][2..], arguments[index + 1]))
        {
            throw new ArgumentException(
                "Usage: --ledger <path> --staging-root <path> --source-repository <path> --provider-version <version> --elsa-commit <sha> --elsa-tree <sha> --run-identity <identity>");
        }
    }

    return values;
}

static string Required(IReadOnlyDictionary<string, string> arguments, string name) =>
    arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required --{name} argument.");
