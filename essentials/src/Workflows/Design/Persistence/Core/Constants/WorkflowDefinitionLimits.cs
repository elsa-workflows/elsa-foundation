using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Constants;

/// <summary>Portable limits shared by workflow-design persistence implementations.</summary>
public static class WorkflowDefinitionLimits
{
    public const int IdentityMaximumLength = 128;
    public const int TextMaximumLength = 256;
    public const int SearchKeyExpansionFactor = 7;
    public const int IdentitySearchKeyMaximumLength = IdentityMaximumLength * SearchKeyExpansionFactor;
    public const int TextSearchKeyMaximumLength = TextMaximumLength * SearchKeyExpansionFactor;

    public static void Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateIdentity(definition.Id, nameof(definition.Id));
        if (definition.TenantId is not null)
            ValidateIdentity(definition.TenantId, nameof(definition.TenantId));
        ValidateText(definition.Name, nameof(definition.Name));
        if (definition.Description is not null)
            ValidateText(definition.Description, nameof(definition.Description));
        if (definition.DeletedReason is not null)
            ValidateText(definition.DeletedReason, nameof(definition.DeletedReason));
    }

    public static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > IdentityMaximumLength)
            throw new ArgumentException($"Workflow-design identities cannot exceed {IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    public static void ValidateText(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > TextMaximumLength)
            throw new ArgumentException($"Workflow-definition text cannot exceed {TextMaximumLength} UTF-16 code units.", parameterName);
    }
}
