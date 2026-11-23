using Elsa.Expressions.JavaScript.Jint.Services;
using Jint;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The outcomes argument of <c>complete(result, outcomes)</c> is copied in a CLR loop, outside Jint's statement and
/// cancellation limits, so the copy checks the evaluation's cancellation itself (local Code Review of PR #4314).
/// </summary>
public sealed class ScriptCompletionOutcomeSnapshotTests
{
    [Fact]
    public void A_cancelled_evaluation_stops_copying_the_outcomes_array()
    {
        var outcomes = new Engine().Evaluate("['Approved', 'Audit']");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            JintJavaScriptScriptEvaluator.ScriptCompletions.Snapshot(outcomes, cancellation.Token));
    }

    [Fact]
    public void An_uncancelled_evaluation_copies_every_outcome_in_order()
    {
        var outcomes = new Engine().Evaluate("['Approved', 'Audit']");

        var snapshot = JintJavaScriptScriptEvaluator.ScriptCompletions.Snapshot(outcomes, CancellationToken.None);

        Assert.Equal(new[] { "Approved", "Audit" }, snapshot.Names);
        Assert.True(snapshot.IsList);
        Assert.Null(snapshot.Invalid);
    }
}
