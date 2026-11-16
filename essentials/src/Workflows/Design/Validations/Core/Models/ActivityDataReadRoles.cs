namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>The authored expression types of the member-read roles, each the read of one member of its source.</summary>
public static class ActivityDataReadRoles
{
    /// <summary>A member of an activity result.</summary>
    public const string ActivityResultMember = "ActivityResultMember";

    /// <summary>A member of a variable.</summary>
    public const string VariableMember = "VariableMember";

    /// <summary>A member of a workflow request member.</summary>
    public const string WorkflowRequestMember = "WorkflowRequestMember";

    /// <summary>A member of a declared boundary input; publication lowers it to a request member read.</summary>
    public const string InputMember = "InputMember";

    /// <summary>Every member-read role.</summary>
    public static IReadOnlyList<string> All { get; } = [ActivityResultMember, VariableMember, WorkflowRequestMember, InputMember];

    /// <summary>Whether <paramref name="expressionType"/> is a member-read role, ignoring case as authored types are matched.</summary>
    public static bool IsMemberRole(string? expressionType) =>
        expressionType is not null && All.Contains(expressionType, StringComparer.OrdinalIgnoreCase);
}
