namespace Elsa.Contracts.Generator;

/// <summary>
/// Canonical-MSBuild-format diagnostics (<c>path: warning|error ELSACT0NN: message</c>) so CI logs, IDEs
/// and coding agents surface generator findings as first-class build diagnostics (RFC #1191 resolved
/// position 2). Infrastructure exceptions never escape the tool raw — they are translated here (§2.23.5).
/// </summary>
public sealed class Diagnostics
{
    private readonly List<string> errors = [];
    private readonly List<string> warnings = [];

    public bool HasErrors => errors.Count > 0;

    public IReadOnlyList<string> Errors => errors;

    public void Error(string path, string code, string message)
    {
        var line = $"{path}: error {code}: {message}";
        errors.Add(line);
        Console.Error.WriteLine(line);
    }

    public void Warning(string path, string code, string message)
    {
        var line = $"{path}: warning {code}: {message}";
        warnings.Add(line);
        Console.Error.WriteLine(line);
    }
}
