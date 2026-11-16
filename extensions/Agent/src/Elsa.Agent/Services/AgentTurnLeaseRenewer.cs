using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

// Metadata renewal belongs to the existing native leaf. Each call owns a fresh ordinary persistence
// scope; it never shares the model callback's scoped store/DbContext or schedules another inference.
internal sealed class AgentTurnLeaseRenewer(IPersistenceOperationScopeFactory scopes, TimeProvider time)
{
    internal AgentTurnLeaseLifetime Start(AgentTurnDocument document, CancellationToken cancellationToken)
    {
        if (document.AdmissionGeneration < 1 || document.Fence is null || document.LeaseExpiresAt is null || document.LeaseExpiresAt <= time.GetUtcNow())
            throw AgentModelRunner.Failure("agent.turn.lease", "The journal cannot supply a finite live claim lease.");
        return new(scopes, time, document, cancellationToken);
    }
}

internal sealed class AgentTurnLeaseLifetime : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly CancellationTokenSource lost = new();
    private readonly Task renewal;
    private int isLost;
    private Exception? failure;

    internal AgentTurnLeaseLifetime(IPersistenceOperationScopeFactory scopes, TimeProvider time, AgentTurnDocument document, CancellationToken cancellationToken)
    {
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renewal = RenewAsync();

        async Task RenewAsync()
        {
            var expiry = document.LeaseExpiresAt!.Value;
            try
            {
                while (true)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    var now = time.GetUtcNow();
                    if (expiry <= now) throw new InvalidOperationException("The agent claim lease expired before renewal.");
                    // A lease already capped by the absolute deadline needs no more renewal.
                    if (expiry >= document.Deadline)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, time, stop.Token);
                        return;
                    }
                    var delay = TimeSpan.FromTicks(Math.Clamp((expiry - now).Ticks / 3, TimeSpan.TicksPerMillisecond, TimeSpan.FromSeconds(10).Ticks));
                    await Task.Delay(delay, time, stop.Token);
                    // Confirmation must arrive before the last confirmed expiry, including scope/DB I/O.
                    var confirmationWindow = expiry - time.GetUtcNow();
                    if (confirmationWindow <= TimeSpan.Zero) throw new InvalidOperationException("The agent claim lease expired before renewal confirmation.");
                    using var expires = new CancellationTokenSource(confirmationWindow, time);
                    using var confirmation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, expires.Token);
                    await using var scope = await scopes.CreateAsync(new PersistenceScope(document.Identity.PersistencePartition), confirmation.Token);
                    var store = scope.ServiceProvider.GetRequiredService<IAgentTurnStore>();
                    var renewed = await store.RenewAsync(document.Handle, document.Identity, AgentTurnLeaseRules.Token(document), time.GetUtcNow(), confirmation.Token);
                    confirmation.Token.ThrowIfCancellationRequested();
                    if (renewed is null || renewed.Handle != document.Handle || renewed.Identity != document.Identity || renewed.Fence != document.Fence || renewed.AdmissionGeneration != document.AdmissionGeneration ||
                        renewed.Deadline != document.Deadline || renewed.LeaseExpiresAt is null || renewed.LeaseExpiresAt > document.Deadline || renewed.LeaseExpiresAt <= time.GetUtcNow())
                        throw new InvalidOperationException("The agent journal could not confirm its live renewal.");
                    expiry = renewed.LeaseExpiresAt.Value;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception exception)
            {
                failure = exception;
                Volatile.Write(ref isLost, 1);
                try { await lost.CancelAsync(); }
                catch (AggregateException) { /* A cancellation callback cannot restore lost ownership. */ }
            }
        }
    }

    internal bool IsLost => Volatile.Read(ref isLost) != 0;
    internal CancellationToken LostToken => lost.Token;
    internal AgentTurnException Failure() => AgentModelRunner.Failure("agent.turn.lease", "The agent journal lease was lost or its renewal could not be confirmed.", failure);
    internal void ThrowIfLost() { if (IsLost) throw Failure(); }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await renewal;
        stop.Dispose();
        lost.Dispose();
    }
}
