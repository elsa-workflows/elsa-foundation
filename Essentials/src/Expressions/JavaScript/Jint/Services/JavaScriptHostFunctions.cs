using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Microsoft.Extensions.Logging;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>What the functions of one evaluation are called with.</summary>
/// <param name="Values">The evaluation's values, which know the numbers that entered it lossy (spec 1228).</param>
/// <param name="Variables">The evaluation's variable reader, for a function that reads variables; null when it has none.</param>
/// <param name="Logger">Where a lossy argument is also logged.</param>
internal sealed record JavaScriptHostFunctionContext(
    IScriptValueEvaluation Values,
    IExpressionVariableReader? Variables,
    ILogger? Logger)
{
    /// <summary>
    /// The warnings the calls raised, reported by the evaluator after the run with its other warnings, so a protected
    /// read can still turn them into their protected form.
    /// </summary>
    public List<ExecutionWarning> Warnings { get; } = [];
}

/// <summary>
/// The registered .NET-implemented functions, checked against the naming rule (spec 1223 FR-027, ADR 0045 §5 D-5) and
/// installed into every evaluation, input bindings and scripts alike (FR-003). A built-in is a global of its own name;
/// a host function <c>acme.lookup</c> is member <c>lookup</c> of the read-only global object <c>acme</c>. Arguments and
/// results cross as JSON values (FR-011).
/// </summary>
internal sealed class JavaScriptHostFunctions
{
    private readonly IReadOnlyList<JavaScriptHostFunctionDeclaration> _globals;
    private readonly IReadOnlyList<(string Root, IReadOnlyList<(string Member, JavaScriptHostFunctionDeclaration Declaration)> Members)> _namespaces;

    public JavaScriptHostFunctions(IEnumerable<JavaScriptHostFunctionDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var all = declarations.ToArray();
        Validate(all);
        _globals = all.Where(declaration => !declaration.Name.Contains('.', StringComparison.Ordinal)).ToArray();
        _namespaces = all
            .Where(declaration => declaration.Name.Contains('.', StringComparison.Ordinal))
            .GroupBy(declaration => declaration.Name[..declaration.Name.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, (IReadOnlyList<(string, JavaScriptHostFunctionDeclaration)>)group
                .Select(declaration => (declaration.Name[(group.Key.Length + 1)..], declaration))
                .OrderBy(member => member.Item1, StringComparer.Ordinal)
                .ToArray()))
            .ToArray();
    }

    /// <summary>The names installed as globals: the built-ins and the namespace roots.</summary>
    public IEnumerable<string> GlobalNames => _globals.Select(declaration => declaration.Name).Concat(_namespaces.Select(item => item.Root));

