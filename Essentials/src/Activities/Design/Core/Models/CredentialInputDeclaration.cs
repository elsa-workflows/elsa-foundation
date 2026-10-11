namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// The phase-0 refusal of a credential declaration from an activity source that cannot check it (spec 188, research
/// R5). A credential input accepts only a secret reference, and CLR reconciliation refuses a credential declaration
/// that could never be bound to one, so the declaration is reserved for <c>[ActivityInput(IsCredential = true)]</c> on
/// a CLR activity. The Activities Design API add commands and the JSON activity reconciliation source share this check;
/// each raises it through its own exception type. CLR reconciliation keeps its own rules, because it accepts a
/// credential that can be bound.
/// </summary>
public static class CredentialInputDeclaration
{
    /// <summary>The first input that declares <c>isCredential: true</c>, or null when none does.</summary>
    public static InputDefinition? FindDeclared(IEnumerable<InputDefinition>? inputs) =>
        inputs?.FirstOrDefault(input => input.IsCredential == true);

    /// <summary>
    /// The refusal text, naming the input and the <paramref name="source"/> that cannot declare a credential, and never
    /// a value.
    /// </summary>
    public static string RefusalMessage(string inputKey, string source) =>
        $"Input '{inputKey}' declares isCredential, which {source} cannot declare. Declare a credential input with [ActivityInput(IsCredential = true)] on a CLR activity.";
}
