namespace Elsa.Server.Readiness;

public sealed class ShellReadinessOptions
{
    public const string SectionName = "Elsa:Readiness";

    public bool WarmDefaultShell { get; set; } = true;
    public string DefaultShellName { get; set; } = "default";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultShellName))
            throw new ArgumentException("A non-empty default shell name is required.", nameof(DefaultShellName));
    }
}
