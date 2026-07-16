using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Runtime.Descriptors;
using Microsoft.Extensions.Options;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes portable binding expressions in a fresh, closed Jint engine. It intentionally does not use
/// the legacy engine configurator, event, preprocessor, host-function, or CLR-access pipelines.
/// </summary>
internal sealed class JintPortableJavaScriptEvaluator(IOptions<FeatureOptions> featureOptions) : IPortableJavaScriptEvaluator
{
    public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var definition = request.Definition;
        if (!StringComparer.Ordinal.Equals(definition.CapabilityProfile, ExpressionCapabilityProfiles.BindingPureV1))
            throw new InvalidOperationException($"JavaScript binding evaluation requires capability profile '{ExpressionCapabilityProfiles.BindingPureV1}'.");
        if (definition.Options.EnumerateObject().Any())
            throw new InvalidOperationException("The JavaScript binding-pure-v1 evaluator does not support evaluator options.");

        var engine = CreateEngine(request.CancellationToken);
        var args = CreateArgs(engine, request.ParameterValues);
        engine.Global.DefineOwnProperty("args", new PropertyDescriptor(args, writable: false, enumerable: true, configurable: false));

        var result = engine.Evaluate($"\"use strict\"; ({definition.Source})");
        if (result.IsUndefined())
            throw new InvalidOperationException("A portable JavaScript expression cannot return undefined.");

        return ValueTask.FromResult(JsonSerializer.SerializeToElement(result.ToObject()));
    }

    private Engine CreateEngine(CancellationToken cancellationToken)
    {
        var configured = featureOptions.Value;
        var options = new JintOptions();

        if (configured.ExecutionTimeout is { } timeout && timeout > TimeSpan.Zero)
            options.TimeoutInterval(timeout);
        if (configured.MaxStatements is { } maxStatements && maxStatements > 0)
            options.MaxStatements(maxStatements);
        if (configured.MaxRecursionDepth is { } maxRecursionDepth && maxRecursionDepth > 0)
            options.LimitRecursion(maxRecursionDepth);
        if (cancellationToken.CanBeCanceled)
            options.CancellationToken(cancellationToken);

        return new Engine(options);
    }

    private static JsValue CreateArgs(Engine engine, IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var args = engine.Intrinsics.Object.Construct([]);
        foreach (var parameter in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var value = CreateValue(engine, parameter.Value);
            args.DefineOwnProperty(parameter.Key, new PropertyDescriptor(value, writable: false, enumerable: true, configurable: false));
        }

        args.PreventExtensions();
        return args;
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
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new PropertyDescriptor(items[index], writable: false, enumerable: true, configurable: false));
        }

        value.DefineOwnProperty("length", new PropertyDescriptor(JsNumber.Create(items.Length), writable: false, enumerable: false, configurable: false));
        value.PreventExtensions();
        return value;
    }
}
