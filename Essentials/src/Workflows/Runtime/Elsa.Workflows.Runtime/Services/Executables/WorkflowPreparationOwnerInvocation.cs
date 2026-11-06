using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

// Same-domain Runtime EF friend uses this for the separate native settlement outcome. Core's
// opaque constructor remains inaccessible to Publishing; no Core retention result is fabricated.
internal static class WorkflowPreparationOwnerInvocation
{
    internal static async ValueTask ExecuteAsync(IWorkflowPreparationOwnerIssuer issuer,
        IWorkflowPreparationCommitParticipant participant, WorkflowPreparationOwnerStamp expected,
        Func<WorkflowPreparationOwnerSession, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        var entered = 0;
        await issuer.ExecuteFencedAsync(expected, participant, async (lease, token) =>
        {
            if (Interlocked.Exchange(ref entered, 1) != 0)
                throw new InvalidOperationException("An enrollment invocation enters one canonical owner callback.");
            var active = 1;
            try
            {
                if (lease.Owner != expected)
                    throw new WorkflowPreparationException("enrollment-owner-mismatch", expected.Id);
                var owner = new WorkflowPreparationOwnerSession(lease, () =>
                {
                    if (Volatile.Read(ref active) == 0) throw new InvalidOperationException("The enrollment owner callback ended.");
                });
                await operation(owner, token);
                owner.EnsureActive();
            }
            finally { Interlocked.Exchange(ref active, 0); }
        }, cancellationToken);
    }
}
