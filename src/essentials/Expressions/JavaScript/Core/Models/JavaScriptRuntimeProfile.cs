using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Models;

/// <summary>
/// Immutable authoring metadata for selected capabilities of the binding-pure JavaScript runtime.
/// This curated surface is intentionally not an exhaustive JavaScript language catalogue.
/// </summary>
public static class JavaScriptRuntimeProfile
{
    public const string ArgumentsName = "args";
    public const string VariablesName = "variables";
    public const string GetVariableName = "getVariable";
    public const string ToolingCatalogRevision = "javascript-binding-pure-profile-v1";
    public const string ArgumentsDocumentation = "Immutable expression parameters. The object is present even when no parameters are declared.";
    public const string VariablesDocumentation = "Immutable visible workflow variables.";
    public const string GetVariableDocumentation = "Reads a visible workflow variable by name.";

    private static readonly IReadOnlyList<ExpressionSymbol> StandardGlobalSymbols = Array.AsReadOnly<ExpressionSymbol>(
    [
        new("javascript:profile:Math", "Math", ExpressionSymbolKind.Namespace,
            new("Math", ExpressionValueKind.Object, false), "Selected deterministic numeric functions."),
        Function("Math.abs", "abs(x): Number", "Returns the absolute value of a number.", new("Number", ExpressionValueKind.Scalar, false), "x"),
        Function("Math.ceil", "ceil(x): Number", "Rounds a number up to the nearest integer.", new("Number", ExpressionValueKind.Scalar, false), "x"),
        Function("Math.floor", "floor(x): Number", "Rounds a number down to the nearest integer.", new("Number", ExpressionValueKind.Scalar, false), "x"),
        Function("Math.max", "max(...values): Number", "Returns the greatest of the supplied numbers.", new("Number", ExpressionValueKind.Scalar, false), "...values"),
        Function("Math.min", "min(...values): Number", "Returns the least of the supplied numbers.", new("Number", ExpressionValueKind.Scalar, false), "...values"),
        Function("Math.pow", "pow(base, exponent): Number", "Returns base raised to exponent.", new("Number", ExpressionValueKind.Scalar, false), "base", "exponent"),
        Function("Math.round", "round(x): Number", "Rounds a number to the nearest integer.", new("Number", ExpressionValueKind.Scalar, false), "x"),
        Function("Math.sqrt", "sqrt(x): Number", "Returns the square root of a number.", new("Number", ExpressionValueKind.Scalar, false), "x"),
        new("javascript:profile:JSON", "JSON", ExpressionSymbolKind.Namespace,
            new("JSON", ExpressionValueKind.Object, false), "Deterministic JSON parsing and serialization."),
        Function("JSON.parse", "parse(text, reviver?): Any", "Parses a JSON string into a JavaScript value.", new("Any"), "text", "reviver?"),
        Function("JSON.stringify", "stringify(value, replacer?, space?): String?", "Serializes a JavaScript value as JSON.", new("String", ExpressionValueKind.Scalar, true), "value", "replacer?", "space?")
    ]);

    private static readonly IReadOnlyList<string> _disabledAmbientGlobalNames = Array.AsReadOnly(
        new[] { "Date", "Temporal", "Intl" });

    private static readonly IReadOnlyList<JavaScriptRuntimeMemberPath> _disabledAmbientMemberPaths = Array.AsReadOnly(
        new[] { new JavaScriptRuntimeMemberPath("Math", "random") });

    private static readonly IReadOnlyList<JavaScriptAmbientCapability> _unavailableAmbientCapabilities = Array.AsReadOnly(
        new[] { "Date", "Temporal", "Intl", "Math.random", "crypto", "performance", "process", "window", "document", "require", "Buffer" }
            .SelectMany(path => new[]
            {
                new JavaScriptAmbientCapability(path, path),
                new JavaScriptAmbientCapability($"globalThis.{path}", path)
            }).ToArray());

    private static readonly IReadOnlyList<string> _reservedVariableGetterNames = Array.AsReadOnly(
        new[] { GetVariableName });

    private static readonly IReadOnlyList<string> GetVariableParameters = Array.AsReadOnly(
        new[] { "name" });

    public static IReadOnlyList<ExpressionSymbol> SupportedStandardGlobals => StandardGlobalSymbols;
    public static IReadOnlyList<string> DisabledAmbientGlobalNames => _disabledAmbientGlobalNames;
    public static IReadOnlyList<JavaScriptRuntimeMemberPath> DisabledAmbientMemberPaths => _disabledAmbientMemberPaths;
    public static IReadOnlyList<JavaScriptAmbientCapability> UnavailableAmbientCapabilities => _unavailableAmbientCapabilities;
    public static IReadOnlyList<string> ReservedVariableGetterNames => _reservedVariableGetterNames;

    public static ExpressionSymbol GetVariableFunction { get; } = new(
        "javascript:getVariable",
        GetVariableName,
        ExpressionSymbolKind.Function,
        Documentation: GetVariableDocumentation,
        Signatures: Array.AsReadOnly<ExpressionCallableSignature>(
        [
            new ExpressionCallableSignature("getVariable(name)", GetVariableParameters, new("Any"))
        ]));

    public static ExpressionToolingCatalog DeclaredToolingCatalog { get; } = new(
        ToolingCatalogRevision,
        StandardGlobalSymbols);

    /// <summary>Gets the runtime's generated getter name, or null when the variable name contains unsupported characters.</summary>
    public static string? GetGeneratedVariableGetterName(string name)
    {
        if (name.Length == 0 || !name.All(character => char.IsLetterOrDigit(character) || character is '_' or '$'))
            return null;

        var getterName = $"get{char.ToUpperInvariant(name[0])}{name[1..]}";
        return ReservedVariableGetterNames.Contains(getterName, StringComparer.Ordinal) ? null : getterName;
    }

    private static ExpressionSymbol Function(
        string name,
        string display,
        string documentation,
        ExpressionValueShape returnShape,
        params string[] parameters) =>
        new(
            $"javascript:profile:{name}",
            name,
            ExpressionSymbolKind.Function,
            new(display, ExpressionValueKind.Function, false),
            documentation,
            Array.AsReadOnly<ExpressionCallableSignature>(
            [
                new ExpressionCallableSignature(
                    display,
                    Array.AsReadOnly(parameters),
                    returnShape)
            ]));
}

public sealed record JavaScriptRuntimeMemberPath(string ObjectName, string MemberName);

public sealed record JavaScriptAmbientCapability(string Path, string DisplayName);
