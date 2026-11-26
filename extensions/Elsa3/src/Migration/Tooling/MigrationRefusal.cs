namespace Elsa3.Migration.Tooling;

/// <summary>
/// A refusal to run (exit 2, contracts/cli.md): one reason code and one sentence. The sentence names a path, a feature or
/// a setting key, never a setting value or definition content.
/// </summary>
internal sealed record MigrationRefusal(string Reason, string Sentence)
{
    public const string Arguments = "arguments";
    public const string InputMissing = "input-missing";
    public const string InputEmpty = "input-empty";
    public const string InputUnreadable = "input-unreadable";
    public const string CompositionFailed = "composition-failed";

    /// <summary>The standard-error line the CLI prints.</summary>
    public string ToLine() => $"elsa3-migration: refused: {Reason}: {Sentence}";
}
