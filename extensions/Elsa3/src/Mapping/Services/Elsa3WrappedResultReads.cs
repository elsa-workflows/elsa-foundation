using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// A read of a variable an Elsa 3 ExecuteWorkflow captured its <c>Result</c> into (spec 1245 FR-010). Elsa 3's value was
/// an <c>ExecuteWorkflowResult</c> wrapping the child's output; Elsa 4 captures the dispatch's <c>Output</c>, so each read
/// is rewritten to the Elsa 4 value it stood for. <paramref name="ProducerNodeId"/> is the capturing node, whose own
/// outputs the WorkflowInstanceId, SubStatus and whole-value rows read; <paramref name="ProducerReadIssue"/> is why that
/// node's outputs cannot be read from this consumer (another structural frame, or not before it), or null.
/// </summary>
public sealed record Elsa3WrappedResultRead(string ProducerNodeId, Elsa3ExpressionRewriteIssue? ProducerReadIssue);

/// <summary>The FR-010 rewrite of a wrapped Execute Workflow result read, and its refusal texts.</summary>
internal static class Elsa3WrappedResultReads
{
    /// <summary>The Elsa 4 DispatchWorkflow outputs the rewritten reads bind to.</summary>
    internal const string ChildWorkflowExecutionIdOutput = "ChildWorkflowExecutionId";
    internal const string StatusOutput = "Status";

    /// <summary>The members of Elsa 3's <c>ExecuteWorkflowResult</c>, in its key order.</summary>
    internal static readonly string[] Elsa3Members =
        ["WorkflowDefinitionVersionId", "WorkflowInstanceId", "CorrelationId", "Status", "SubStatus", "Output"];

    /// <summary>The argument names a rewritten read binds the capturing node's outputs under.</summary>
    internal static string InstanceIdArgument(string variableName) => $"{variableName}_{ChildWorkflowExecutionIdOutput}";

    internal static string StatusArgument(string variableName) => $"{variableName}_{StatusOutput}";

    /// <summary>
    /// Elsa 3's <c>SubStatus</c> from the dispatch's <c>Status</c> output: Completed is Finished, Cancelled is Cancelled, and
    /// Faulted and DispatchFailed are Faulted. The dispatch always waits, so Dispatched does not occur.
    /// </summary>
    internal static string SubStatus(string status) =>
        $"({status} === \"Completed\" ? \"Finished\" : {status} === \"Cancelled\" ? \"Cancelled\" : \"Faulted\")";

    /// <summary>The whole Elsa 3 value, rebuilt with Elsa 3's keys.</summary>
    internal static string WholeValue(string output, string instanceId, string status) =>
        $"({{ WorkflowDefinitionVersionId: null, WorkflowInstanceId: {instanceId}, CorrelationId: null, Status: \"Finished\", " +
        $"SubStatus: {SubStatus(status)}, Output: {output} }})";

    private const string Holds = "which holds an Execute Workflow result. Elsa 4 captures the dispatch's Output into it";

    internal static string ProducerReadGuidance(string guidance) =>
        guidance + " The read uses the Execute Workflow node's own outputs (WorkflowInstanceId, SubStatus or the whole value), " +
        "so in the Elsa 3 workflow read only .Output there, or move the read after the node in the same container, before import.";

    internal static Elsa3ExpressionRewriteIssue UnknownMember(string variable, string? member) => new(
        Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
        member is null
            ? $"JavaScript reads a computed member of variable '{variable}', {Holds}, and a computed member has no Elsa 4 counterpart."
            : $"JavaScript reads '{member}' of variable '{variable}', {Holds}. Elsa 3 had '{member}' only in the bare output dictionary it set after a child that suspended, so what the read means is ambiguous.",
        $"Read the child's output as .Output.<name>, or one of {string.Join(", ", Elsa3Members)}, with a literal member name in the Elsa 3 workflow, before import.");

    internal static Elsa3ExpressionRewriteIssue LiquidRead(string variable) => new(
        Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
        $"Liquid reads variable '{variable}', {Holds}; the Liquid conversion rewrites only .Output and .Output.<name>.",
        $"Read the child's output as Variables.{variable}.Output.<name>, or move the read into a JavaScript expression, in the Elsa 3 workflow before import.");

    internal static Elsa3ExpressionRewriteIssue LiquidOutputRoot(string node) => new(
        Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
        $"Liquid reads Execute Workflow '{node}''s result through the Output root; Elsa 4 cannot rebuild Elsa 3's result there.",
        "Capture the result into a variable and read Variables.<variable>.Output.<name> in the Elsa 3 workflow, before import.");

    internal static Elsa3ExpressionRewriteIssue ComputedRead(IReadOnlyList<string> variables) => new(
        Elsa3ActivityDiagnosticCodes.OutputNotRepresentable,
        $"JavaScript reads variables by a computed name, and {string.Join(", ", variables.Select(name => $"'{name}'"))} holds an Execute Workflow result. " +
        "Elsa 4 serves a computed read by a run-time lookup, which would read the dispatch's Output instead of Elsa 3's result.",
        $"Read {string.Join(", ", variables.Select(name => $"getVariable('{name}')"))} by its literal name in the Elsa 3 workflow, before import.");

    internal static (string Message, string Guidance) OtherWriter(string variable, string writer) => (
        $"Variable '{variable}' holds an Execute Workflow result, whose reads convert to the dispatch's Output, but {writer} also writes it, so its reads would see two shapes.",
        $"Capture the Execute Workflow result into a variable that nothing else writes, and remove {writer} or point it at another variable, in the Elsa 3 workflow before import.");

    internal static (string Message, string Guidance) NotAVariable(string memoryId) => (
        $"Execute Workflow's Result is captured into memory slot '{memoryId}', which is no workflow variable, so its reads cannot be rewritten to the dispatch's Output.",
        "Capture the result into a declared workflow variable in the Elsa 3 workflow, before import.");
}
