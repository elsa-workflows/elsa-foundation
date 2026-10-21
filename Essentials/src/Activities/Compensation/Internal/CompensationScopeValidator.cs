using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Activity-owned Draft validator for compensation scopes: every step needs a unique name and a forward activity, and
/// the handler retry inputs (spec 261) must be in range when they are literals. Errors do not block saving the Draft;
/// the promotion gate refuses to publish it. The compile-time guard in <see cref="CompensationScopeStructureHandler"/>
/// and the runtime navigator are backstops; an expression's value is clamped into range at run time instead.
/// </summary>
internal sealed class CompensationScopeValidator(IActivityStructureService activityStructureService) : IDraftValidator
{
    public const string BlankNameType = "CompensationScope/BlankStepName";
    public const string DuplicateNameType = "CompensationScope/DuplicateStepName";
    public const string MissingActivityType = "CompensationScope/MissingStepActivity";
    public const string InvalidRetryDelayType = "CompensationScope/InvalidRetryDelay";
    public const string InvalidBackoffMultiplierType = "CompensationScope/InvalidBackoffMultiplier";
    public const string InvalidRetryMaxDelayType = "CompensationScope/InvalidRetryMaxDelay";

    public const string RetryDelayInputKey = "handlerRetryDelay";
    public const string BackoffMultiplierInputKey = "handlerRetryBackoffMultiplier";
    public const string RetryMaxDelayInputKey = "handlerRetryMaxDelay";

    private const string LiteralExpressionType = "Literal";

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (!StringComparer.Ordinal.Equals(node.Structure?.Kind, CompensationScopeActivity.StructureKind))
                continue;

            var steps = CompensationScopeAuthoredStructureReader.Read(node).Steps.ToArray();
            for (var index = 0; index < steps.Length; index++)
            {
                if (CompensationStepRules.Normalize(steps[index].Name).Length == 0)
                    errors.Add(new ValidationError(StepPath(node, index), BlankNameType, $"Step {index + 1} needs a name."));
            }

            foreach (var duplicate in CompensationStepRules.DuplicateNames(steps.Select(step => step.Name)))
            {
                var index = Array.FindIndex(steps, step => StringComparer.Ordinal.Equals(CompensationStepRules.Normalize(step.Name), duplicate));
                errors.Add(new ValidationError(StepPath(node, index), DuplicateNameType, $"Step \"{duplicate}\" is defined more than once. Step names must be unique."));
            }

            for (var index = 0; index < steps.Length; index++)
            {
                if (steps[index].Activity is null)
                    errors.Add(new ValidationError(StepPath(node, index), MissingActivityType, $"Step \"{CompensationStepRules.Normalize(steps[index].Name)}\" has no activity."));
            }

            ValidateRetryInputs(node, errors);
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    // Spec 261 FR-509: a literal delay between 0 and 24 hours, a literal multiplier of at least 1, and a literal cap
    // between the delay and 24 hours. An unbound input takes its default; an expression is checked at run time, where its
    // value is clamped into the same range.
    private static void ValidateRetryInputs(ActivityNode node, List<ValidationError> errors)
    {
        var maxDelay = CompensationScopeActivity.MaxHandlerRetryDelay;

        // The delay the cap is compared with: its literal, the default when unbound, unknown for an expression or an
        // invalid literal (then only the cap's own range is checked).
        TimeSpan? delay = TimeSpan.Zero;
        if (ReadAuthored(node, RetryDelayInputKey) is { } authoredDelay)
        {
            delay = null;
            if (authoredDelay.IsLiteral)
            {
                if (TryReadDuration(authoredDelay.Value, out var value) && value >= TimeSpan.Zero && value <= maxDelay)
                    delay = value;
                else
                    errors.Add(new ValidationError(InputPath(node, RetryDelayInputKey), InvalidRetryDelayType, "Handler retry delay must be between 0 and 24 hours."));
            }
        }

        if (ReadAuthored(node, BackoffMultiplierInputKey) is { IsLiteral: true } multiplier &&
            !(TryReadNumber(multiplier.Value, out var factor) && double.IsFinite(factor) && factor >= 1))
            errors.Add(new ValidationError(InputPath(node, BackoffMultiplierInputKey), InvalidBackoffMultiplierType, "Handler retry backoff multiplier must be at least 1."));

        if (ReadAuthored(node, RetryMaxDelayInputKey) is { IsLiteral: true } cap &&
            !(TryReadDuration(cap.Value, out var capValue) && capValue >= (delay ?? TimeSpan.Zero) && capValue <= maxDelay))
            errors.Add(new ValidationError(InputPath(node, RetryMaxDelayInputKey), InvalidRetryMaxDelayType, "Handler retry max delay must be at least the retry delay and at most 24 hours."));
    }

    // The input's authored value, or null when it is unbound. A literal carries its value; an expression's value is known
    // only at run time.
    private static AuthoredInput? ReadAuthored(ActivityNode node, string key)
    {
        var input = node.Inputs.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, key));
        if (input is null || !input.IsBound() || input.Value is not { } value)
            return null;

        var isLiteral = string.IsNullOrWhiteSpace(value.ExpressionType) ||
                        string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase);
        return new AuthoredInput(isLiteral, value.Value);
    }

    private static bool TryReadDuration(object? value, out TimeSpan duration)
    {
        switch (value)
        {
            case TimeSpan span:
                duration = span;
                return true;
            case string text:
                return TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out duration);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return TimeSpan.TryParse(element.GetString()?.Trim(), CultureInfo.InvariantCulture, out duration);
            default:
                duration = default;
                return false;
        }
    }

    private static bool TryReadNumber(object? value, out double number)
    {
        switch (value)
        {
            case double literal:
                number = literal;
                return true;
            case float or int or long or decimal:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            case string text:
                return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetDouble(out number);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return double.TryParse(element.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            default:
                number = default;
                return false;
        }
    }

    private sealed record AuthoredInput(bool IsLiteral, object? Value);

    private static string InputPath(ActivityNode node, string key) => $"{node.NodeId}/inputs/{key}";

    // The path starts with the scope's node id, so the designer's "Go to" lands on the scope; the step index lets
    // the scope designer highlight the row.
    private static string StepPath(ActivityNode node, int index) => $"{node.NodeId}/steps/{index}";

    // Iterative depth-first walk over the activity tree. A reference-identity visited set guards against malformed
    // or cyclic Draft data without an arbitrary depth cap.
    private IEnumerable<ActivityNode> WalkTree(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