    /// <summary>
    /// Fails with a message naming the function when a registration breaks the naming rule: a duplicate name, an
    /// unprefixed name that is not a built-in, a malformed name, or a namespace root that is reserved.
    /// </summary>
    public static void Validate(IReadOnlyList<JavaScriptHostFunctionDeclaration> declarations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var name = declaration.Name;
            if (!seen.Add(name))
                throw new InvalidOperationException($"JavaScript function '{name}' is registered more than once; each function name must be registered once.");

            var parts = name.Split('.');
            if (parts.Length == 1)
            {
                if (!JavaScriptBuiltInFunctions.Names.Contains(name))
                    throw new InvalidOperationException(
                        $"JavaScript function '{name}' has no namespace prefix. Register a host function as '<namespace>.{name}' (for example 'acme.{name}'); only the built-in functions are unprefixed.");
                continue;
            }

            if (parts.Length != 2 || !parts.All(IsIdentifier))
                throw new InvalidOperationException(
                    $"JavaScript function '{name}' is not a valid name: use '<namespace>.<function>', both JavaScript identifiers.");
            if (IsReservedRoot(parts[0]))
                throw new InvalidOperationException(
                    $"JavaScript function '{name}' uses the reserved name '{parts[0]}' as its namespace. Read roots, built-in function names and JavaScript globals cannot be namespaces.");
        }
    }

    /// <summary>Installs every function as a non-writable global of <paramref name="engine"/>.</summary>
    public void Install(Engine engine, FeatureOptions options, JavaScriptHostFunctionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        if (_globals.Count == 0 && _namespaces.Count == 0)
            return;

        // The intrinsic parser, captured before user code runs: a script replacing JSON.parse changes nothing here.
        var parse = engine.GetValue("JSON").AsObject().Get("parse");
        foreach (var declaration in _globals)
        {
            Define(engine, declaration.Name, Function(engine, options, parse, declaration, declaration.Name, context, cancellationToken));
        }

        foreach (var (root, members) in _namespaces)
        {
            var target = engine.Intrinsics.Object.Construct([]);
            foreach (var (member, declaration) in members)
            {
                target.DefineOwnProperty(member, new PropertyDescriptor(
                    Function(engine, options, parse, declaration, member, context, cancellationToken),
                    writable: false, enumerable: true, configurable: false));
            }

            target.PreventExtensions();
            Define(engine, root, target);
        }
    }

    private static ClrFunction Function(
        Engine engine,
        FeatureOptions options,
        JsValue parse,
        JavaScriptHostFunctionDeclaration declaration,
        string name,
        JavaScriptHostFunctionContext context,
        CancellationToken cancellationToken) =>
        new(engine, name, (_, arguments) =>
        {
            var values = new JsonElement[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
                values[index] = Argument(engine, options, declaration.Name, index, arguments[index]);
            WarnLossyArguments(declaration.Name, values, context);

            JsonElement result;
            try
            {
                result = declaration.Implementation(new JavaScriptHostFunctionCall(values, context.Variables, cancellationToken));
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or JavaScriptException or OutOfMemoryException))
            {
                throw new JavaScriptException(engine.Intrinsics.Error, $"{declaration.Name}: {exception.Message}");
            }

            return result.ValueKind == JsonValueKind.Undefined
                ? JsValue.Undefined
                : engine.Invoke(parse, JsValue.Undefined, [result.GetRawText()]);
        });

    /// <summary>
    /// A number argument that lost precision when it entered JavaScript (spec 1223 FR-028, ADR 0046 V9): the call runs
    /// with the nearest value JavaScript holds, and a warning names the function and the argument path, never a value.
    /// The function does not turn the number into a string. One warning per function and set of paths in an evaluation.
    /// </summary>
    private static void WarnLossyArguments(string function, IReadOnlyList<JsonElement> arguments, JavaScriptHostFunctionContext context)
    {
        var paths = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
            CollectLossy(arguments[index], $"argument {index}", context.Values, paths);
        if (paths.Count == 0)
            return;

        var where = string.Join(", ", paths.Distinct(StringComparer.Ordinal));
        if (context.Warnings.Any(warning => warning.Details is { } details &&
                                            details.TryGetValue("function", out var known) && known == function &&
                                            details.TryGetValue("paths", out var knownPaths) && knownPaths == where))
            return;

        // A member path can name keys of the value: the protected form names the arguments only.
        var roots = string.Join(", ", paths.Select(path => path.Split('.')[0]).Distinct(StringComparer.Ordinal));
        string Message(string at) =>
            $"JavaScript passed numbers it cannot hold exactly to '{function}' ({at}); the function received the nearest value JavaScript holds. " +
            "Hold such numbers as strings to keep every digit.";
        context.Warnings.Add(new ExecutionWarning(
            JavaScriptValueDiagnostics.PrecisionLossRead,
            Message(where),
            ExecutionWarningSeverity.Warning,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["function"] = function, ["paths"] = where })
        {
            ProtectedForm = roots == where
                ? null
                : new ExecutionWarning(
                    JavaScriptValueDiagnostics.PrecisionLossRead,
                    Message(roots),
                    ExecutionWarningSeverity.Warning,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["function"] = function, ["paths"] = roots })
        });
        context.Logger?.LogWarning("{Code}: {Message}", JavaScriptValueDiagnostics.PrecisionLossRead, Message(roots));
    }

    private static void CollectLossy(JsonElement element, string path, IScriptValueEvaluation values, List<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetDouble(out var number) && values.TryFindLoss(number, out _):
                paths.Add(path);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectLossy(property.Value, $"{path}.{property.Name}", values, paths);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    CollectLossy(item, $"{path}[{index++}]", values, paths);
                break;
        }
    }

    // A script value maps to JSON as a script result does (FR-011): a Date becomes its ISO text, undefined null.
    private static JsonElement Argument(Engine engine, FeatureOptions options, string function, int index, JsValue value)
    {
        if (value.IsUndefined())
            return JsonSerializer.SerializeToElement<object?>(null);
        try
        {
            return JintResultMaterializer.Stringify(engine, value) is { } json
                ? JintResultMaterializer.ParseResult(json, options)
                : throw new InvalidOperationException("It has no JSON form (a function or a symbol).");
        }
        catch (InvalidOperationException exception)
        {
            throw new JavaScriptException(engine.Intrinsics.TypeError,
                $"{function}: argument {index} is not a JSON value. {exception.Message}");
        }
    }

    // A global the engine already holds read-only cannot be replaced; failing beats a silently missing function.
    private static void Define(Engine engine, string name, JsValue value)
    {
        if (!engine.Global.DefineOwnProperty(name, new PropertyDescriptor(value, writable: false, enumerable: false, configurable: false)))
            throw new InvalidOperationException($"JavaScript function '{name}' could not be installed: the engine already defines '{name}'.");
    }

    /// <summary>
    /// Whether no host function may use <paramref name="name"/> as its namespace root: a read root, a built-in name, a
    /// global the platform installs (<c>complete</c>, <c>getVariable</c>) or a standard JavaScript global.
    /// </summary>
    internal static bool IsReservedRoot(string name) =>
        JavaScriptBuiltInFunctions.ReadRoots.Contains(name, StringComparer.Ordinal) ||
        JavaScriptBuiltInFunctions.Names.Contains(name) ||
        EngineGlobals.Contains(name) ||
        name.StartsWith("__elsa", StringComparison.Ordinal);

    private static readonly HashSet<string> EngineGlobals = new(StringComparer.Ordinal)
    {
        JavaScriptScriptBuiltIns.Complete, "getVariable",
        "globalThis", "undefined", "NaN", "Infinity", "eval", "isFinite", "isNaN", "parseFloat", "parseInt",
        "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "escape", "unescape",
        "String", "Number", "Boolean", "Array", "Object", "Date", "Math", "JSON", "Intl", "Temporal", "RegExp", "Map", "Set",
        "WeakMap", "WeakSet", "WeakRef", "Symbol", "BigInt", "Promise", "Error", "Function", "Proxy", "Reflect",
        "ArrayBuffer", "SharedArrayBuffer", "DataView", "Atomics", "Iterator", "AggregateError", "EvalError", "RangeError",
        "ReferenceError", "SyntaxError", "TypeError", "URIError", "FinalizationRegistry", "DisposableStack",
        "AsyncDisposableStack", "SuppressedError", "Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array",
        "Uint16Array", "Int32Array", "Uint32Array", "Float16Array", "Float32Array", "Float64Array", "BigInt64Array",
        "BigUint64Array",
    };

    private static bool IsIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] is '_' or '$') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
}
