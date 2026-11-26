using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Acme;

// catalog:case-14 begin
// The Elsa 3 global lookupCustomer(id) becomes acme.lookupCustomer(id): the converter matches a global to the
// registered host function with the same name after its prefix, and rewrites direct calls. The function is
// effect-free and takes and returns JSON. AcmeFeature registers the declaration.
public static class AcmeScriptFunctions
{
    private static readonly Dictionary<string, string> CustomerNames = new(StringComparer.Ordinal)
    {
        ["c-1"] = "Acme Retail",
        ["c-2"] = "Acme Wholesale"
    };

    public static JavaScriptHostFunctionDeclaration LookupCustomer { get; } = new("acme.lookupCustomer", call =>
    {
        var id = call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.String ? call.Arguments[0].GetString()! : "";
        return CustomerNames.TryGetValue(id, out var name)
            ? JsonSerializer.SerializeToElement(new { id, name })
            : JsonSerializer.SerializeToElement<object?>(null);
    });
}
// catalog:case-14 end
