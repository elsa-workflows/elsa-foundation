using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelStandaloneEventAvailability(KernelShellRuntime runtime) : IRuntimeAuthoredStandaloneEventAvailability
{
    public bool Supports(PersistenceScope scope) => runtime.SupportsAuthoredStandaloneEvents(scope);
}

/// <summary>Transfers committed workflow Events to the prepared standalone source owner.</summary>
internal sealed class KernelStandaloneEventRelay(IAuthoredStandaloneEventOutbox outbox, IServiceScopeFactory scopes,
    TriggerScope scope, TimeProvider clock, ILogger<KernelStandaloneEventRelay> logger)
{
    private const string WorkflowEventPrefix = "event:";
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AttemptBudget = TimeSpan.FromSeconds(5);

    internal async Task RunAsync(CancellationToken stopping)
    {
        AuthoredStandaloneEventCursor? after = null;
        using var timer = new PeriodicTimer(Cadence, clock);
        try
        {
            do
            {
                IReadOnlyList<AuthoredStandaloneEvent> page;
                using var read = Limit(stopping);
                try
                {
                    page = await outbox.ReadAsync(after, read.Token);
                }
                catch (Exception error) when (!stopping.IsCancellationRequested)
                {
                    logger.LogWarning(error, "Could not read committed standalone Event transfers.");
                    continue;
                }

                if (page.Count == 0)
                {
                    after = null;
                    continue;
                }

                foreach (var item in page)
                {
                    stopping.ThrowIfCancellationRequested();
                    using var attempt = Limit(stopping);
                    try
                    {
                        await RelayAsync(item, attempt.Token);
                    }
                    catch (Exception error) when (!stopping.IsCancellationRequested)
                    {
                        logger.LogWarning(error, "Standalone Event transfer {Occurrence} remains pending.", item.Occurrence);
                    }

                    after = item.Cursor;
                }
            } while (await timer.WaitForNextTickAsync(stopping));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private async Task RelayAsync(AuthoredStandaloneEvent item, CancellationToken token)
    {
        var eventName = item.EventName.StartsWith(WorkflowEventPrefix, StringComparison.Ordinal)
            ? item.EventName[WorkflowEventPrefix.Length..]
            : null;
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            var publisher = item.Publisher.Id;
            var authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("workflow-actor:" + publisher);
            var source = new RuntimeAuthoredStandaloneEvent(scope, new(scope.Persistence.Value), item.Occurrence,
                publisher, eventName, JsonSerializer.Deserialize<JsonElement>(item.Payload ?? "null"),
                DateTimeOffset.FromUnixTimeMilliseconds(item.CommittedAt), authority);
            await using var runtime = await scopes.CreateAsyncScopeAsync(scope.Persistence);
            await runtime.ServiceProvider.GetRequiredService<IRuntimeAuthoredStandaloneEventIngress>().AdmitAsync(source, token);
        }

        // A second host may already have acknowledged the same committed transfer.
        _ = await outbox.AcknowledgeAsync(item.Occurrence, item.TransferFingerprint, token);
    }

    private static CancellationTokenSource Limit(CancellationToken stopping)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        budget.CancelAfter(AttemptBudget);
        return budget;
    }
}
