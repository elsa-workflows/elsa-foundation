using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal sealed class ConnectionWriter(
    IConnectionCatalogStore store,
    IConnectionWriteAuthorizer authorizer,
    IConnectionReadiness readiness,
    IConnectionRecovery recovery,
    ILocalLoginRecovery localRecovery,
    IOptions<ExternalAuthenticationOptions> options,
    TimeProvider clock) : IConnectionWriter
{
    public async ValueTask<ConnectionChangeAssessment> AssessAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, actor, cancellationToken);
        return new(prepared.Failure is null, prepared.Proposed, prepared.Failure);
    }

    public async ValueTask<ConnectionWriteResult> ApplyAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, actor, cancellationToken);
        if (prepared.Failure is not null) return new(false, Failure: prepared.Failure);
        cancellationToken.ThrowIfCancellationRequested();
        var saved = await store.SaveAsync(prepared.Catalog!, cancellationToken);
        return saved.Saved ? new(true, saved.Revision, prepared.RecordId) : new(false, Failure: "revision-conflict");
    }

    private async ValueTask<PreparedWrite> PrepareAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Record);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await authorizer.AuthorizeAsync(actor, cancellationToken)) return Reject("not-authorized");
        if (!options.Value.AllowDatabaseManagement) return Reject("database-management-disabled");
        if (!store.SupportsIdentityRevisionGuards) return Reject("identity-coordination-unavailable");
        var current = await store.ReadAsync(cancellationToken);
        if (current.Revision != request.ExpectedRevision) return Reject("revision-conflict");
        ConnectionSelection.Validate(current.Records);
        var records = current.Records.ToList();
        var existing = records.SingleOrDefault(record => record.Id == request.Record.Id);
        var create = request.Change is ConnectionChange.Create or ConnectionChange.CreateOverride;
        if (!create && existing is null) return Reject("database-record-not-found");
        if (create && records.Any(record => record.Key == request.Record.Key)) return Reject("connection-key-in-use");
        if (!create && existing!.Key != request.Record.Key) return Reject("connection-key-immutable");
        var proposed = request.Change switch
        {
            ConnectionChange.Create => request.Record with { Id = Guid.NewGuid().ToString("n"), Enabled = false, OverridesDeployment = false, ArchivedAt = null },
            ConnectionChange.CreateOverride => request.Record with { Id = Guid.NewGuid().ToString("n"), OverridesDeployment = true, ArchivedAt = null },
            ConnectionChange.Update => request.Record,
            ConnectionChange.PromoteOverride => request.Record with { OverridesDeployment = true },
            ConnectionChange.Enable => existing! with { Enabled = true },
            ConnectionChange.Disable => existing! with { Enabled = false },
            ConnectionChange.Archive => existing! with { ArchivedAt = clock.GetUtcNow() },
            ConnectionChange.Restore => existing! with { ArchivedAt = null },
            ConnectionChange.Remove => existing!,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        if (request.Change == ConnectionChange.Update && proposed.OverridesDeployment != existing!.OverridesDeployment)
            return Reject("explicit-override-required");
        if (request.Change == ConnectionChange.Create && request.Record.OverridesDeployment)
            return Reject("explicit-override-required");
        if (request.Change == ConnectionChange.Update && (proposed.Enabled != existing!.Enabled || proposed.ArchivedAt != existing.ArchivedAt))
            return Reject("explicit-lifecycle-required");
        if (request.Change == ConnectionChange.PromoteOverride && (existing!.OverridesDeployment || proposed.ArchivedAt != existing.ArchivedAt))
            return Reject("explicit-lifecycle-required");
        try { ConnectionSelection.Validate([proposed]); }
        catch (InvalidOperationException) { return Reject("invalid-configuration"); }
        if (request.Change is not (ConnectionChange.Disable or ConnectionChange.Archive or ConnectionChange.Remove) &&
            !await authorizer.AuthorizePolicyAsync(actor, proposed, cancellationToken)) return Reject("role-delegation-forbidden");
        var policy = request.Change is ConnectionChange.Disable or ConnectionChange.Archive or ConnectionChange.Remove ? null : proposed;
        var guards = await authorizer.CaptureRevisionGuardsAsync(actor, policy, cancellationToken);
        if (guards is null) return Reject("identity-authority-changed");
        var before = ConnectionSelection.Select(options.Value, current);
        if (request.Change is ConnectionChange.CreateOverride or ConnectionChange.PromoteOverride)
        {
            var baseline = before.Connections.SingleOrDefault(view => view.Source == ConnectionSource.Deployment && view.Record.Key == proposed.Key);
            if (baseline is null || !baseline.OverridePermitted) return Reject("deployment-override-forbidden");
        }
        if (proposed is { Enabled: true, ArchivedAt: null } && request.Change != ConnectionChange.Remove &&
            !await readiness.IsReadyAsync(proposed, cancellationToken)) return Reject("connection-not-ready");
        if (existing is not null) records.Remove(existing);
        if (request.Change != ConnectionChange.Remove) records.Add(ConnectionSelection.Snapshot(proposed));
        ConnectionSelection.Validate(records);
        var desired = new ConnectionCatalog(records, current.Revision);
        var after = ConnectionSelection.Select(options.Value, desired);
        if (RemovesEnabledPath(before, after))
        {
            var recoveryGuards = await localRecovery.CaptureRevisionGuardsAsync(actor, cancellationToken) ??
                await ExternalPath(after, actor, cancellationToken);
            if (recoveryGuards is null) return Reject("final-login-path");
            guards = IdentitySnapshot.Merge(guards, recoveryGuards);
            if (guards is null) return Reject("identity-authority-changed");
        }
        desired = desired with { IdentityRevisionGuards = guards };
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedWrite(desired, after, proposed.Id);
    }

    private async ValueTask<IReadOnlyList<IamRevisionGuard>?> ExternalPath(ConnectionRegistryView registry, ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            foreach (var view in registry.Connections.Where(view => view.Effective && view.Record.Enabled))
                if (await readiness.IsReadyAsync(view.Record, deadline.Token) &&
                    await recovery.CaptureRevisionGuardsAsync(view.Record, actor, deadline.Token) is { } guards) return guards;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        return null;
    }

    private static bool RemovesEnabledPath(ConnectionRegistryView before, ConnectionRegistryView after) =>
        before.Connections.Where(view => view.Effective && view.Record.Enabled).Any(previous =>
            !after.Connections.Any(view => view.Effective && view.Record.Enabled && view.Record.Key == previous.Record.Key &&
                view.MaterialRevision == previous.MaterialRevision));

    private static PreparedWrite Reject(string code) => new(null, null, null, code);
    private sealed record PreparedWrite(ConnectionCatalog? Catalog, ConnectionRegistryView? Proposed, string? RecordId, string? Failure = null);
}
