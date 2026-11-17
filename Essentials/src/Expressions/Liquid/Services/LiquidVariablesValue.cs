using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Helpers;
using Fluid;
using Fluid.Values;

namespace Elsa.Expressions.Liquid.Services;

/// <summary>
/// The Liquid <c>variables</c> root (spec 1222 FR-005): <c>{{ variables.order }}</c> reads the value publish pinned for
/// <c>order</c>; on a template with a computed read (<c>{{ variables[key] }}</c>) any other name goes through the host's
/// recorded lookup. A computed name that matches nothing renders nil (FR-011); an unset declared variable is JSON
/// <c>null</c>, which Liquid also renders as nil. A refused read fails the render, and the host has recorded it.
/// </summary>
internal sealed class LiquidVariablesValue(
    IReadOnlyDictionary<string, JsonElement> pinned,
    IExpressionVariableReader? reader) : FluidValue
{
    private readonly Dictionary<string, FluidValue> _values = new(StringComparer.Ordinal);

    public override FluidValues Type => FluidValues.Object;

    public override ValueTask<FluidValue> GetValueAsync(string name, TemplateContext context) =>
        ValueTask.FromResult(Read(name, context));

    public override ValueTask<FluidValue> GetIndexAsync(FluidValue index, TemplateContext context) =>
        ValueTask.FromResult(Read(index.ToStringValue(), context));

    private FluidValue Read(string name, TemplateContext context)
    {
        if (_values.TryGetValue(name, out var known))
            return known;

        FluidValue value;
        if (pinned.TryGetValue(name, out var element))
            value = Create(element, context);
        else if (reader is null)
            value = NilValue.Instance;
        else
        {
            var read = reader.Read(name);
            value = read.Outcome switch
            {
                ExpressionVariableReadOutcome.Present => Create(read.Value, context),
                ExpressionVariableReadOutcome.Refused => throw new InvalidOperationException(read.Refusal),
                _ => NilValue.Instance
            };
        }

        _values[name] = value;
        return value;
    }

    private static FluidValue Create(JsonElement element, TemplateContext context) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? NilValue.Instance
            : FluidValue.Create(DynamicValueMaterializer.Materialize(element), context.Options);

    public override bool Equals(FluidValue other) => ReferenceEquals(this, other);

    public override bool ToBooleanValue() => true;

    public override decimal ToNumberValue() => 0;

    public override string ToStringValue() => "";

    public override object ToObjectValue() => this;

    public override void WriteTo(TextWriter writer, TextEncoder encoder, CultureInfo cultureInfo)
    {
    }

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
