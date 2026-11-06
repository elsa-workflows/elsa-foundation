using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Observes actual final logical envelopes before native coalescing, without changing them.</summary>
internal sealed class HistoryCheckpointRecorder : IRuntimeCheckpointCommitEnricher
{
    public int Order => 3000;
    public ConcurrentQueue<RuntimeCheckpointCommit> Commits { get; } = new();

    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(commit);

    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        Commits.Enqueue(commit);
        return ValueTask.CompletedTask;
    }
}
