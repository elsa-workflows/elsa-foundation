namespace Elsa.Decisions.Core;

/// <summary>Canonical domain grant identities shared by API, workflow and Studio contributions.</summary>
public static class DecisionPermissions
{
    public const string Read = "decisions:read";
    public const string Manage = "decisions:manage";
    public const string Publish = "decisions:publish";
    public const string Evaluate = "decisions:evaluate";
    public const string Test = "decisions:test";
    public const string History = "decisions:history";
    public const string Source = "decisions:source";
    public const string SensitiveValues = "decisions:sensitive-values";
}
