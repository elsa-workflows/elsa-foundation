using System.Runtime.CompilerServices;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Services;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Immutable syntax only; each evaluation still constructs its own isolated engine.</summary>
internal static class PreparedJavaScript
{
    // Ephemeron ownership releases a tree that references its source after the last owning source
    // becomes unreachable, whether an executable binding or a freshly hydrated script input.
    // Separate tables preserve binding completion values versus script-activity return semantics.
    private static readonly ConditionalWeakTable<string, Code> Bindings = new();
    private static readonly ConditionalWeakTable<string, Code> Scripts = new();
    private const int ScriptIndexCapacity = 256;
    private static readonly object ScriptIndexGate = new();
    private static readonly Dictionary<(int Hash, int Length), WeakReference<Code>> ScriptIndex = new();
    private static readonly Queue<(int Hash, int Length)> ScriptIndexOrder = new();

    public static Prepared<Script> Binding(string source) => Bindings.GetValue(source, static text =>
    {
        var expression = JavaScriptBindingSyntax.IsExpression(text);
        return new Code(Engine.PrepareScript(expression ? $"(\n{text}\n)" : text, strict: true,
            options: new ScriptPreparationOptions
            {
                ParsingOptions = new() { AllowReturnOutsideFunction = !expression }
            }), text);
    }).Script;

    public static Prepared<Script> Script(string source) => Scripts.GetValue(source, PrepareScript).Script;

    private static Code PrepareScript(string source)
    {
        // Hydration decodes a fresh string per activation. A bounded hash-only index lets equal
        // content find an existing preparation without retaining code/source after its owners die.
        // Hash collisions only cause misses: every hit must compare the complete source ordinally.
        var key = (source.GetHashCode(StringComparison.Ordinal), source.Length);
        lock (ScriptIndexGate)
        {
            if (ScriptIndex.TryGetValue(key, out var reference) && reference.TryGetTarget(out var hit) &&
                StringComparer.Ordinal.Equals(hit.Source, source))
                return hit;
        }

        var prepared = new Code(Engine.PrepareScript($"\"use strict\"; (() => {{ {source} }})()"), source);
        lock (ScriptIndexGate)
        {
            if (ScriptIndex.TryGetValue(key, out var reference) && reference.TryGetTarget(out var hit) &&
                StringComparer.Ordinal.Equals(hit.Source, source))
                return hit;
            if (!ScriptIndex.ContainsKey(key))
            {
                if (ScriptIndex.Count == ScriptIndexCapacity)
                    ScriptIndex.Remove(ScriptIndexOrder.Dequeue());
                ScriptIndexOrder.Enqueue(key);
            }
            ScriptIndex[key] = new WeakReference<Code>(prepared);
        }
        return prepared;
    }

    private sealed record Code(Prepared<Script> Script, string Source);
}
