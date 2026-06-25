namespace Elsa.Agent.GitHubCopilot.Options;

public sealed class GitHubCopilotAgentOptions
{
    public const string SectionName = "Elsa:Agent:GitHubCopilot";

    public bool Enabled { get; set; }

    public string? GitHubToken { get; set; }

    public string? GitHubTokenEnvironmentVariable { get; set; } = "COPILOT_GITHUB_TOKEN";

    public bool UseLoggedInUser { get; set; }

    public string? RuntimeUrl { get; set; }

    public string? RuntimeConnectionToken { get; set; }

    public string? BaseDirectory { get; set; }

    public string? WorkingDirectory { get; set; }

    public string Model { get; set; } = "auto";

    public string? ReasoningEffort { get; set; }

    public bool Streaming { get; set; } = true;

    public string? SystemMessage { get; set; }

    public bool IncludeSensitiveContextContent { get; set; }

    public IList<string> AvailableTools { get; } = [];

    public IList<string> ExcludedTools { get; } =
    [
        "edit_file",
        "run_in_terminal",
        "create_file",
        "delete_file",
        "replace_string_in_file",
        "insert_edit_into_file",
        "apply_patch",
        "install_extension",
        "create_pull_request"
    ];
}
