using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// The one shape of a connection-literal refusal: a connection-typed input may only carry a
/// Connection reference (name and type). Nothing here receives the bound value.
/// </summary>
public static class ConnectionLiteralFinding
{
    public static ValidationError For(string nodeId, string referenceKey, string inputName) => new(
        Path: $"{nodeId}/inputs/{referenceKey}",
        Type: ConnectionInputBinding.RuleId,
        Message: $"{ConnectionInputBinding.RuleId}: input '{inputName}' on activity '{nodeId}' accepts only a Connection reference.");
}
