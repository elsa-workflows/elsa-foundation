using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// Complete and End. In a plain workflow, a node in the workflow's root flowchart with at most one literal outcome
/// becomes the <c>finish</c> intrinsic (End finishes with <c>Done</c>); anywhere else it is unconvertible, because
/// <c>finish</c> ends the whole workflow. Reusable workflows emit the graph <c>Complete</c> activity instead (plan
/// D-1 to D-4), handled by <see cref="ApplyReusableAsync"/>, with one literal outcome or several (spec 1208 FR-029).
/// </summary>
internal sealed class Elsa3TerminalPlacementRewrite : IElsa3NodeRewrite
{
    internal const string Done = "Done";
    private const string RootFlowchartType = "Elsa.Flowchart";
    private const string EndType = "Elsa.End";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.TerminalPlacement;

    public async ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        if (!TryReadOutcomes(context, out var outcomes, out var property))
            return;

        var parent = context.Node.Parent;
        var inRootFlowchart = parent is not null &&
                              ReferenceEquals(parent, context.Plan.Definition.Root) &&
                              StringComparer.Ordinal.Equals(parent.Type, RootFlowchartType);

        // Elsa 3 End ends only its enclosing flowchart (a terminal node), so it converts only where that is the whole
        // definition: its root flowchart. This narrows spec 236 D-3 for reusable workflows (spec 239 FR-011).
        if (StringComparer.Ordinal.Equals(context.Activity.Type, EndType) && !inRootFlowchart)
        {
            context.Error(UnsupportedMode,
                "Elsa 3 'End' ends only its enclosing flowchart, and outside a flowchart it does nothing; Elsa 4 has no activity that does either.",
                "Remove the End, and let the flowchart end when its last activity completes, before converting.");
            return;
        }

        if (context.IsReusable)
        {
            await ApplyReusableAsync(context, outcomes, cancellationToken);
            return;
        }

        if (!inRootFlowchart)
        {
            context.Error(UnsupportedMode,
                $"Elsa 3 '{context.Activity.Type}' ends only its enclosing container here; Elsa 4 'finish' would end the whole workflow.",
                "Move the node into the workflow's root flowchart, or end the container with its natural completion.",
                property);
            return;
        }

        // A workflow that is not reusable ends with Elsa 4 'finish', which names one outcome. Several outcomes convert
        // only in a reusable workflow, whose graph Complete passes all of them to its caller (spec 1208 FR-029).
        if (outcomes.Count > 1)
        {
            context.Error(UnsupportedMode,
                $"Elsa 3 completes with several outcomes ({string.Join(", ", outcomes)}); a workflow that is not reusable ends with Elsa 4 'finish', which names one outcome.",
                "Complete with one outcome, or mark the workflow usable as an activity, so its callers route on every outcome it completes with.",
                property);
            return;
        }

        context.SetProperty(Elsa3IntrinsicContracts.OutcomeKey, Elsa3TypedValues.CreateLiteral("String", outcomes[0]));
    }

    /// <summary>The graph Complete activity spec 237 adds: it ends the nearest reusable activity from any depth.</summary>
    internal const string CompleteActivityTypeKey = "Elsa.Activities.Graph.Runtime.Activities.Complete";

    private const string CompleteOutcomeKey = "outcome";
    private const string CompleteOutcomesKey = "outcomes";

    /// <summary>
    /// The reusable-workflow branch (plan D-1 to D-4): at any depth, one graph <c>Complete</c> node whose literal
    /// <c>outcome</c> is the outcome's reference key (<c>Done</c> → <c>done</c>), or whose literal <c>outcomes</c> list
    /// holds the reference keys of several outcomes (spec 1208 FR-029). Each outcome is recorded so the converter declares
    /// it on the activity contract (D-5 to D-7).
    /// </summary>
    private static async ValueTask ApplyReusableAsync(Elsa3RewriteContext context, IReadOnlyList<string> outcomes, CancellationToken cancellationToken)
    {
        var resolution = await context.Resolver.ResolveCatalogTypeKeyAsync(CompleteActivityTypeKey, cancellationToken);
        if (resolution.Failure is { } failure)
        {
            context.Error(failure.Code, failure.Message, failure.Guidance);
            return;
        }

        context.Activity.AdditionalProperties.Remove(Elsa3IntrinsicContracts.OutcomeKey);
        // The Elsa 3 member shares its name with the Elsa 4 'outcomes' input; the Elsa 4 value replaces it.
        context.RemoveProperty("Outcomes");
        context.Node.Resolved = resolution.Activity! with { Entry = context.Node.Resolved?.Entry };
        context.Node.Intrinsic = null;
        if (outcomes.Count == 1)
        {
            context.SetProperty(CompleteOutcomeKey, Elsa3TypedValues.CreateLiteral("String", Elsa3OutcomeKey.ReferenceKey(outcomes[0])));
        }
        else
        {
            var keys = new JsonArray(outcomes.Select(outcome => (JsonNode?)Elsa3OutcomeKey.ReferenceKey(outcome)).ToArray());
            context.SetProperty(CompleteOutcomesKey, Elsa3TypedValues.CreateLiteral("List<String>", keys));
        }

        foreach (var outcome in outcomes)
            context.Plan.CompleteOutcomes.Add((outcome, Elsa3ConversionDiagnostics.NodeId(context.Activity), context.Node.JsonPath));
    }

    /// <summary>
    /// The literal outcomes a Complete names, distinct and in order (<c>Done</c> when none; End always <c>Done</c>), or
    /// reports why they cannot be read. An outcome given by an expression only Elsa 3 can evaluate stays refused.
    /// </summary>
    private static bool TryReadOutcomes(Elsa3RewriteContext context, out IReadOnlyList<string> outcomes, out string property)
    {
        outcomes = [Done];
        if (!context.TryGetProperty("Outcomes", out property, out var value) || !Elsa3TypedValues.IsBound(value))
            return true;

        if (!TryReadNameList(value, out var text, out var names))
        {
            context.Error(UnsupportedMode,
                "The outcome is set by an expression; Elsa 4 needs literal outcomes.",
                "Name the outcomes literally.",
                property);
            return false;
        }

        if (names is null)
        {
            context.Error(UnsupportedMode,
                $"The outcomes '{text}' cannot be read as a list of outcome names.",
                "Name one outcome, or a JSON list of outcome names.",
                property);
            return false;
        }

        var distinct = names.Distinct(StringComparer.Ordinal).ToArray();
        outcomes = distinct.Length == 0 ? [Done] : distinct;
        return true;
    }

    /// <summary>
    /// Reads a list of names (Complete outcomes, FlowFork branches) from a literal or an Object expression: one name, or a
    /// JSON string or string array. False when the value is any other expression, which only Elsa 3 can evaluate;
    /// <paramref name="names"/> is null when the value is literal but not a name list.
    /// </summary>
    internal static bool TryReadNameList(JsonElement value, out string text, out IReadOnlyList<string>? names)
    {
        text = string.Empty;
        names = null;
        string? read = null;
        if (Elsa3TypedValues.TryGetLiteral(value, out var literal))
            read = literal;
        else if (Elsa3TypedValues.TryGetExpression(value, out var type, out var expression) &&
                 string.Equals(type, "Object", StringComparison.OrdinalIgnoreCase))
            read = expression.ValueKind == JsonValueKind.String ? expression.GetString() : expression.GetRawText();

        if (read is null)
            return false;
        text = read;
        names = ParseOutcomes(read);
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
