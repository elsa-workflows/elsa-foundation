using System.Text;
using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// The built-in .NET-implemented functions of every JavaScript surface (spec 1223 FR-006, FR-007, FR-026), and the names
/// the function naming rule reserves (FR-027, ADR 0045 §5 D-5). For the same GUID or input, the built-ins return the
/// text Elsa 3 (Elsa.JavaScript 3.5.3) produced. They return strings and read no variables. There is no <c>toJson</c>:
/// scripts serialize with <c>JSON.stringify</c> (FR-009).
/// </summary>
public static class JavaScriptBuiltInFunctions
{
    public const string NewGuid = "newGuid";
    public const string NewGuidString = "newGuidString";
    public const string GetGuidString = "getGuidString";
    public const string NewShortGuid = "newShortGuid";
    public const string GetShortGuid = "getShortGuid";
    public const string ParseGuid = "parseGuid";
    public const string StringToBase64 = "stringToBase64";
    public const string StringFromBase64 = "stringFromBase64";

    /// <summary>The read roots a script reads engine-owned values through (ADR 0089, ADR 0045 §5 D-5).</summary>
    public static IReadOnlyList<string> ReadRoots { get; } = ["variables", "input", "activities", "workflow", "args"];

    /// <summary>The built-in functions, each with <see cref="JavaScriptHostFunctionDeclaration.ReadsVariables"/> false.</summary>
    public static IReadOnlyList<JavaScriptHostFunctionDeclaration> Declarations { get; } =
    [
        new(NewGuid, _ => Text(Guid.NewGuid().ToString())),
        new(NewGuidString, _ => Text(Guid.NewGuid().ToString())),
        new(GetGuidString, _ => Text(Guid.NewGuid().ToString())),
        new(NewShortGuid, _ => Text(ShortGuid(Guid.NewGuid()))),
        new(GetShortGuid, _ => Text(ShortGuid(Guid.NewGuid()))),
        new(ParseGuid, call => Text(CanonicalGuid(StringArgument(ParseGuid, call)))),
        new(StringToBase64, call => Text(ToBase64(StringArgument(StringToBase64, call)))),
        new(StringFromBase64, call => Text(FromBase64(StringArgument(StringFromBase64, call)))),
    ];

    /// <summary>The built-in function names.</summary>
    public static IReadOnlySet<string> Names { get; } = Declarations.Select(declaration => declaration.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether no host function may use <paramref name="name"/> as its namespace root: a read root, a built-in name, a
    /// global the platform installs (<c>complete</c>, <c>getVariable</c>) or a standard JavaScript global.
    /// </summary>
    public static bool IsReservedRoot(string name) =>
        ReadRoots.Contains(name, StringComparer.Ordinal) || Names.Contains(name) || EngineGlobals.Contains(name);

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

    /// <summary>The canonical 36-character text of the GUID <paramref name="text"/> denotes, in any form <c>Guid.Parse</c> accepts.</summary>
    public static string CanonicalGuid(string text) => Guid.Parse(text).ToString();

    /// <summary>UTF-8 text to standard Base64.</summary>
    public static string ToBase64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// <summary>Standard Base64 to UTF-8 text; invalid Base64 throws, invalid UTF-8 decodes to U+FFFD.</summary>
    public static string FromBase64(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    private static readonly HashSet<string> EngineGlobals = new(StringComparer.Ordinal)
    {
        JavaScriptScriptBuiltIns.Complete, "getVariable",
        "globalThis", "undefined", "NaN", "Infinity", "eval", "isFinite", "isNaN", "parseFloat", "parseInt",
        "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "escape", "unescape",
        "String", "Number", "Boolean", "Array", "Object", "Date", "Math", "JSON", "Intl", "Temporal", "RegExp", "Map", "Set",
        "WeakMap", "WeakSet", "WeakRef", "Symbol", "BigInt", "Promise", "Error", "Function", "Proxy", "Reflect",
        "ArrayBuffer", "SharedArrayBuffer", "DataView", "Atomics", "Iterator", "AggregateError", "EvalError", "RangeError",
        "ReferenceError", "SyntaxError", "TypeError", "URIError", "FinalizationRegistry",
    };

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    private static string StringArgument(string function, JavaScriptHostFunctionCall call)
    {
        if (call.Arguments.Count > 0 && call.Arguments[0].ValueKind == JsonValueKind.String)
            return call.Arguments[0].GetString()!;

        var given = call.Arguments.Count == 0 ? "no argument" : $"a {call.Arguments[0].ValueKind.ToString().ToLowerInvariant()}";
        throw new ArgumentException($"'{function}' takes a string argument; it was given {given}.");
    }
}
