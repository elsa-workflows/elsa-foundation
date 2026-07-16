using System.Globalization;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Helpers;
using Elsa.Expressions.Core.Models;
using Fluid;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// Evaluates pure Liquid bindings from their complete, explicitly declared JSON parameter snapshot.
/// </summary>
/// <remarks>
/// This path deliberately does not use <see cref="LiquidTemplateManager"/>. The legacy manager builds
/// a template context around an <see cref="IExpressionExecutionContext"/> and publishes configuration
/// events; a binding expression receives neither of those ambient surfaces.
/// </remarks>
public sealed class PortableLiquidExpressionHandler(FluidParser parser) : IPortableExpressionHandler
{
    private static readonly HashSet<string> NumericAliases =
    [
        "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal"
    ];

    public string Language => "Liquid";

    public async ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var definition = request.Definition;
        if (!StringComparer.Ordinal.Equals(definition.Language, Language))
            throw new ArgumentException($"Expected a {Language} expression, but received '{definition.Language}'.", nameof(request));
        if (!request.Capabilities.IsBindingPure ||
            !StringComparer.Ordinal.Equals(request.Capabilities.Profile, ExpressionCapabilityProfiles.BindingPureV1))
            throw new InvalidOperationException($"Liquid binding evaluation requires capability profile '{ExpressionCapabilityProfiles.BindingPureV1}'.");
        if (!parser.TryParse(definition.Source, out var template, out var error))
            throw new InvalidOperationException($"Failed to parse Liquid binding expression: {error}");

        // A model-less context has no workflow/activity context, service provider, configuration, or
        // event pipeline. Only the immutable parameter roots copied below are visible to Fluid.
        var options = new TemplateOptions
        {
            Now = static () => DateTimeOffset.UnixEpoch,
            TimeZone = TimeZoneInfo.Utc,
            Undefined = path => throw new InvalidOperationException(
                $"Liquid binding expression referenced undeclared parameter '{path}'.")
        };
        foreach (var filter in new[] { "date", "format_date", "time_zone" })
        {
            options.Filters.AddFilter(filter, static (_, _, _) =>
                throw new InvalidOperationException("Liquid time and time-zone filters are unavailable in the binding-pure-v1 capability profile."));
        }
        var context = new TemplateContext(options, StringComparer.Ordinal)
        {
            CultureInfo = CultureInfo.InvariantCulture
        };
        foreach (var (name, value) in request.ParameterValues)
            context.SetValue(name, DynamicValueMaterializer.Materialize(value));

        var rendered = await template.RenderAsync(context);
        request.CancellationToken.ThrowIfCancellationRequested();
        return NormalizeResult(rendered, definition.ResultType.Alias);
    }

    private static JsonElement NormalizeResult(string rendered, string resultTypeAlias)
    {
        if (StringComparer.Ordinal.Equals(resultTypeAlias, "Boolean"))
        {
            if (!bool.TryParse(rendered.Trim(), out var value))
                throw new InvalidOperationException($"Liquid rendered '{rendered}' for a Boolean expression result.");

            return JsonSerializer.SerializeToElement(value);
        }

        if (NumericAliases.Contains(resultTypeAlias))
        {
            if (!TryParseJson(rendered, out var number) || number.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException($"Liquid rendered '{rendered}' for a numeric expression result of type '{resultTypeAlias}'.");

            return number;
        }

        if ((StringComparer.Ordinal.Equals(resultTypeAlias, "Object") || StringComparer.Ordinal.Equals(resultTypeAlias, "Any"))
            && TryParseJson(rendered, out var structuredValue))
            return structuredValue;

        return JsonSerializer.SerializeToElement(rendered);
    }

    private static bool TryParseJson(string value, out JsonElement result)
    {
        try
        {
            result = JsonSerializer.Deserialize<JsonElement>(value);
            return true;
        }
        catch (JsonException)
        {
            result = default;
            return false;
        }
    }
}
