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

internal sealed class KernelExecutableProtection(IServiceScopeFactory scopes, PersistenceScope scope, BoundScope actors,
    KernelServing serving, KernelShellStorage foundation, TimeProvider clock, ILogger<KernelExecutableProtection> logger,
    BoundScope? testActors = null)
{
    private const int PageSize = 32;
    private static readonly TimeSpan CleanupCadence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeldRetryDelay = TimeSpan.FromMilliseconds(200);

    internal async Task RequireAsync(CancellationToken token)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
        _ = runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinScanStore>();
        _ = await runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>().ReadOpenTailAsync(scope, token);
    }

    internal async ValueTask<ChildBirthProtectionOutcome> ProtectAsync(IReadOnlyList<PendingChildBirth> births, CancellationToken token)
    {
        var pins = new List<RuntimeKernelExecutablePin>();
        foreach (var birth in births)
        {
            if (birth.Tenant != actors.Tenant || scope.Value != actors.Tenant ||
                KernelExecutableSelection.Read(birth.Type, birth.TypeData) is not { TestStart: null } selected)
                return ChildBirthProtectionOutcome.Refused;
            if (selected.Executable is { } exact)
                pins.Add(new(scope, actors.Tenant, birth.Actor.Id, exact, birth.Issuance));
            else if (!serving.IsHostWorkflow(selected.Workflow, selected.Definition))
                return ChildBirthProtectionOutcome.Refused;
        }

        try
        {
            foreach (var batch in pins.Chunk(RuntimeStorePageRequest.MaximumLimit))
                if (!await TryPinAsync(batch, token)) return ChildBirthProtectionOutcome.Stale;
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

    internal async ValueTask<bool> ProtectHeldStartAsync(PendingRootStart start, CancellationToken token)
    {
        if (scope.Value != actors.Tenant ||
            KernelExecutableSelection.Read(start.Type, start.TypeData) is not { } selected)
            return false;
        var selectedTenant = selected.TestStart is null ? actors : testActors;
        if (selectedTenant is null || selectedTenant.Tenant != start.Tenant) return false;

        if (selected.Executable is not { } expected) return serving.IsHostWorkflow(selected.Workflow, selected.Definition);
        RuntimeKernelExecutablePin[] pins = [new(scope, actors.Tenant, start.Actor.Id, expected, start.Issuance)
        {
            IsolatedTenant = selected.TestStart is null ? null : selectedTenant.Tenant
        }];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return await TryPinAsync(pins, token, selected); }
            catch (Exception error) when (error is WorkflowExecutableDependencyGraphException or InvalidDataException)
            {
                logger.LogWarning(error, "Held executable protection refused the selected identity for tenant {Tenant}.", actors.Tenant);
                return false;
            }
            catch (Exception error) when (!token.IsCancellationRequested &&
                error is OperationCanceledException or WorkflowExecutableRootWriteLeaseLostException or
                    WorkflowExecutableRootWriteLeaseUnavailableException or TimeoutException or DbException { IsTransient: true })
            {
                logger.LogDebug(error, "Held executable protection will retry for tenant {Tenant}.", actors.Tenant);
                await Task.Delay(HeldRetryDelay, clock, token);
            }
        }
    }

    private async ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken token,
        KernelExecutableSelection? selected = null)
    {
        using var bounded = Limit(token);
        await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
        var services = runtime.ServiceProvider;
        var store = services.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        if (selected?.TestStart is { } test)
        {
            if (test is not { ScopeId.Length: > 0, Partition.Length: > 0, SourceDigest.Length: > 0 } ||
                (test.Tenant ?? PersistenceScope.DefaultValue) != scope.Value || test.ExpiresAt <= clock.GetUtcNow() ||
                test.ScopeId != test.SourceReferenceId || test.SubjectId is not null && string.IsNullOrWhiteSpace(test.SubjectId))
                return false;
            var reference = await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                .FindAsync(test.SourceReferenceId, bounded.Token);
            if (reference is null || reference.Scope != WorkflowExecutableReferenceScope.TestRun ||
                reference.ArtifactId != selected.Workflow || reference.DefinitionId != selected.Definition ||
                reference.TenantId != test.Tenant || reference.ExpiresAt != test.ExpiresAt ||
                reference.SourceKind != test.SourceKind || reference.SourceId != test.SourceId || reference.SourceVersion != test.SourceVersion ||
                reference.DefinitionVersionId != test.DefinitionVersionId || reference.ArtifactVersion != test.ArtifactVersion ||
                !reference.IsLive(clock.GetUtcNow()) || KernelPublishedRunDispatcher.SourceDigest(reference) != test.SourceDigest)
                return false;
            var expected = new WorkflowTestScope(test.ScopeId, test.ExpiresAt, test.Tenant, new(test.Partition));
            var current = await services.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, bounded.Token);
            if (current is not { State: WorkflowTestScopeState.Open } || !WorkflowTestScope.ContextEquals(current.Scope, expected))
                return false;

            var pin = pins.Single();
            var selectedBinding = new WorkflowTestRunActorBinding(pin.ActorReference, pin.Issuance);
            if (current.IsolatedActor is { } previous && previous != selectedBinding)
            {
                // Query the issuing store before beginning the Runtime transaction. Disarm is permanent.
                if (testActors is null || await foundation.ConfirmPreparedStartAsync(testActors,
                    new(previous.ActorReference), previous.Issuance, bounded.Token) != RootStartProof.PreparationDisarmed)
                    return false;
            }

            return await store.TryPinTestRunAsync(pin, expected, reference, current.IsolatedActor, bounded.Token);
        }
        if (selected?.PublishedStart is not { } publication) return await store.TryPinAsync(pins, bounded.Token);

        // The preparation fingerprints the complete start header. The Runtime transaction fences the
        // exact current rows before it makes the pin and its GC root visible.
        if (publication is not { SourceReferenceId.Length: > 0, SlotName.Length: > 0, ActivationId.Length: > 0, SlotRevision: > 0 } ||
            publication.SubjectId is not null && string.IsNullOrWhiteSpace(publication.SubjectId)) return false;
        var source = await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(publication.SourceReferenceId, bounded.Token);
        if (source is null || source.ArtifactId != selected.Workflow || source.DefinitionId != selected.Definition ||
            source.Scope != WorkflowExecutableReferenceScope.Published || source.ActivationId != publication.ActivationId ||
            source.SourceKind != publication.SourceKind || source.SourceId != publication.SourceId || source.SourceVersion != publication.SourceVersion ||
            source.DefinitionVersionId != publication.DefinitionVersionId || source.ArtifactVersion != publication.ArtifactVersion ||
            source.SlotId != publication.SlotId || (source.TenantId ?? PersistenceScope.DefaultValue) != actors.Tenant ||
            source.PublicationScope is { } protectedScope && protectedScope != scope || !source.IsLive(clock.GetUtcNow()) ||
            KernelPublishedRunDispatcher.SourceDigest(source) != publication.SourceDigest) return false;
        var slot = await services.GetRequiredService<IWorkflowActivationAuthority>()
            .FindAsync(selected.Definition, publication.SlotName, bounded.Token);
        if (slot is null || slot.SlotId != publication.SlotId || slot.ActiveActivationId != publication.ActivationId ||
            slot.Revision != publication.SlotRevision) return false;
        return await store.TryPinPublishedAsync(pins.Single(), slot, source, bounded.Token);
    }

    internal async Task RunAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(CleanupCadence, clock);
            do
            {
                if (foundation.TakesPreparedStarts) await PrunePreparedStartsAsync(token);
                try
                {
                    using var pageBudget = Limit(token);
                    await using var runtime = await scopes.CreateAsyncScopeAsync(scope);
                    var store = runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                    var scan = runtime.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinScanStore>();
                    var page = await scan.ReadScanPageAsync(scope, PageSize, pageBudget.Token);
                    if (page is null) continue;
                    foreach (var pin in page.Items)
                    {
                        token.ThrowIfCancellationRequested();
                        if (pin.Scope != scope || pin.Tenant != actors.Tenant) continue;
                        await ReleaseAsync(store, pin, token);
                    }
                    using var checkpointBudget = Limit(token);
                    await scan.TryAdvanceScanAsync(scope, page, checkpointBudget.Token);
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    logger.LogWarning(error, "Executable pin cleanup will retry for tenant {Tenant}.", actors.Tenant);
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "Executable pin cleanup stopped for tenant {Tenant}.", actors.Tenant);
        }
    }

    private async Task PrunePreparedStartsAsync(CancellationToken token)
    {
        try
        {
            using var bounded = Limit(token);
            await foundation.PrunePreparedStartsAsync(actors, bounded.Token);
            if (testActors is not null)
                await foundation.PrunePreparedStartsAsync(testActors, bounded.Token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(error, "Prepared root cleanup will retry for tenant {Tenant}.", actors.Tenant);
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
            logger.LogWarning(error, "Proof of actor birth {Actor} is unavailable for tenant {Tenant}.", pin.ActorReference, actors.Tenant);
        }

        async ValueTask<RuntimeKernelExecutablePinProof> ConfirmAsync(RuntimeKernelExecutablePin exact, CancellationToken cancellationToken)
        {
            if (exact != pin) return RuntimeKernelExecutablePinProof.Unresolved;
            if (PendingRootStart.OwnsIssuance(exact.Issuance))
            {
                var selectedTenant = exact.IsolatedTenant is null ? actors : testActors;
                if (!foundation.TakesPreparedStarts || selectedTenant is null ||
                    exact.IsolatedTenant is { } isolated && isolated != selectedTenant.Tenant)
                    return RuntimeKernelExecutablePinProof.Unresolved;
                var root = await foundation.ConfirmPreparedStartAsync(selectedTenant, new(exact.ActorReference), exact.Issuance, cancellationToken);
                return root switch
                {
                    RootStartProof.BirthRetired => RuntimeKernelExecutablePinProof.BirthRetired,
                    RootStartProof.PreparationDisarmed => RuntimeKernelExecutablePinProof.IssuanceFencedAbsent,
                    _ => RuntimeKernelExecutablePinProof.Unresolved
                };
            }

            if (exact.IsolatedTenant is not null) return RuntimeKernelExecutablePinProof.Unresolved;
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
