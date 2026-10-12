using System.Data.Common;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaGate;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 181, SC-004 and User Story 5: an evaluator finalizing version 2 races a host that joins reading only version 1,
/// through every interleaving of the evaluator's three steps (write the intent, read the fleet, commit or abandon) with
/// the joiner's three (publish its report, read the record, act on it). Whatever the order, the outcome is never
/// "version 2 finalized and the version-1 host active", and it is always one of the two safe outcomes: 2 finalized and
/// the joiner refused, or 2 still pending and the joiner admitted.
/// </summary>
/// <remarks>
/// Each step is a real call on a real database: a script releases the two actors one step at a time, at the point each
/// step begins, so the order is exactly the one named. The evaluator's fleet read before it writes the intent always
/// comes first, so every interleaving exercises an intent.
/// </remarks>
public sealed class EfSchemaFinalizationInterleavingTests : IAsyncLifetime
{
    private static readonly string[] EvaluatorSteps = ["e1", "e2", "e3"];
    private static readonly string[] JoinerSteps = ["j1", "j2", "j3"];
    private static readonly string RecordTable = EfSchemaFinalization.RecordTableName("ElsaGateTests");

    private readonly TemporarySqliteDatabase database = new("schema-interleaving");
    private readonly List<DbContext> contexts = [];

    public async Task InitializeAsync() => await Context().Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    public static TheoryData<string> Interleavings()
    {
        var data = new TheoryData<string>();
        foreach (var order in Merge(EvaluatorSteps, JoinerSteps))
            data.Add(string.Join(",", order));
        return data;
    }

    [Theory]
    [MemberData(nameof(Interleavings))]
    public async Task No_interleaving_finalizes_a_version_while_a_host_that_cannot_read_it_activates(string interleaving)
    {
        var fleet = new FakeFleetState();
        var evaluatorFleet = new FakeFleet(fleet, fleet.Add(new FakeMember("evaluator").Reading(Family, "1", "2").Reading(OtherFamily, "1")));
        var joinerFleet = new FakeFleet(fleet, fleet.Add(new FakeMember("joiner").Reading(Family, "1").Reading(OtherFamily, "1")));
        var evaluator = Gate(Families("2"), evaluatorFleet);
        await AdmitEvaluatorAtOneAsync(evaluator);

        var script = new Script(["e0", .. interleaving.Split(',')]);
        var baseline = evaluatorFleet.Counts;
        evaluatorFleet.BeforeCount = count => (count - baseline) switch
        {
            1 => script.ReachAsync("evaluator", "e0"),
            2 => script.ReachAsync("evaluator", "e2"),
            _ => Task.CompletedTask
        };
        var joinerPublishes = 0;
        joinerFleet.BeforePublish = () => ++joinerPublishes == 1 ? script.ReachAsync("joiner", "j1") : Task.CompletedTask;
        var evaluatorContext = Context(new RecordCommands(select => select switch
        {
            2 => script.ReachAsync("evaluator", "e1"),
            3 => script.ReachAsync("evaluator", "e3"),
            _ => Task.CompletedTask
        }));
        var joinerContext = Context(new RecordCommands(command => command switch
        {
            1 => script.ReachAsync("joiner", "j2"),
            2 => script.ReachAsync("joiner", "j3"),
            _ => Task.CompletedTask
        }));

        var evaluation = Run(() => evaluator.EvaluateAsync(evaluatorContext), () => script.Finish("evaluator", ["e0", .. EvaluatorSteps]));
        var joining = Run(() => Gate(Families("1"), joinerFleet).ActivateAsync(joinerContext), () => script.Finish("joiner", JoinerSteps));
        // A refused joiner is an outcome to judge below, not a failure of the run.
        await Task.WhenAny(Task.WhenAll(evaluation, joining), Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(evaluation.IsCompleted && joining.IsCompleted, $"{interleaving}: the actors did not finish.");

        var finalized = (await new EfSchemaFinalizationStore(Context()).FindAsync(Family))!.FinalizedVersion;
        var joinerRefused = joining.Exception?.InnerException is EfSchemaActivationRefusedException { Refusal: EfSchemaActivationRefusal.FinalizedUnreadable };
        Assert.True(evaluation.IsCompletedSuccessfully, $"{interleaving}: the evaluator failed: {evaluation.Exception}");
        Assert.True(joining.IsCompletedSuccessfully || joinerRefused, $"{interleaving}: the joiner failed otherwise: {joining.Exception}");
        Assert.False(finalized == "2" && !joinerRefused, $"{interleaving}: 2 was finalized while the host that reads only 1 activated.");
        Assert.True(finalized == "2" ? joinerRefused : !joinerRefused, $"{interleaving}: 2 was not finalized, yet the joiner was refused.");
        Assert.Equal(ExpectedFinalization(interleaving), finalized);
    }

    /// <summary>
    /// A confirming read (e2) after the joiner published (j1) counts it, so the evaluator abandons. Otherwise the joiner
    /// published after the intent was durable, so it reads the intent (j2) and abandons it (j3), and whichever of that and
    /// the evaluator's commit (e3) runs first decides.
    /// </summary>
    private static string ExpectedFinalization(string interleaving)
    {
        var order = interleaving.Split(',').ToList();
        if (order.IndexOf("j1") < order.IndexOf("e2"))
            return "1";
        return order.IndexOf("e3") < order.IndexOf("j3") ? "2" : "1";
    }

    private async Task AdmitEvaluatorAtOneAsync(EfSchemaModuleGate evaluator)
    {
        var store = new EfSchemaFinalizationStore(Context());
        var created = await store.GetOrCreateAsync(Family, "1", ["1", "2"], SchemaFinalizationActor.OfOperator("ops"));
        await store.PlaceHoldAsync(Family, created.Revision, null, "so the evaluator admits at 1", "ops", ["1", "2"]);
        await evaluator.ActivateAsync(Context());
        var held = (await new EfSchemaFinalizationStore(Context()).FindAsync(Family))!;
        await new EfSchemaFinalizationStore(Context()).ReleaseHoldAsync(Family, held.Revision, null, "ops");
    }

    private static async Task Run(Func<Task> actor, Action finish)
    {
        try
        {
            await actor();
        }
        finally
        {
            finish();
        }
    }

    private static IEnumerable<IReadOnlyList<string>> Merge(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0)
        {
            yield return right;
            yield break;
        }

        if (right.Count == 0)
        {
            yield return left;
            yield break;
        }

        foreach (var rest in Merge(left.Skip(1).ToArray(), right))
            yield return [left[0], .. rest];
        foreach (var rest in Merge(left, right.Skip(1).ToArray()))
            yield return [right[0], .. rest];
    }

