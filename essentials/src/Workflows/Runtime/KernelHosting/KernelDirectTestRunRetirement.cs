using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Retires expired roots from the shell's private Test Run tenant using selected provider commands.</summary>
internal sealed class KernelDirectTestRunRetirement(BoundScope tenant, IActorReads reads,
    Func<Func<BoundScope, ActorRef, string, bool>, CancellationToken, Task<IIngress>> openOperators,
    TimeProvider clock, ILogger<KernelDirectTestRunRetirement> logger)
{
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PageBudget = TimeSpan.FromSeconds(4);

    internal async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(Cadence, clock);
        PageToken? cursor = null;
        try
        {
            do
            {
                using var budget = new CancellationTokenSource(PageBudget, clock);
                using var page = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
                try { cursor = await SweepPageAsync(cursor, page.Token); }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning(error, "Expired Test Run retirement in actor tenant {Tenant} will retry.", tenant.Tenant);
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async ValueTask<PageToken?> SweepPageAsync(PageToken? cursor, CancellationToken token)
    {
        var page = await reads.ListAsync(tenant, new(WorkflowActor.ActorType), cursor, token);
        if (page.Next is not null && page.Next == cursor)
            throw new InvalidOperationException("The expired Test Run reader returned the same continuation.");

        var now = clock.GetUtcNow();
        foreach (var view in page.Items)
        {
            if (view.Header.Tenant != tenant.Tenant || view.Selection is not { } selection ||
                WorkflowActor.DescribeRun(view.Header)?.TestStart is not { } test || test.ExpiresAt > now)
                continue;
            var actor = view.Header.Ref;
            var kind = view.Header.Status == ActorStatus.Running || view.Park is not null ? Kinds.Terminate : Kinds.Delete;
            var ingress = await openOperators((selected, target, operation) =>
                selected.Tenant == tenant.Tenant && target == actor && operation == kind, token);
            var command = new OperatorCommand(selection, ExpectedTarget: actor);
            // A selected command that lost its race will receive a fresh selection on a later pass.
            _ = await ingress.SendAsync(tenant, new Target.Ref(actor), Guid.NewGuid().ToString("N"), kind,
                JsonSerializer.Serialize(command), cancellationToken: token);
        }
        return page.Next;
    }
}
