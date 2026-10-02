using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

/// <summary>
/// Refuses a credential declaration on an activity version added through this API (spec 188, research R5), with the
/// shared <see cref="CredentialInputDeclaration"/> check raised as an <see cref="ActivityAuthoringException"/>. A
/// sensitive declaration is accepted.
/// </summary>
internal static class CredentialInputDeclarations
{
    /// <summary>Throws before anything is stored when any input declares <c>isCredential: true</c>, naming the first one.</summary>
    public static void Refuse(IEnumerable<InputDefinition>? inputs)
    {
        var credential = CredentialInputDeclaration.FindDeclared(inputs);
        if (credential is null)
            return;

        throw new ActivityAuthoringException(
            400,
            ActivityErrorCodes.RequestInvalid,
            "Credential input declaration refused",
            CredentialInputDeclaration.RefusalMessage(credential.ReferenceKey, "an activity version added through the API"));
    }
}
