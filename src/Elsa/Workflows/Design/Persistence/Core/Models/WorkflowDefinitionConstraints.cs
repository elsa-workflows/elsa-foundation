using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>Portable persistence limits for workflow-definition identity and display name.</summary>
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
