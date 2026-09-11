using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elsa.Persistence.Groundwork.DesignAtomic;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class DesignAtomicWriteProtocolTests
{
    [Fact]
    public async Task ExecuteAsync_NullLane_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            (DesignAtomicWriteLane<ProbeScope, Marker, Stage, Result>)null!,
            static (_, _) => Task.FromResult(new Stage(true, "value")),
            beforeAttempt: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_NullStage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            new Probe().CreateLane(),
            (Func<ProbeScope, CancellationToken, Task<Stage>>)null!,
            beforeAttempt: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task ExistingMarker_ReplaysWithoutStagingOrPreflight()
    {
        var probe = new Probe { Marker = new Marker("winner") };
        var beforeAttemptCalls = 0;

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(new Result("replayed", "winner"), result);
        Assert.Equal(0, beforeAttemptCalls);
        Assert.Equal(new[] { "load" }, probe.Trace);
        Assert.Equal(0, probe.RollbackCount);
    }

    [Fact]
    public async Task AcceptedCommit_RunsPreflightOnceAndReturnsCommitted()
    {
        var probe = new Probe();
        var beforeAttemptCalls = 0;

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(new Result("committed", "value"), result);
        Assert.Equal(1, beforeAttemptCalls);
        Assert.Equal(new[] { "load", "begin", "stage", "save", "commit" }, probe.Trace);
        Assert.Equal(0, probe.RollbackCount);
    }

    [Fact]
    public async Task RejectedStage_RollsBackEvenWhenAttemptRollbackIsSuppressed()
    {
        var probe = new Probe { Accepted = false };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("rejected"), result);
        Assert.Equal(1, probe.RollbackCount);
        Assert.Equal(new[] { "load", "begin", "stage", "rollback" }, probe.Trace);
    }

    [Fact]
    public async Task CommitDispositionRejected_ReturnsRejectedWithoutRollback()
    {
        var probe = new Probe { CommitDisposition = DesignAtomicCommitDisposition.Rejected };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("rejected"), result);
        Assert.Equal(0, probe.RollbackCount);
        Assert.Contains("commit", probe.Trace);
    }

    [Fact]
    public async Task DelayBeforeMarkerReload_DelaysBeforeLoadingADurableWinner()
    {
        var probe = new Probe();
        probe.CommitFailures.Enqueue(new MarkerRaceException());
        probe.MarkerOnCommitFailure = new Marker("winner");

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(delayBeforeMarkerReload: true, rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("replayed", "winner"), result);
        Assert.Equal(new[] { "load", "begin", "stage", "save", "commit", "delay", "load" }, probe.Trace);
    }

    [Fact]
    public async Task ReloadBeforeDelay_ReplaysADurableWinnerWithoutDelaying()
    {
        var probe = new Probe();
        probe.CommitFailures.Enqueue(new MarkerRaceException());
        probe.MarkerOnCommitFailure = new Marker("winner");

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(delayBeforeMarkerReload: false, rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("replayed", "winner"), result);
        Assert.Equal(new[] { "load", "begin", "stage", "save", "commit", "load" }, probe.Trace);
    }

    [Fact]
    public async Task DelayBeforeMarkerReload_WhenWinnerIsAbsent_DelaysThenRetries()
    {
        var probe = new Probe();
        probe.CommitFailures.Enqueue(new MarkerRaceException());

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(delayBeforeMarkerReload: true, budget: 2, rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("committed", "value"), result);
        Assert.Equal(
            new[]
            {
                "load", "begin", "stage", "save", "commit", "delay", "load",
                "begin", "stage", "save", "commit"
            },
            probe.Trace);
    }

    [Fact]
    public async Task ReloadBeforeDelay_WhenWinnerIsAbsent_ReloadsThenDelaysThenRetries()
    {
        var probe = new Probe();
        probe.CommitFailures.Enqueue(new MarkerRaceException());

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(delayBeforeMarkerReload: false, budget: 2, rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("committed", "value"), result);
        Assert.Equal(
            new[]
            {
                "load", "begin", "stage", "save", "commit", "load", "delay",
                "begin", "stage", "save", "commit"
            },
            probe.Trace);
    }

    [Fact]
    public async Task ExhaustedMarkerRace_WithoutFactory_RethrowsOriginalConflict()
    {
        var probe = new Probe();
        var conflict = new MarkerRaceException();
        probe.CommitFailures.Enqueue(conflict);

        var thrown = await Assert.ThrowsAsync<MarkerRaceException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(budget: 1),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.Same(conflict, thrown);
    }

    [Fact]
    public async Task ExhaustedMarkerRace_WithFactory_ThrowsLaneException()
    {
        var probe = new Probe();
        probe.CommitFailures.Enqueue(new MarkerRaceException());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(
                budget: 1,
                exhausted: (exception, markerId) =>
                {
                    Assert.IsType<MarkerRaceException>(exception);
                    return new InvalidOperationException($"exhausted:{markerId}");
                }),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.Equal("exhausted:marker-1", thrown.Message);
    }

    [Fact]
    public async Task AttemptFailure_SuppressesRollbackWhenPolicyIsOff()
    {
        var probe = new Probe { StageFailure = new InvalidOperationException("stage failed") };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(rollbackOnAttemptFailure: false),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.Equal("stage failed", thrown.Message);
        Assert.Equal(0, probe.RollbackCount);
    }

    [Fact]
    public async Task AttemptFailure_RollsBackWhenPolicyIsOn_AndSwallowsRollbackFailure()
    {
        var probe = new Probe
        {
            StageFailure = new InvalidOperationException("stage failed"),
            RollbackFailure = new InvalidOperationException("rollback failed")
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(rollbackOnAttemptFailure: true),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.Equal("stage failed", thrown.Message);
        Assert.Equal(1, probe.RollbackCount);
        Assert.Contains("rollback", probe.Trace);
    }

    [Fact]
    public async Task OuterUncertainCommit_DoesNotRollBackAndDelegatesToLane()
    {
        var probe = new Probe { StageFailure = new UncertainCommitException() };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(classifyUncertainCommit: true),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("uncertain"), result);
        Assert.Same(probe.StageFailure, probe.UncertainException);
        Assert.Equal(0, probe.RollbackCount);
        Assert.DoesNotContain("delay", probe.Trace);
        Assert.DoesNotContain("reconcile", probe.Trace);
        Assert.DoesNotContain("commit", probe.Trace);
    }

    [Fact]
    public async Task UncertainCommitFromCommit_SkipsRollbackAfterReconcileMiss()
    {
        var probe = new Probe();
        var uncertain = new UncertainCommitException();
        probe.CommitFailures.Enqueue(uncertain);

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(classifyUncertainCommit: true),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("uncertain"), result);
        Assert.Same(uncertain, probe.UncertainException);
        Assert.Equal(0, probe.RollbackCount);
        Assert.Contains("reconcile", probe.Trace);
    }

    [Fact]
    public async Task PostCommitReconciliation_ReturnsLaneResultWhenMarkerIsVisible()
    {
        var probe = new Probe
        {
            ReconcileResult = new Result("reconciled", "winner")
        };
        probe.CommitFailures.Enqueue(new InvalidOperationException("commit uncertain"));

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None);

        Assert.Equal(new Result("reconciled", "winner"), result);
        Assert.Contains("reconcile", probe.Trace);
        Assert.Equal(0, probe.RollbackCount);
    }

    [Fact]
    public async Task PostCommitReconciliation_RethrowsWhenLaneCannotClassify()
    {
        var probe = new Probe();
        var commitFailure = new InvalidOperationException("commit uncertain");
        probe.CommitFailures.Enqueue(commitFailure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.Same(commitFailure, thrown);
        Assert.Contains("reconcile", probe.Trace);
        Assert.Equal(1, probe.RollbackCount);
    }

    [Fact]
    public async Task MarkerRaceOnCommit_DoesNotReconcile()
    {
        var probe = new Probe { ReconcileResult = new Result("reconciled") };
        probe.CommitFailures.Enqueue(new MarkerRaceException());

        await Assert.ThrowsAsync<MarkerRaceException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(budget: 1),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.DoesNotContain("reconcile", probe.Trace);
    }

    [Fact]
    public async Task CancellationAtEachAttempt_ThrowsBeforeStaging()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var probe = new Probe();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(throwIfCancellationRequestedEachAttempt: true),
            probe.StageAsync,
            beforeAttempt: null,
            cancelled.Token));

        Assert.Equal(new[] { "load" }, probe.Trace);
    }

    [Fact]
    public async Task CommitCancellation_IsNotReconciled()
    {
        var probe = new Probe { ReconcileResult = new Result("reconciled") };
        probe.CommitFailures.Enqueue(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            probe.StageAsync,
            beforeAttempt: null,
            CancellationToken.None));

        Assert.DoesNotContain("reconcile", probe.Trace);
        Assert.Equal(1, probe.RollbackCount);
    }

    [Fact]
    public async Task NullStagedResult_Throws()
    {
        var probe = new Probe();

        await Assert.ThrowsAsync<ArgumentNullException>(() => DesignAtomicWriteProtocol.ExecuteAsync(
            probe.CreateLane(),
            static (_, _) => Task.FromResult<Stage>(null!),
            beforeAttempt: null,
            CancellationToken.None));
    }

    public sealed class ProbeScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    public sealed record Marker(string Id);

    public sealed record Stage(bool Accepted, string Value);

    public sealed record Result(string Kind, string? Value = null);

    public sealed class MarkerRaceException : Exception;

    public sealed class UncertainCommitException : Exception;

    private sealed class Probe
    {
        public List<string> Trace { get; } = [];
        public Queue<Exception> CommitFailures { get; } = new();
        public Marker? Marker { get; set; }
        public Marker? MarkerOnCommitFailure { get; set; }
        public Exception? StageFailure { get; set; }
        public Exception? RollbackFailure { get; set; }
        public Exception? UncertainException { get; private set; }
        public Result? ReconcileResult { get; set; }
        public bool Accepted { get; set; } = true;
        public DesignAtomicCommitDisposition CommitDisposition { get; set; } = DesignAtomicCommitDisposition.Committed;
        public int RollbackCount { get; private set; }

        public DesignAtomicWriteLane<ProbeScope, Marker, Stage, Result> CreateLane(
            bool delayBeforeMarkerReload = false,
            bool rollbackOnAttemptFailure = true,
            bool throwIfCancellationRequestedEachAttempt = false,
            bool classifyUncertainCommit = false,
            int budget = 4,
            Func<Exception, string, Exception>? exhausted = null) =>
            new()
            {
                DocumentKind = "testOperation",
                MarkerId = "marker-1",
                LoadMarker = _ =>
                {
                    Trace.Add("load");
                    return Task.FromResult(Marker);
                },
                BeginScope = () =>
                {
                    Trace.Add("begin");
                    return new ProbeScope();
                },
                SaveMarker = (_, _, _) =>
                {
                    Trace.Add("save");
                    return Task.CompletedTask;
                },
                Commit = (_, _) =>
                {
                    Trace.Add("commit");
                    if (CommitFailures.Count > 0)
                    {
                        if (MarkerOnCommitFailure is not null)
                            Marker = MarkerOnCommitFailure;
                        throw CommitFailures.Dequeue();
                    }

                    return Task.FromResult(CommitDisposition);
                },
                Rollback = _ =>
                {
                    Trace.Add("rollback");
                    RollbackCount++;
                    if (RollbackFailure is not null)
                        throw RollbackFailure;
                },
                ClassifyMarkerRace = static exception => exception is MarkerRaceException,
                ClassifyUncertainCommit = exception => classifyUncertainCommit && exception is UncertainCommitException,
                OnUncertainCommit = (exception, _) =>
                {
                    Trace.Add("uncertain");
                    UncertainException = exception;
                    return Task.FromResult(new Result("uncertain"));
                },
                TryReconcileAfterCommit = (_, _) =>
                {
                    Trace.Add("reconcile");
                    return Task.FromResult(ReconcileResult);
                },
                Delay = (_, _) =>
                {
                    Trace.Add("delay");
                    return Task.CompletedTask;
                },
                IsAccepted = staged => staged.Accepted,
                OnCommitted = staged => new Result("committed", staged.Value),
                OnReplay = marker => new Result("replayed", marker.Id),
                OnRejected = static () => new Result("rejected"),
                MarkerRaceAttemptBudget = budget,
                DelayBeforeMarkerReload = delayBeforeMarkerReload,
                ThrowIfCancellationRequestedEachAttempt = throwIfCancellationRequestedEachAttempt,
                RollbackOnAttemptFailure = rollbackOnAttemptFailure,
                CreateExhaustedMarkerRaceException = exhausted
            };

        public Task<Stage> StageAsync(ProbeScope _, CancellationToken cancellationToken)
        {
            Trace.Add("stage");
            if (StageFailure is not null)
                throw StageFailure;
            return Task.FromResult(new Stage(Accepted, "value"));
        }
    }
}
