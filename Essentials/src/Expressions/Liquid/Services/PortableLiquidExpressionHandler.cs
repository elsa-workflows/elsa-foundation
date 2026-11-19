using System.Globalization;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid.Helpers;
using Elsa.Primitives.Models;
using Fluid;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// Evaluates pure Liquid bindings from their complete, explicitly declared JSON parameter snapshot.
/// </summary>
internal sealed class PortableLiquidExpressionHandler(FluidParser parser) : IPortableExpressionHandler
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
        if (!StringComparer.OrdinalIgnoreCase.Equals(definition.Language, Language))
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
        foreach (var (name, value) in request.ArgumentValues())
            context.SetValue(name, DynamicValueMaterializer.Materialize(value));

        // Spec 1222 FR-005: pinned variable reads and the computed-read lookup sit under one `variables` root, the
        // same surface JavaScript gets. A template that reads no variable gets none.
        var pinnedVariables = request.PinnedVariableValues();
        if (pinnedVariables.Count > 0 || request.VariableReader is not null)
        {
            // In Liquid the args parameters are roots too: one named `variables` cannot share the name.
            if (request.ArgumentValues().ContainsKey("variables"))
                throw new InvalidOperationException("A Liquid expression that reads workflow variables cannot also declare a parameter named 'variables'.");
            context.SetValue("variables", new LiquidVariablesValue(pinnedVariables, request.VariableReader));
        }

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

        if (TypeAliasConvention.IsAny(resultTypeAlias)
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
