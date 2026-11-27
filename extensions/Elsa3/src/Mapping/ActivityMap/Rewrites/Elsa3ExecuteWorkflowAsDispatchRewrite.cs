using Elsa3.Mapping.Conversion;
using Elsa3.Models;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// ExecuteWorkflow becomes a DispatchWorkflow that always waits (spec 1245 FR-008). Elsa 3 ran the child in process and
/// continued once it completed, or, with <c>WaitForCompletion</c> unset or false, as soon as it suspended; Elsa 4 runs the
/// child as a durable dispatch and waits for it to finish in every case. The rewrite emits <c>WaitForCompletion</c>
/// literal <c>true</c> and raises <c>ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE</c> once, adding that Elsa 4 also waits for a
/// child that suspends unless the Elsa 3 value was literal true (Elsa 3 ran an unset value as false).
/// </summary>
internal sealed class Elsa3ExecuteWorkflowAsDispatchRewrite : IElsa3NodeRewrite
{
    private const string WaitForCompletion = "WaitForCompletion";

    internal const string SuspendedChildSentence =
        " Elsa 4 waits for a child that suspends, where Elsa 3 continued.";

    public Elsa3NodeRewriteKind Kind => Elsa3NodeRewriteKind.ExecuteWorkflowAsDispatch;

    public ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken)
    {
        var waited = context.TryGetProperty(WaitForCompletion, out _, out var value) &&
                     value.ValueKind != System.Text.Json.JsonValueKind.Null &&
                     !Elsa3TypedValues.IsNullLiteral(value) &&
                     Elsa3TypedValues.TryGetLiteral(value, out var literal) &&
                     bool.TryParse(literal, out var parsed) && parsed;

        var type = context.Node.Resolved?.FindInput(WaitForCompletion)?.Type.Alias ?? "Boolean";
        context.SetProperty(WaitForCompletion, Elsa3TypedValues.CreateLiteral(type, "true"));
        context.Warning(
            Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable,
            Elsa3ActivityNormalizer.ExecuteWorkflowDurableMessage + (waited ? string.Empty : SuspendedChildSentence),
            "Review the converted workflow's behaviour for this difference.",
            null);
        return ValueTask.CompletedTask;
    }
}
