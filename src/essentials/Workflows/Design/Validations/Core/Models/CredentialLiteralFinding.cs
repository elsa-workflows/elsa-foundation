using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// The one shape of a credential-literal refusal (spec 188, FR-008), shared by the design-time validator and
/// publication: the input's path, the rule identifier as its type, and a message that starts with the rule identifier,
/// so every surface that shows only messages still carries it. Nothing here receives the bound value.
/// </summary>
public static class CredentialLiteralFinding
{
    /// <summary>The finding for input <paramref name="referenceKey"/>, named <paramref name="inputName"/>, of node <paramref name="nodeId"/>.</summary>
    public static ValidationError For(string nodeId, string referenceKey, string inputName) => new(
        Path: $"{nodeId}/inputs/{referenceKey}",
        Type: CredentialInputBinding.RuleId,
        Message: $"{CredentialInputBinding.RuleId}: input '{inputName}' on activity '{nodeId}' holds a credential and accepts only a secret reference.");
}
