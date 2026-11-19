using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acornima;
using Acornima.Ast;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;
using Elsa3.Mapping.Conversion;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// Switch and FlowSwitch (R7). In MatchFirst mode Elsa 3 evaluates each case's condition and takes the first true one;
/// Elsa 4 compares one <c>Value</c> to each case's <c>match</c>. The rewrite synthesizes <c>Value</c> as a JavaScript
/// expression returning the label of the first true case, keys each case by its label, and routes case and default
/// activities into the <c>Switch.Case[{match}]</c> and <c>Switch.Default</c> slots. In MatchAny mode Elsa 3 takes every
/// true case; the Elsa 4 Switch in <c>MatchAny</c> mode does the same from one Boolean condition per case (spec 1208
/// FR-028), so each case keeps its own condition. A MatchAny case that holds an activity, or a mode set by an
/// expression, is unconvertible.
/// </summary>
internal sealed class Elsa3SwitchValueSynthesisRewrite : IElsa3NodeRewrite
{
    private const string ValueKey = "Value";
    private const string CaseSlot = "Switch.Case";
    private const string ModeField = "mode";
    private const string MatchAnyMode = "MatchAny";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.SwitchValueSynthesis;

    /// <summary>The node property that holds a MatchAny case's condition until the native structure takes it.</summary>
    internal static string ConditionProperty(string caseId) => $"Case:{caseId}:Condition";

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        var matchAny = false;
        if (context.TryGetProperty("Mode", out var modeProperty, out var mode) && Elsa3TypedValues.IsBound(mode))
        {
            var literal = Elsa3TypedValues.TryGetLiteral(mode, out var text) ? text?.Trim() : null;
            matchAny = literal is not null && (literal.Equals(MatchAnyMode, StringComparison.OrdinalIgnoreCase) || literal == "1");
            var matchFirst = literal is not null && (literal.Equals("MatchFirst", StringComparison.OrdinalIgnoreCase) || literal == "0");
            if (!matchAny && !matchFirst)
            {
                context.Error(UnsupportedMode,
                    literal is null
                        ? "The switch mode is set by an expression; Elsa 4 needs the mode when the workflow is published."
                        : $"Switch mode '{literal}' has no Elsa 4 counterpart; Elsa 4 takes the first matching case (MatchFirst) or every matching case (MatchAny).",
                    "Set the mode to MatchFirst or MatchAny literally, or split the switch into separate decisions.",
                    modeProperty);
                return ValueTask.CompletedTask;
            }
        }

        if (!context.TryGetProperty("Cases", out var casesProperty, out var cases) || cases.ValueKind != JsonValueKind.Array)
        {
            if (matchAny)
                context.Node.StructureFields[ModeField] = MatchAnyMode;
            return ValueTask.CompletedTask;
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        var script = new StringBuilder("(function () {");
        var index = 0;
        foreach (var @case in cases.EnumerateArray())
        {
            var caseIndex = $"[{index}]";
            var caseId = $"case-{index}";
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
                    casesProperty,
                    caseIndex);
                index++;
                continue;
            }

            if (!TryCondition(@case, out var condition, out var reason))
            {
                context.Error(UnsupportedMode,
                    matchAny
                        ? $"The condition of switch case '{label}' {reason}, so it cannot become an Elsa 4 case condition."
                        : $"The condition of switch case '{label}' {reason}, so it cannot be combined into one Elsa 4 value.",
                    "Write the case condition as one JavaScript expression.",
                    casesProperty,
                    $"{caseIndex}.condition");
                index++;
                continue;
            }

            var hasActivity = @case.TryGetProperty("activity", out var activity) && activity.ValueKind == JsonValueKind.Object;
            if (matchAny)
            {
                if (hasActivity)
                {
                    // Elsa 4 runs a case activity only as the one selected branch; several selected cases route through
                    // their outcome connections (SwitchModes.MatchAny).
                    context.Error(UnsupportedMode,
                        $"Switch case '{label}' holds an activity; Elsa 3 runs every matching case's activity, but an Elsa 4 Switch in MatchAny mode routes each matching case through its outcome connection and cannot hold case activities.",
                        "Use a FlowSwitch in a flowchart and connect each case to the activities it runs, or set the mode to MatchFirst.",
                        casesProperty,
                        $"{caseIndex}.activity");
                    index++;
                    continue;
                }

                // The condition is read with JavaScript truthiness, as the MatchFirst value script reads it, so both
                // modes select the same cases; the Elsa 4 condition input needs a Boolean, never null.
                var property = ConditionProperty(caseId);
                context.SetProperty(property, condition is "true" or "false"
                    ? Elsa3TypedValues.CreateLiteral("Boolean", JsonValue.Create(condition == "true"))
                    : Elsa3TypedValues.CreateExpression("Boolean", "JavaScript", $"!!{condition}"));
                context.Node.Slots.Add(new Elsa3SlotChild(CaseSlot, label, null, caseId, property));
                index++;
                continue;
            }

            // One case per line, so no case's source can reach into the next.
            script.Append("\n  if (").Append(condition).Append(") { return ").Append(JsonSerializer.Serialize(label)).Append("; }");
            if (hasActivity)
            {
                context.AddChild(CaseSlot, label, activity, $"{context.Node.JsonPath}.{casesProperty}[{index}].activity");
                context.Node.Slots[^1] = context.Node.Slots[^1] with { ItemId = caseId };
            }
            else
            {
                context.Node.Slots.Add(new Elsa3SlotChild(CaseSlot, label, null, caseId));
            }
            index++;
        }

        if (matchAny)
        {
            context.Node.StructureFields[ModeField] = MatchAnyMode;
            return ValueTask.CompletedTask;
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
