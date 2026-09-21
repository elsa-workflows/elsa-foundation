using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>
/// API projection of the derived validation error set for a draft. Wire shape for
/// <c>GET design/workflows/drafts/{draftId}/validations</c>.
/// </summary>
public sealed record DraftValidationsView(string DraftId, IReadOnlyCollection<DraftValidationErrorView> Errors)
{
    public static DraftValidationsView From(string draftId, IEnumerable<ValidationError> errors) =>
        new(draftId, errors.Select(DraftValidationErrorView.From).ToArray());
}

/// <summary>
/// Client-facing projection of a single <see cref="ValidationError"/>. The internal model's
/// slash-delimited <c>Path</c> (R2) is decomposed into a <see cref="NodeId"/> pointer and a
/// <see cref="Member"/> suffix so clients can bind an error to a node or an input/output/variable
/// without re-parsing the path; the internal <c>Type</c> category (R3) surfaces as <see cref="Code"/>.
/// </summary>
public sealed record DraftValidationErrorView(
    string Code,
    string Message,
    string? NodeId,
    string? Member,
    string Severity)
{
    /// <summary>
    /// All validators emit compile errors (FR-024) — the internal model carries no severity axis, so
    /// every error projects as <c>Error</c>. Kept explicit so a future severity dimension is additive.
    /// </summary>
    public const string ErrorSeverity = "Error";

    public static DraftValidationErrorView From(ValidationError error)
    {
        var (nodeId, member) = SplitPath(error.Path);
        return new(error.Type, error.Message, nodeId, member, ErrorSeverity);
    }

    /// <summary>
    /// Splits an R2 path into its leading node pointer and trailing member reference. The
    /// <c>$workflow</c> sentinel (a workflow-scope concern, not a node) maps to a null node id.
    /// Examples: <c>n1/inputs/x</c> → (<c>n1</c>, <c>inputs/x</c>); <c>$workflow/variables/v</c> →
    /// (<c>null</c>, <c>variables/v</c>); <c>$workflow</c> → (<c>null</c>, <c>null</c>).
    /// </summary>
    private static (string? NodeId, string? Member) SplitPath(string path)
    {
        var separatorIndex = path.IndexOf('/');
        var head = separatorIndex < 0 ? path : path[..separatorIndex];
        var tail = separatorIndex < 0 ? null : path[(separatorIndex + 1)..];
        var nodeId = head == ValidationPaths.Workflow ? null : head;
        return (nodeId, string.IsNullOrEmpty(tail) ? null : tail);
    }
}
