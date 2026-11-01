using System.Runtime.CompilerServices;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Services;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Immutable syntax only; each evaluation still constructs its own isolated engine.</summary>
internal static class PreparedJavaScript
{
    // Source strings belong to immutable executable bindings/definitions. Ephemeron ownership also
    // releases a prepared tree that references its source after the executable leaves its cache.
    // Separate tables preserve binding completion values versus script-activity return semantics.
    private static readonly ConditionalWeakTable<string, Code> Bindings = new();
    private static readonly ConditionalWeakTable<string, Code> Scripts = new();

    public static Prepared<Script> Binding(string source) => Bindings.GetValue(source, static text =>
    {
        var expression = JavaScriptBindingSyntax.IsExpression(text);
        return new Code(Engine.PrepareScript(expression ? $"(\n{text}\n)" : text, strict: true,
            options: new ScriptPreparationOptions
            {
                ParsingOptions = new() { AllowReturnOutsideFunction = !expression }
            }));
    }).Script;

    public static Prepared<Script> Script(string source) => Scripts.GetValue(source, static text =>
        new Code(Engine.PrepareScript($"\"use strict\"; (() => {{ {text} }})()"))).Script;

    private sealed record Code(Prepared<Script> Script);
}
