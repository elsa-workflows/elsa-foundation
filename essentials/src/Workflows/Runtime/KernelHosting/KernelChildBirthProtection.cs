using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.Executables;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelChildBirthProtection(IServiceScopeFactory scopes, PersistenceScope scope, BoundScope actors,
    KernelServing serving, KernelShellStorage foundation, TimeProvider clock, ILogger<KernelChildBirthProtection> logger)
{
    private const int PageSize = 32;
    private static readonly TimeSpan CleanupCadence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(5);

    internal async Task RequireAsync(CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
        _ = await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>().ReadOpenTailAsync(scope, token);
    }

    internal async ValueTask<ChildBirthProtectionOutcome> ProtectAsync(IReadOnlyList<PendingChildBirth> births, CancellationToken token)
    {
        var pins = new List<RuntimeKernelExecutablePin>();
        foreach (var birth in births)
        {
            if (birth.Tenant != actors.Tenant || scope.Value != actors.Tenant ||
                KernelExecutableSelection.Read(birth.Type, birth.TypeData) is not { } selected)
                return ChildBirthProtectionOutcome.Refused;
            if (selected.Executable is { } exact)
                pins.Add(new(scope, actors.Tenant, birth.Actor.Id, exact, birth.Issuance));
            else if (!serving.IsHostWorkflow(selected.Workflow, selected.Definition))
                return ChildBirthProtectionOutcome.Refused;
        }

        try
        {
            foreach (var batch in pins.Chunk(RuntimeStorePageRequest.MaximumLimit))
            {
                using var bounded = Limit(token);
                await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
                var store = runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                if (!await store.TryPinAsync(batch, bounded.Token)) return ChildBirthProtectionOutcome.Stale;
            }
            return ChildBirthProtectionOutcome.Protected;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return ChildBirthProtectionOutcome.Retry;
        }
        catch (Exception error) when (error is WorkflowExecutableRootWriteLeaseLostException or WorkflowExecutableRootWriteLeaseUnavailableException or
            TimeoutException or DbException { IsTransient: true })
        {
            logger.LogWarning(error, "Child executable protection will retry for tenant {Tenant}.", actors.Tenant);
            return ChildBirthProtectionOutcome.Retry;
        }
        catch (Exception error) when (error is WorkflowExecutableDependencyGraphException or InvalidDataException)
        {
            logger.LogWarning(error, "Child executable protection refused a selected identity for tenant {Tenant}.", actors.Tenant);
            return ChildBirthProtectionOutcome.Refused;
        }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        string? through = null, after = null;
        try
        {
            using var timer = new PeriodicTimer(CleanupCadence, clock);
            do
            {
                try
                {
                    using var pageBudget = Limit(token);
                    await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
                    var store = runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                    through ??= await store.ReadOpenTailAsync(scope, pageBudget.Token);
                    if (through is null) continue;
                    var page = await store.ListOpenAsync(scope, after, through, PageSize, pageBudget.Token);
                    foreach (var pin in page.Items)
                    {
                        token.ThrowIfCancellationRequested();
                        if (pin.Scope != scope || pin.Tenant != actors.Tenant) continue;
                        await ReleaseAsync(store, pin, token);
                    }
                    after = page.Next;
                    if (after is null) through = null;
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning(error, "Child executable pin cleanup will retry for tenant {Tenant}.", actors.Tenant);
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "Child executable pin cleanup stopped for tenant {Tenant}.", actors.Tenant);
        }
    }

    private async Task ReleaseAsync(IRuntimeKernelExecutablePinStore store, RuntimeKernelExecutablePin pin, CancellationToken token)
    {
        try
        {
            using var bounded = Limit(token);
            _ = await store.TryReleaseAsync(pin, ConfirmAsync, bounded.Token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Proof of child birth {Actor} is unavailable for tenant {Tenant}.", pin.ActorReference, actors.Tenant);
        }

        async ValueTask<RuntimeKernelExecutablePinProof> ConfirmAsync(RuntimeKernelExecutablePin exact, CancellationToken cancellationToken)
        {
            if (exact != pin) return RuntimeKernelExecutablePinProof.Unresolved;
            var proof = await foundation.ConfirmChildBirthAsync(actors, new(exact.ActorReference), exact.Issuance, cancellationToken);
            return proof switch
            {
                FoundationChildBirthProof.BirthRetired => RuntimeKernelExecutablePinProof.BirthRetired,
                FoundationChildBirthProof.IssuanceFencedAbsent => RuntimeKernelExecutablePinProof.IssuanceFencedAbsent,
                _ => RuntimeKernelExecutablePinProof.Unresolved
            };
        }
    }

    private static CancellationTokenSource Limit(CancellationToken token)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(OperationBudget);
        return bounded;
    }
}
