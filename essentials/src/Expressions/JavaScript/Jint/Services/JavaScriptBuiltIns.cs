using System.Text;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The built-in functions the Jint feature registers (spec 1223 FR-006, FR-007, FR-026). For the same GUID or input
/// they return the text Elsa 3 (Elsa.JavaScript 3.5.3) produced. They return strings and read no variables.
/// </summary>
internal static class JavaScriptBuiltIns
{
    /// <summary>The built-in declarations, each with <see cref="JavaScriptHostFunctionDeclaration.ReadsVariables"/> false.</summary>
    public static IReadOnlyList<JavaScriptHostFunctionDeclaration> Declarations { get; } =
    [
        new(JavaScriptBuiltInFunctions.NewGuid, _ => Text(Guid.NewGuid().ToString())),
        new(JavaScriptBuiltInFunctions.NewGuidString, _ => Text(Guid.NewGuid().ToString())),
        new(JavaScriptBuiltInFunctions.GetGuidString, _ => Text(Guid.NewGuid().ToString())),
        new(JavaScriptBuiltInFunctions.NewShortGuid, _ => Text(ShortGuid(Guid.NewGuid()))),
        new(JavaScriptBuiltInFunctions.GetShortGuid, _ => Text(ShortGuid(Guid.NewGuid()))),
        new(JavaScriptBuiltInFunctions.ParseGuid, call => Text(Guid.Parse(StringArgument(JavaScriptBuiltInFunctions.ParseGuid, call)).ToString())),
        new(JavaScriptBuiltInFunctions.StringToBase64, call => Text(Convert.ToBase64String(Encoding.UTF8.GetBytes(StringArgument(JavaScriptBuiltInFunctions.StringToBase64, call))))),
        new(JavaScriptBuiltInFunctions.StringFromBase64, call => Text(Encoding.UTF8.GetString(Convert.FromBase64String(StringArgument(JavaScriptBuiltInFunctions.StringFromBase64, call))))),
    ];

    /// <summary>
    /// Elsa 3's short GUID: the standard Base64 of the GUID's 16 bytes in .NET byte order, with every <c>/</c>,
    /// <c>+</c> and <c>=</c> removed (at most 22 characters).
    /// </summary>
    public static string ShortGuid(Guid guid)
    {
        var base64 = Convert.ToBase64String(guid.ToByteArray());
        var builder = new StringBuilder(base64.Length);
        foreach (var character in base64)
        {
            if (character is not ('/' or '+' or '='))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value, Elsa.Serialization.Core.JsonBaseline.Options);

    private static string StringArgument(string function, JavaScriptHostFunctionCall call)
    {
        if (call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.String)
            return call.Arguments[0].GetString()!;
        // Elsa 3 converted a number argument to its text (stringToBase64(123) encoded "123").
        if (call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.Number)
            return call.Arguments[0].GetRawText();

        var given = call.Arguments.Count == 0 ? "no argument" : $"a {call.Arguments[0].ValueKind.ToString().ToLowerInvariant()}";
        throw new ArgumentException($"'{function}' takes a string argument; it was given {given}.");
    }
}
