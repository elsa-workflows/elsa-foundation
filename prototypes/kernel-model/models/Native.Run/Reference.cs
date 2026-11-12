using static Kernel.Native.Binding;
using static Kernel.Native.Wf;

namespace Kernel.Native.Run;

/// <summary>
/// The reference workflows of the current core's cost measurement (Essentials/tests/Activities/Http/IntegrationTests/
/// CommandBudgets/ReferenceWorkflows.cs), rebuilt as native definitions, and the workflows of the HTTP cases.
/// </summary>
public static class Reference
{
    public const string HttpPath = "budget/http-reference";
    public const string EventName = "budget-resume";

    public static Workflow WriteLines(int lines) =>
        Workflow($"writeline-sequence-{lines}", Sequence("sequence", [.. Enumerable.Range(1, lines).Select(i => WriteLine($"write-{i}", $"line {i}"))]));

    /// <summary>Today's shape: a Parallel of a WriteLine branch and an Event branch; the join completes the workflow.</summary>
    public static Workflow ForkJoinResume() =>
        Workflow("fork-join-resume", Parallel("fork-join", null, WriteLine("write-in-branch", "forked"), Event("wait-for-event", Expr("input.event"))));

    /// <summary>The brief's shape: three branches that each wait for an event, then a join and an activity after it.</summary>
    public static Workflow ForkJoinEvents(int branches = 3) =>
        Workflow($"fork-join-{branches}-events", Sequence("root",
            Parallel("fork-join", null, [.. Enumerable.Range(1, branches).Select(i => Event($"wait-{i}", Expr($"input.event + '-{i}'")))]),
            WriteLine("after-join", "joined")));

    /// <summary>Today's shape: an HttpEndpoint that captures the request and its content, a variable set from them, the response.</summary>
    public static Workflow HttpReference() =>
        Workflow("http-reference", Sequence("root",
                HttpEndpoint("http-in", HttpPath).Capture("request").Capture("content", "Body"),
                Set("set-reference-text", "referenceText", Expr("content.firstName + ' ' + content.lastName")),
                Respond("write-response", Var("referenceText"))),
            ("request", null), ("content", null), ("referenceText", null));

    /// <summary>Case 2: the workflow answers, then goes on with slow work and a variable.</summary>
    public static Workflow RespondThenContinue() =>
        Workflow("http-respond-continue", Sequence("root",
            HttpEndpoint("http-in", "orders/respond-continue").Capture("order", "Body"),
            Respond("accepted", Expr("'accepted ' + order.id")),
            Call("ship", "shipping").Capture("shipped"),
            Set("done", "output", Expr("'shipped ' + order.id"))));

    /// <summary>Case 3: off-step work comes before the response; the request keeps waiting.</summary>
    public static Workflow WaitBeforeResponse() =>
        Workflow("http-wait-before", Sequence("root",
            HttpEndpoint("http-in", "quotes"),
            Call("quote", "pricing", milliseconds: 20).Capture("quote"),
            Respond("answer", Var("quote"))));

    /// <summary>Case 3 with a short Delay instead of off-step work.</summary>
    public static Workflow DelayBeforeResponse() =>
        Workflow("http-delay-before", Sequence("root",
            HttpEndpoint("http-in", "slow"),
            Delay("pause", 20),
            Respond("answer", "after a pause")));

    /// <summary>Case 4: a request starts it; it answers, then waits in the middle for a second request on its own route.</summary>
    public static Workflow ApproveOrder() =>
        Workflow("http-approve", Sequence("root",
                HttpEndpoint("http-in", "orders").Capture("order", "Body"),
                Respond("accepted", Expr("'waiting for approval of ' + order.id")),
                HttpEndpoint("approve-in", Expr("'orders/' + order.id + '/approve'")).Capture("approval", "Body"),
                Set("approved", "output", Expr("'approved ' + order.id + ' by ' + approval.by")),
                Respond("approved-answer", Var("output"))),
            ("order", null), ("approval", null), ("output", null));

    public static WorkflowRegistry Registry()
    {
        var registry = new WorkflowRegistry();
        foreach (var lines in new[] { 5, 10, 20, 40 }) registry.Add(WriteLines(lines));
        registry.Add(ForkJoinResume()).Add(ForkJoinEvents()).Add(HttpReference()).Add(RespondThenContinue()).Add(WaitBeforeResponse())
            .Add(DelayBeforeResponse()).Add(ApproveOrder());
        foreach (var workflow in Smoke.Workflows()) registry.Add(workflow);
        return registry;
    }
}
