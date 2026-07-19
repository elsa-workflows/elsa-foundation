using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>
/// Portable persistence limits for workflow-definition identity and display name. Groundwork schema version
/// 1.1 projects both values into bounded paging columns: pre-1.1 documents whose ID or name exceeds 128
/// characters must be renamed or rekeyed before the schema is applied. The upgrade is intentionally rejected
/// rather than truncating a value or allowing provider-specific behavior.
/// </summary>
public static class WorkflowDefinitionConstraints
{
    public const int MaximumIdLength = 128;
    public const int MaximumNameLength = 128;

    public static void Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateId(definition.Id);
        ValidateName(definition.Name);
    }

    public static void ValidateId(string id) => ValidateLength(id, MaximumIdLength, nameof(id));

    public static void ValidateName(string name) => ValidateLength(name, MaximumNameLength, nameof(name));

    private static void ValidateLength(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, value.Length, $"{parameterName} must be {maximumLength} characters or fewer.");
    }
}
