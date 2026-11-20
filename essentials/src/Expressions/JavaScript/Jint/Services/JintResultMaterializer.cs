using System.Text;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Converts a closed-world Jint result to persistable JSON without CLR object materialization.</summary>
internal static class JintResultMaterializer
{
    private const string StringifySlot = "__elsaCanonicalJsonStringify";

    /// <summary>
    /// Captures the engine's intrinsic JSON serializer before user code can replace the mutable
    /// <c>JSON.stringify</c> property. The private slot is non-writable and non-configurable so result
    /// validation always observes the actual returned value.
    /// </summary>
    public static void Initialize(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var stringify = engine.GetValue("JSON").AsObject().Get("stringify");
        engine.Global.DefineOwnProperty(
            StringifySlot,
            new global::Jint.Runtime.Descriptors.PropertyDescriptor(
                stringify,
                writable: false,
                enumerable: false,
                configurable: false));
    }

    public static JsonElement Materialize(Engine engine, JsValue result, FeatureOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ValidatePositive(options.MaxResultBytes, nameof(options.MaxResultBytes));
        ValidatePositive(options.MaxResultDepth, nameof(options.MaxResultDepth));
        ValidatePositive(options.MaxResultNodes, nameof(options.MaxResultNodes));

        var serialized = engine.Invoke(
            engine.GetValue(StringifySlot),
            JsValue.Undefined,
            [result]);
        if (!serialized.IsString())
            throw new InvalidOperationException("A JavaScript result must be representable as persistable JSON.");

        var json = serialized.AsString();
        var byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > options.MaxResultBytes)
        {
            throw new InvalidOperationException(
                $"A JavaScript result exceeds the configured limit of {options.MaxResultBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = options.MaxResultDepth });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"A JavaScript result exceeds the configured JSON depth limit of {options.MaxResultDepth}.",
                exception);
        }

        using (document)
        {
            ValidateTree(document.RootElement, options, depth: 1, nodeCount: 0);

            return document.RootElement.Clone();
        }
    }

    private static int ValidateTree(JsonElement element, FeatureOptions options, int depth, int nodeCount)
    {
        if (depth > options.MaxResultDepth)
        {
            throw new InvalidOperationException(
                $"A JavaScript result exceeds the configured JSON depth limit of {options.MaxResultDepth}.");
        }

        nodeCount++;
        if (nodeCount > options.MaxResultNodes)
        {
            throw new InvalidOperationException(
                $"A JavaScript result exceeds the configured limit of {options.MaxResultNodes} nodes.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    nodeCount = ValidateTree(property.Value, options, depth + 1, nodeCount);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    nodeCount = ValidateTree(item, options, depth + 1, nodeCount);
                break;
        }

        return nodeCount;
    }

    private static void ValidatePositive(int value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"JavaScript sandbox option '{name}' must be greater than zero.");
    }
}
