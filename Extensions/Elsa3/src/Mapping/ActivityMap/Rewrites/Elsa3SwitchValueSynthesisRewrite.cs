using System.Text;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;
using Elsa3.Mapping.Conversion;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// Switch and FlowSwitch (R7): Elsa 3 evaluates each case's condition and takes the first true one (MatchFirst);
/// Elsa 4 compares one <c>Value</c> to each case's <c>match</c>. The rewrite synthesizes <c>Value</c> as a JavaScript
/// expression returning the label of the first true case, keys each case by its label, and routes case and default
/// activities into the <c>Switch.Case[{match}]</c> and <c>Switch.Default</c> slots. MatchAny, or a mode set by an
/// expression, is unconvertible.
/// </summary>
internal sealed class Elsa3SwitchValueSynthesisRewrite : IElsa3NodeRewrite
{
    private const string ValueKey = "Value";
    private const string CaseSlot = "Switch.Case";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.SwitchValueSynthesis;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (context.TryGetProperty("Mode", out var modeProperty, out var mode) && Elsa3TypedValues.IsBound(mode))
        {
            var isMatchFirst = Elsa3TypedValues.TryGetLiteral(mode, out var literal) &&
                               literal?.Trim() is "MatchFirst" or "matchFirst" or "0";
            if (!isMatchFirst)
            {
                context.Error(UnsupportedMode,
                    Elsa3TypedValues.IsExpression(mode)
                        ? "The switch mode is set by an expression; Elsa 4 takes the first matching case only."
                        : $"Switch mode '{literal}' has no Elsa 4 counterpart; Elsa 4 takes the first matching case only.",
                    "Set the mode to MatchFirst, or split the switch into separate decisions.",
                    modeProperty);
                return ValueTask.CompletedTask;
            }
        }

        if (!context.TryGetProperty("Cases", out var casesProperty, out var cases) || cases.ValueKind != JsonValueKind.Array)
            return ValueTask.CompletedTask;

        var labels = new HashSet<string>(StringComparer.Ordinal);
        var script = new StringBuilder("(function () {");
        var index = 0;
        foreach (var @case in cases.EnumerateArray())
        {
            var casePath = $"{casesProperty}[{index}]";
            var label = @case.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String
                ? labelElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(label) || !labels.Add(label))
            {
                context.Error(UnsupportedMode,
                    string.IsNullOrWhiteSpace(label)
                        ? $"Switch case {index} has no label; Elsa 4 keys each case by its label."
                        : $"Switch label '{label}' is used by more than one case; Elsa 4 requires distinct case labels.",
                    "Give every case a distinct label.",
                    casePath);
                index++;
                continue;
            }

            if (!TryCondition(@case, out var condition, out var reason))
            {
                context.Error(UnsupportedMode,
                    $"The condition of switch case '{label}' {reason}, so it cannot be combined into one Elsa 4 value.",
                    "Write the case condition as one JavaScript expression.",
                    $"{casePath}.condition");
                index++;
                continue;
            }

            // One case per line, so no case's source can reach into the next.
            script.Append("\n  if (").Append(condition).Append(") { return ").Append(JsonSerializer.Serialize(label)).Append("; }");
            if (@case.TryGetProperty("activity", out var activity) && activity.ValueKind == JsonValueKind.Object)
                context.AddChild(CaseSlot, label, activity, $"{context.Node.JsonPath}.{casesProperty}[{index}].activity");
            else
                context.Node.Slots.Add(new Elsa3SlotChild(CaseSlot, label, null));
            index++;
        }

        script.Append("\n  return null;\n})()");
        if (labels.Count > 0)
            context.SetProperty(ValueKey, Elsa3TypedValues.CreateExpression("String", "JavaScript", script.ToString()));
        return ValueTask.CompletedTask;
    }

    private static bool TryCondition(JsonElement @case, out string condition, out string reason)
    {
        condition = "false";
        reason = "is not JavaScript or a literal";
        if (!@case.TryGetProperty("condition", out var value) || !Elsa3TypedValues.IsBound(value))
            return true;
        if (Elsa3TypedValues.TryGetLiteral(value, out var literal))
        {
            condition = string.Equals(literal?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            return true;
        }

        if (Elsa3TypedValues.TryGetExpression(value, out var type, out var expression) &&
            string.Equals(type, "JavaScript", StringComparison.OrdinalIgnoreCase) &&
            expression.ValueKind == JsonValueKind.String)
        {
            return TryConditionExpression(expression.GetString() ?? string.Empty, out condition, out reason);
        }

        return false;
    }

    /// <summary>
    /// The case's single expression, without a statement terminator or trailing comment, which would otherwise break
    /// the combined script.
    /// </summary>
    private static bool TryConditionExpression(string source, out string condition, out string reason)
    {
        condition = "false";
        Script parsed;
        try
        {
            parsed = new Parser().ParseScript(source);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            reason = $"could not be parsed ({exception.Message})";
            return false;
        }

        if (parsed.Body is not [ExpressionStatement statement])
        {
            reason = "is not a single JavaScript expression";
            return false;
        }

        condition = $"({source[statement.Expression.Start..statement.Expression.End]})";
        reason = string.Empty;
        return true;
    }
}
