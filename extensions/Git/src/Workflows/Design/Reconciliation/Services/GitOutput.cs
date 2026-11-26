namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>Reads the text a git command prints.</summary>
internal static class GitOutput
{
    /// <summary>The non-blank lines of <paramref name="output"/>, each trimmed.</summary>
    public static string[] Lines(this string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
