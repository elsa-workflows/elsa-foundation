using System.Globalization;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Builds the closed Jint value world shared by portable expressions and scripts.</summary>
internal static class IsolatedJintEngine
{
    public static Engine Create(FeatureOptions configured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var options = new JintOptions();

        if (configured.ExecutionTimeout is { } timeout && timeout > TimeSpan.Zero)
            options.TimeoutInterval(timeout);
        if (configured.MaxStatements is { } maxStatements && maxStatements > 0)
            options.MaxStatements(maxStatements);
        if (configured.MaxRecursionDepth is { } maxRecursionDepth && maxRecursionDepth > 0)
            options.LimitRecursion(maxRecursionDepth);
        if (cancellationToken.CanBeCanceled)
            options.CancellationToken(cancellationToken);

        var engine = new Engine(options);
        DisableAmbientCapabilities(engine);
        return engine;
    }

    public static void SetReadOnlyArgs(Engine engine, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(parameters);
        var args = engine.Intrinsics.Object.Construct([]);
        foreach (var parameter in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            args.DefineOwnProperty(
                parameter.Key,
                new PropertyDescriptor(CreateValue(engine, parameter.Value), writable: false, enumerable: true, configurable: false));
        }

        args.PreventExtensions();
        engine.Global.DefineOwnProperty("args", new PropertyDescriptor(args, writable: false, enumerable: true, configurable: false));
    }

    private static void DisableAmbientCapabilities(Engine engine)
    {
        // Time, randomness, locale, and environment data enter through pinned args, never intrinsics.
        foreach (var name in new[] { "Date", "Temporal", "Intl" })
            engine.Global.DefineOwnProperty(name, new PropertyDescriptor(JsValue.Undefined, writable: false, enumerable: false, configurable: false));

        engine.GetValue("Math").AsObject().DefineOwnProperty(
            "random",
            new PropertyDescriptor(JsValue.Undefined, writable: false, enumerable: false, configurable: false));
    }

    private static JsValue CreateValue(Engine engine, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => CreateObject(engine, element),
        JsonValueKind.Array => CreateArray(engine, element),
        JsonValueKind.String => JsValue.FromObject(engine, element.GetString()),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => JsValue.FromObject(engine, integer),
        JsonValueKind.Number => JsValue.FromObject(engine, element.GetDouble()),
        JsonValueKind.True => JsBoolean.True,
        JsonValueKind.False => JsBoolean.False,
        JsonValueKind.Null => JsValue.Null,
        _ => throw new InvalidOperationException($"JSON kind '{element.ValueKind}' is not a persistable JavaScript parameter value.")
    };

    private static JsValue CreateObject(Engine engine, JsonElement element)
    {
        var value = engine.Intrinsics.Object.Construct([]);
        foreach (var property in element.EnumerateObject())
        {
            value.DefineOwnProperty(
                property.Name,
                new PropertyDescriptor(CreateValue(engine, property.Value), writable: false, enumerable: true, configurable: false));
        }

        value.PreventExtensions();
        return value;
    }

    private static JsValue CreateArray(Engine engine, JsonElement element)
    {
        var items = element.EnumerateArray().Select(item => CreateValue(engine, item)).ToArray();
        var value = engine.Intrinsics.Array.Construct((uint)items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            value.DefineOwnProperty(
                index.ToString(CultureInfo.InvariantCulture),
                new PropertyDescriptor(items[index], writable: false, enumerable: true, configurable: false));
        }

        value.DefineOwnProperty("length", new PropertyDescriptor(JsNumber.Create(items.Length), writable: false, enumerable: false, configurable: false));
        value.PreventExtensions();
        return value;
    }
}
