using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

/// <summary>
/// Refuses a credential declaration on an activity version added through this API (spec 188, research R5). A
/// credential input accepts only a secret reference, and CLR reconciliation refuses a credential declaration that
/// could never be bound to one; a version added here has no such check, so in phase 0 the credential declaration is
/// reserved for <c>[ActivityInput(IsCredential = true)]</c> on a CLR activity. A sensitive declaration is accepted.
/// </summary>
internal static class CredentialInputDeclarations
{
    /// <summary>Throws before anything is stored when any input declares <c>isCredential: true</c>, naming the first one.</summary>
    public static void Refuse(IEnumerable<InputDefinition>? inputs)
    {
        var credential = inputs?.FirstOrDefault(input => input.IsCredential == true);
        if (credential is null)
            return;

        throw new ActivityAuthoringException(
            400,
            ActivityErrorCodes.RequestInvalid,
            "Credential input declaration refused",
            $"Input '{credential.ReferenceKey}' declares isCredential, which an activity version added through the API cannot declare. Declare a credential input with [ActivityInput(IsCredential = true)] on a CLR activity.");
    }
}
