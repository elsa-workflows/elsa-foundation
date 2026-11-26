using Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// The workflow runs a kernel host's foundation holds, for a kernel shell's run reads (Studio's Runs page): the
/// foundation's reads and the one tenant the host binds them to. A host composes it and registers it by instance, as it
/// does <see cref="KernelServing"/>, so each shell resolves the host's own. A caller sees these runs only in that tenant.
/// </summary>
public sealed class KernelRuns
{
    private readonly Func<CancellationToken, ValueTask<IActorReads>> _reads;

    /// <param name="tenant">The tenant the host's foundation binds: its runs are this tenant's.</param>
    /// <param name="reads">The foundation's reads, opened when first needed.</param>
    public KernelRuns(BoundScope tenant, Func<CancellationToken, ValueTask<IActorReads>> reads)
    {
        Tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
    }

    /// <summary>The tenant the host's foundation binds.</summary>
    public BoundScope Tenant { get; }

    /// <summary>
    /// The workflow runs of the host's tenant, one page after another until the last, filtered by definition when one is
    /// given. Each is the actor's view and what its header says about the run (<see cref="Kernel.WorkflowActor.WorkflowActor.DescribeRun"/>).
    /// </summary>
    public async IAsyncEnumerable<(ActorView View, Kernel.WorkflowActor.WorkflowRunHeader Run)> ListAsync(string? definition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reads = await _reads(cancellationToken);
        var query = new ActorQuery(Kernel.WorkflowActor.WorkflowActor.ActorType,
            Labels: definition is null ? null : new Dictionary<string, string> { ["definition"] = definition });
        PageToken? from = null;
        do
        {
            var page = await reads.ListAsync(Tenant, query, from, cancellationToken);
            foreach (var view in page.Items)
                if (Kernel.WorkflowActor.WorkflowActor.DescribeRun(view.Header) is { } run)
                    yield return (view, run);
            from = page.Next;
        } while (from is not null);
    }
}
