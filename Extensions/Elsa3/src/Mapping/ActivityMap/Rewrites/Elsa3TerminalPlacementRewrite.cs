using System.Text.Json;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// Complete and End. In a plain workflow, a node in the workflow's root flowchart with at most one literal outcome
/// becomes the <c>finish</c> intrinsic (End finishes with <c>Done</c>); anywhere else it is unconvertible, because
/// <c>finish</c> ends the whole workflow. Reusable workflows emit the graph <c>Complete</c> activity instead (plan
/// D-1 to D-4), handled by <see cref="ApplyReusableAsync"/>.
/// </summary>
internal sealed class Elsa3TerminalPlacementRewrite : IElsa3NodeRewrite
{
    internal const string Done = "Done";
    private const string RootFlowchartType = "Elsa.Flowchart";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.TerminalPlacement;

    public async ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (!TryReadOutcome(context, out var outcome, out var property))
            return;

        if (context.IsReusable)
        {
            await ApplyReusableAsync(context, outcome, cancellationToken);
            return;
        }

        var parent = context.Node.Parent;
        var inRootFlowchart = parent is not null &&
                              ReferenceEquals(parent, context.Plan.Definition.Root) &&
                              StringComparer.Ordinal.Equals(parent.Type, RootFlowchartType);
        if (!inRootFlowchart)
        {
            context.Error(UnsupportedMode,
                $"Elsa 3 '{context.Activity.Type}' ends only its enclosing container here; Elsa 4 'finish' would end the whole workflow.",
                "Move the node into the workflow's root flowchart, or end the container with its natural completion.",
                property);
            return;
        }

        context.SetProperty(Elsa3IntrinsicContracts.OutcomeKey, Elsa3TypedValues.CreateLiteral("String", outcome));
    }

    /// <summary>The graph Complete activity spec 237 adds: it ends the nearest reusable activity from any depth.</summary>
    internal const string CompleteActivityTypeKey = "Elsa.Activities.Graph.Runtime.Activities.Complete";

    /// <summary>
    /// The reusable-workflow branch (plan D-1 to D-4): at any depth, one graph <c>Complete</c> node whose literal
    /// <c>outcome</c> is the outcome's reference key (<c>Done</c> → <c>done</c>). The outcome is recorded so the
    /// converter declares it on the activity contract (D-5 to D-7).
    /// </summary>
    private static async ValueTask ApplyReusableAsync(Elsa3RewriteContext context, string outcome, CancellationToken cancellationToken)
    {
        var resolution = await context.Resolver.ResolveCatalogTypeKeyAsync(CompleteActivityTypeKey, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            context.Error(failure.Code, failure.Message, failure.Guidance);
            return;
        }

        context.Activity.AdditionalProperties.Remove(Elsa3IntrinsicContracts.OutcomeKey);
        context.Node.Resolved = resolution.Activity! with { Entry = context.Node.Resolved?.Entry };
        context.Node.Intrinsic = null;
        context.SetProperty("outcome", Elsa3TypedValues.CreateLiteral("String", Elsa3OutcomeKey.ReferenceKey(outcome)));
        context.Plan.CompleteOutcomes.Add((outcome, Elsa3ConversionDiagnostics.NodeId(context.Activity), context.Node.JsonPath));
    }

    /// <summary>The single literal outcome a Complete names (Done when none; End always Done), or reports why there is none.</summary>
    private static bool TryReadOutcome(Elsa3RewriteContext context, out string outcome, out string property)
    {
        outcome = Done;
        if (!context.TryGetProperty("Outcomes", out property, out var value) || !Elsa3TypedValues.IsBound(value))
            return true;

        string? text = null;
        if (Elsa3TypedValues.TryGetLiteral(value, out var literal))
            text = literal;
        else if (Elsa3TypedValues.TryGetExpression(value, out var type, out var expression) &&
                 string.Equals(type, "Object", StringComparison.OrdinalIgnoreCase))
            text = expression.ValueKind == JsonValueKind.String ? expression.GetString() : expression.GetRawText();

        if (text is null)
        {
            context.Error(UnsupportedMode,
                "The outcome is set by an expression; Elsa 4 needs one literal outcome.",
                "Use one literal outcome.",
                property);
            return false;
        }

        var outcomes = ParseOutcomes(text);
        if (outcomes is null || outcomes.Count > 1)
        {
            context.Error(UnsupportedMode,
                outcomes is null
                    ? $"The outcomes '{text}' cannot be read as one outcome."
                    : $"Elsa 3 completes with several outcomes ({string.Join(", ", outcomes)}); Elsa 4 completes with exactly one.",
                "Complete with one outcome, and route the others from the caller.",
                property);
            return false;
        }

        outcome = outcomes.Count == 0 ? Done : outcomes[0];
        return true;
    }

    private static IReadOnlyList<string>? ParseOutcomes(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return [];
        if (!trimmed.StartsWith('[') && !trimmed.StartsWith('"'))
            return [trimmed];
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => [document.RootElement.GetString()!],
                JsonValueKind.Array when document.RootElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) =>
                    document.RootElement.EnumerateArray().Select(item => item.GetString()!).Where(item => item.Length > 0).ToArray(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