    private GateContext Context(params IInterceptor[] interceptors)
    {
        var context = SchemaGate.Context(database.ConnectionString, null, interceptors);
        contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Releases actors one step at a time in <c>order</c>. An actor's step begins when it reaches it, and is done when the
    /// actor reaches its next step or finishes, so each step runs whole before the next one in the order begins.
    /// </summary>
    private sealed class Script(IReadOnlyList<string> order)
    {
        private readonly object _lock = new();
        private readonly HashSet<string> _done = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ReachAsync(string actor, string step)
        {
            lock (_lock)
            {
                if (_current.Remove(actor, out var previous))
                    MarkDone(previous);
            }

            while (true)
            {
                Task changed;
                lock (_lock)
                {
                    if (order.TakeWhile(candidate => candidate != step).All(_done.Contains))
                    {
                        _current[actor] = step;
                        return;
                    }

                    changed = _changed.Task;
                }

                await changed.WaitAsync(TimeSpan.FromSeconds(20));
            }
        }

        public void Finish(string actor, IEnumerable<string> steps)
        {
            lock (_lock)
            {
                _current.Remove(actor);
                foreach (var step in steps)
                    MarkDone(step);
            }
        }

        private void MarkDone(string step)
        {
            _done.Add(step);
            var changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.SetResult();
        }
    }

    /// <summary>Calls <c>before</c> with a running count before each select on the record table.</summary>
    private sealed class RecordCommands(Func<int, Task> before) : DbCommandInterceptor
    {
        private int _count;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var text = command.CommandText.TrimStart();
            if (text.Contains(RecordTable, StringComparison.Ordinal) && text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                await before(Interlocked.Increment(ref _count));
            return result;
        }
    }
}
