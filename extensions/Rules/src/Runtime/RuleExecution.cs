using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>One bounded durable round per acquired lease; the store remains the scheduling authority.</summary>
internal sealed class RuleExecution(IRuleRunStore store, IRuleRunReader reader,
    IRuleImplementationIdentitySource identitySource, RuleRoundEvaluator evaluator)
{
    public async Task<bool> ExecuteOneAsync(string workerId, CancellationToken cancellationToken)
    {
        var implementation = await identitySource.ReadAsync(cancellationToken);
        // An empty interpreter set still permits common accepted-receipt recovery in a capable store.
        var claim = await store.ClaimWorkAsync(new RuleClaimRequest(workerId,
            implementation is null ? [] : [implementation], [1], [1], [1]), cancellationToken);
        if (claim.Disposition != RuleWorkClaimDisposition.Claimed || claim.Token is not { } token)
            return false;

        // Release even when a response is lost: acceptance/consumption replay lives in durable state,
        // and a fresh worker can continue. A stale release cannot clear another owner's lease.
        try
        {
            var work = await reader.ReadWorkAsync(token, cancellationToken);
            if (work.Disposition == RuleWorkReadDisposition.EvaluateRound)
            {
                var input = work.RoundInput!;
                token = work.CurrentToken!;
                if (implementation is null || RuleRecordCodec.PinHash(input.Implementation) != RuleRecordCodec.PinHash(implementation))
                    return false;
                var receipt = evaluator.Evaluate(input);
                var accepted = await store.AcceptRoundAsync(token,
                    new(receipt, new(Guid.NewGuid().ToString("N"), workerId, token.Lease.Fence)), cancellationToken);
                if (accepted.Disposition is not (RuleReceiptDisposition.Accepted or RuleReceiptDisposition.Replay))
                    return false;
                // Never use acceptance as a grant of successor work. Obtain a consistent current token.
                work = await reader.ReadWorkAsync(token, cancellationToken);
            }
            if (work.Disposition != RuleWorkReadDisposition.ConsumeReceipt)
                return false;
            token = work.CurrentToken!;
            var pending = work.PendingReceipt!;
            var consumed = await store.ConsumeRoundAsync(token,
                new(pending.ReceiptId, pending.Receipt.SemanticReceiptSha256), cancellationToken);
            return consumed.Disposition is RuleTransitionDisposition.Consumed or RuleTransitionDisposition.Replay;
        }
        finally
        {
            // Shutdown leaves the bounded lease to expire instead of prolonging host shutdown.
            if (!cancellationToken.IsCancellationRequested)
                await store.ReleaseAsync(token.Lease, cancellationToken);
        }
    }
}
