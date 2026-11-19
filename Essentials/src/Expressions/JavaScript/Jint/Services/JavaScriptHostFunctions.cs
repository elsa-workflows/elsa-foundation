using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;

namespace Elsa.Expressions.JavaScript.Jint.Services;

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
            if (JavaScriptBuiltInFunctions.IsReservedRoot(parts[0]))
                throw new InvalidOperationException(
                    $"JavaScript function '{name}' uses the reserved name '{parts[0]}' as its namespace. Read roots, built-in function names and JavaScript globals cannot be namespaces.");
        }
    }

    /// <summary>Installs every function as a non-writable global of <paramref name="engine"/>.</summary>
    public void Install(Engine engine, FeatureOptions options, IExpressionVariableReader? variables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        if (_globals.Count == 0 && _namespaces.Count == 0)
            return;

        // The intrinsic parser, captured before user code runs: a script replacing JSON.parse changes nothing here.
        var parse = engine.GetValue("JSON").AsObject().Get("parse");
        foreach (var declaration in _globals)
        {
            engine.Global.DefineOwnProperty(declaration.Name, new PropertyDescriptor(
                Function(engine, options, parse, declaration, declaration.Name, variables, cancellationToken),
                writable: false, enumerable: false, configurable: false));
        }

        foreach (var (root, members) in _namespaces)
        {
            var target = engine.Intrinsics.Object.Construct([]);
            foreach (var (member, declaration) in members)
            {
                target.DefineOwnProperty(member, new PropertyDescriptor(
                    Function(engine, options, parse, declaration, member, variables, cancellationToken),
                    writable: false, enumerable: true, configurable: false));
            }

            target.PreventExtensions();
            engine.Global.DefineOwnProperty(root, new PropertyDescriptor(target, writable: false, enumerable: false, configurable: false));
        }
    }

    private static ClrFunction Function(
        Engine engine,
        FeatureOptions options,
        JsValue parse,
        JavaScriptHostFunctionDeclaration declaration,
        string name,
        IExpressionVariableReader? variables,
        CancellationToken cancellationToken) =>
        new(engine, name, (_, arguments) =>
        {
            var values = new JsonElement[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
                values[index] = Argument(engine, options, declaration.Name, index, arguments[index]);

            JsonElement result;
            try
            {
                result = declaration.Implementation(new JavaScriptHostFunctionCall(values, variables, cancellationToken));
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or JavaScriptException or OutOfMemoryException))
            {
                throw new JavaScriptException(engine.Intrinsics.Error, $"{declaration.Name}: {exception.Message}");
            }

            return result.ValueKind == JsonValueKind.Undefined
                ? JsValue.Undefined
                : engine.Invoke(parse, JsValue.Undefined, [result.GetRawText()]);
        });

    // A script value maps to JSON as a script result does (FR-011): a Date becomes its ISO text, undefined null.
    private static JsonElement Argument(Engine engine, FeatureOptions options, string function, int index, JsValue value)
    {
        if (value.IsUndefined())
            return JsonSerializer.SerializeToElement<object?>(null);
        try
        {
            return JintResultMaterializer.Materialize(engine, value, options);
        }
        catch (InvalidOperationException exception)
        {
            throw new JavaScriptException(engine.Intrinsics.TypeError,
                $"{function}: argument {index} is not a JSON value. {exception.Message}");
        }
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] is '_' or '$') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
}
