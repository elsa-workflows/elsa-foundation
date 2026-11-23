using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Serialization.SystemText.Services;

/// <summary>Converts JSON through the selected target contract; explicit CLR extension converters own their non-JSON inputs.</summary>
internal sealed class ObjectConverter(IServiceProvider serviceProvider) : IObjectConverter
{
    public Result TryConvertTo<T>(object? value, ObjectConverterOptions? converterOptions = null) => TryConvertTo(value, typeof(T), converterOptions);
    public Result TryConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null)
    {
        try { return new(true, ConvertTo(value, targetType, converterOptions), null); }
        catch (Exception exception) { return new(false, null, exception); }
    }
    public T? ConvertTo<T>(object? value, ObjectConverterOptions? converterOptions = null) => (T?)ConvertTo(value, typeof(T), converterOptions);

    public object? ConvertTo(object? value, Type targetType, ObjectConverterOptions? converterOptions = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        if (value is null) return null; // Presence/admission belongs to the caller, including S-2.
        var contract = converterOptions?.ConversionContract ?? JsonConversionContract.For(targetType);
        var report = converterOptions?.ReportJsonDiagnostic;
        if (value is JsonElement element) return contract.Read(element, targetType, report);
        if (value is JsonNode node) return contract.Read(JsonSerializer.SerializeToElement(node, JsonBaseline.Options), targetType, report);

        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is string text)
        {
            // Text is a JSON string value, never an implicitly parsed document, number or Boolean.
            if (target == typeof(string)) return text;
            return contract.Read(JsonConversionContract.SerializeToElement(text), targetType, report);
        }
        if (value is DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan)
            return contract.Read(JsonConversionContract.SerializeToElement(value), targetType, report);
        if (targetType.IsInstanceOfType(value)) return value;
        if (IsIntegral(target) && value is double or float or decimal)
            throw new JsonException("A floating-point CLR value cannot be implicitly converted to an integer.");

        var extension = serviceProvider.GetServices<ITypeConverter>().FirstOrDefault(converter => converter.TargetType == targetType);
        if (extension is not null) return extension.Convert(value);

        // Serialize a CLR source once, then read the resulting JSON value. Never recursively feed document text
        // back through the text-value path, and never fall back to TypeDescriptor or Convert.ChangeType.
        var json = JsonConversionContract.SerializeToElement(value);
        return contract.Read(json, targetType, report);
    }

    private static bool IsIntegral(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
}
