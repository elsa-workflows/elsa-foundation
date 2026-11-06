using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

// Actual public sender operations against retained sources. Captured documents are compared with the
// physical source; they never become independent permission or bypass current claim validation.
internal static class SupplementalSourceCommands
{
    private static PostCommitDeliveryContext Source(ModelCommand command)
    {
        var source = command.SavedSource ?? throw new InvalidDataException("Exact captured source required.");
        var admission = command.SavedAdmission ?? throw new InvalidDataException("Exact captured admission required.");
        if (source.Scope.Value != command.Scope.PersistencePartition || admission.Scope != command.Scope || admission.Origin is null)
            throw new InvalidDataException("The captured sender is outside this exact owned scope.");
        return source;
    }

    internal static async ValueTask<object> ReplayAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        var old = Source(command);
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var current = await lookup.FindDurableAsync(old.SourceOutboxItemId, ct) ?? throw new InvalidDataException("The exact original source disappeared.");
        if (!current.Intent.IsEquivalentTo(old.Intent) || current.StimulusDeliverySourceScope != old.Scope)
            throw new InvalidDataException("The physical sender intent changed.");
        var store = services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claims = await store.ClaimAsync(new("model-replay-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1), 1, old.Intent.WorkflowExecutionId, old.Intent.Kind), ct);
        if (claims.Count == 0) return new { claimed = false, current };
        var claim = claims.Single();
        if (claim.OutboxItemId != old.SourceOutboxItemId || !claim.Item.Intent.IsEquivalentTo(old.Intent) || claim.FencingToken <= old.Claim.FencingToken)
            throw new InvalidDataException("The successor did not claim the exact retained original sender.");
        var source = new PostCommitDeliveryContext(claim, old.Scope);
        await services.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(source, ct);
        await store.RecordDeliveryResultAsync(claim, new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, DateTimeOffset.UtcNow), ct);
        var retained = await lookup.FindDurableAsync(claim.OutboxItemId, ct);
        if (retained?.Status != RuntimePostCommitOutboxStatus.Delivered || !retained.Intent.IsEquivalentTo(old.Intent))
            throw new InvalidDataException("The exact replayed sender was not durably delivered.");
        return new { claimed = true, oldFencingToken = old.Claim.FencingToken, claim, retained,
            origin = await services.GetRequiredService<IJobQueries>().FindOriginAsync(command.Scope, command.SavedAdmission!.Origin!, ct) };
    }

    internal static async ValueTask<object> DuplicateAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        var source = Source(command);
        try { await services.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(source, ct); }
        catch (RuntimePostCommitOutboxStaleClaimException)
        { return new { refusedStaleSource = true, source.SourceOutboxItemId, source.IntentFingerprint, originalFencingToken = source.Claim.FencingToken }; }
        throw new InvalidDataException("The old in-flight sender was not refused at its actual stale source fence.");
    }

    internal static async ValueTask<JobAdmissionResult> ReplayOriginAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        _ = Source(command);
        // This fresh ordinary key and different proposal have no old-key fallback. Only the exact
        // protected origin may resolve the already accepted canonical JobId after result ACK/expiry.
        var request = command.SavedAdmission! with { IdempotencyKey = "fresh-origin-key-" + Guid.NewGuid().ToString("N"),
            ProposedJobId = "fresh-origin-proposal-" + Guid.NewGuid().ToString("N") };
        return await services.GetRequiredService<IJobCommands>().AdmitAsync(request, ct);
    }

    internal static async ValueTask<RuntimePostCommitOutboxItem> ReadAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        var source = Source(command);
        var retained = await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(source.SourceOutboxItemId, ct)
            ?? throw new InvalidDataException("The exact retained original sender is missing.");
        if (!retained.Intent.IsEquivalentTo(source.Intent) || retained.StimulusDeliverySourceScope != source.Scope)
            throw new InvalidDataException("The exact retained original sender changed.");
        return retained;
    }
}
