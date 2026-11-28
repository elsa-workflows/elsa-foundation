using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Acme.Elsa3MigrationFixture;

/// <summary>
/// The fixture's JavaScript host functions. Each carries the <c>acme</c> namespace prefix and the member name of the
/// Elsa 3 global it takes over, so the converter rewrites a direct call <c>name(…)</c> into <c>acme.name(…)</c>. Their
/// results are fixed JSON; nothing runs the fixture's workflows.
/// </summary>
internal static class AcmeHostFunctions
{
    /// <summary>The custom Elsa 3 global the fixture calls in one definition and passes as a value in another.</summary>
    public static JavaScriptHostFunctionDeclaration LookupCustomer { get; } =
        new("acme.lookupCustomer", call => JsonSerializer.SerializeToElement(new { id = FirstArgument(call) }));

    /// <summary>The Elsa 3 built-in the converter refuses unless a host function takes it over.</summary>
    public static JavaScriptHostFunctionDeclaration GetConfig { get; } =
        new("acme.getConfig", _ => JsonSerializer.SerializeToElement("fixture"));

    /// <summary>A <c>get</c>-prefixed custom global that no variable or input of the fixture has.</summary>
    public static JavaScriptHostFunctionDeclaration GetCustomerData { get; } =
        new("acme.getCustomerData", call => JsonSerializer.SerializeToElement(new { id = FirstArgument(call) }));

    public static IReadOnlyList<JavaScriptHostFunctionDeclaration> All { get; } = [LookupCustomer, GetConfig, GetCustomerData];

    private static string? FirstArgument(JavaScriptHostFunctionCall call) =>
        call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.String ? call.Arguments[0].GetString() : null;
}
