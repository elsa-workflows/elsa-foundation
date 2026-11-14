using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfIngressReservationStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IRuntimeIngressReservationReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public bool SupportsReservations => context.Database.IsRelational();

    public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string stimulusType, string stimulusHash, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => FindAsyncCore(scope, stimulusType, stimulusHash, cancellationToken), scope?.Persistence.Value, stimulusHash);

    public ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ListAsyncCore(scope, query, cancellationToken), scope?.Persistence.Value, query?.StimulusHash);

    public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ListInScopeAsyncCore(scope, query, cancellationToken), scope?.Value, query?.StimulusHash);

    private async ValueTask<RuntimeIngressReservation?> FindAsyncCore(TriggerScope scope, string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        Require(scope.Persistence, cancellationToken);
        var id = RuntimeIngressReservation.CreateIdentity(scope.Persistence, stimulusType, stimulusHash);
        var row = await context.IngressReservations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var found = row is null ? null : Read(row, scope.Persistence);
        return found?.Scope == scope ? found : null;
    }

    private async ValueTask<RuntimeIngressReservationPage> ListAsyncCore(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
    {
        var page = await ListInScopeAsync(scope.Persistence, query, cancellationToken);
        return new(page.Items.Where(item => item.Scope == scope).ToArray(), page.Next);
    }

    private async ValueTask<RuntimeIngressReservationPage> ListInScopeAsyncCore(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RuntimeStorePageRequest.ValidateLimit(query.Limit, nameof(query));
        var scopeHash = Hash(scope.Value); var typeHash = Hash(query.StimulusType);
        var rows = context.IngressReservations.AsNoTracking().Where(row => row.ScopeHash == scopeHash && row.StimulusTypeHash == typeHash);
        if (query.StimulusHash is not null)
        { var hash = Hash(query.StimulusHash); rows = rows.Where(row => row.StimulusHashHash == hash); }
        if (query.After is not null) rows = rows.Where(row => string.Compare(row.Id, query.After) > 0);
        var page = await rows.OrderBy(row => row.Id).Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        var values = page.Select(row => Read(row, scope)).ToArray();
        if (values.Any(item => item.StimulusType != query.StimulusType || query.StimulusHash is not null && item.StimulusHash != query.StimulusHash))
            throw new InvalidDataException("A reservation projection hash differs from its full stimulus identity.");
        return new(values.Take(query.Limit).ToArray(), values.Length > query.Limit ? values[query.Limit - 1].ReservationId : null);
    }

    /// <summary>Stages one owner's whole exact claim set in the caller's resource/activation transaction.</summary>
    internal async ValueTask StageOwnerAsync(TriggerScope scope, string ownerKind, string ownerId,
        IReadOnlyList<RuntimeIngressReservation> claims, CancellationToken ct,
        IReadOnlyDictionary<string, IIngressReservationPolicy>? policies = null)
    {
        Require(scope.Persistence, ct);
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Reservation mutation requires its owning source transaction.");
        policies ??= RuntimeIngressReservationRules.Policies(null);
        if (claims.Any(claim => policies.ContainsKey(claim.StimulusType))) RuntimeSchemaWriteVersion.RequireReservationSemantics(context);
        RuntimeIngressReservationRules.CheckSemanticCandidates(claims, policies);
        // The owning SERIALIZABLE transaction keeps the entire same-type predicate until commit, including absence.
        foreach (var type in claims.Select(claim => claim.StimulusType).Distinct(StringComparer.Ordinal).Where(policies.ContainsKey))
        {
            var physicalScopeHash = Hash(scope.Persistence.Value); var typeHash = Hash(type);
            var existing = await context.IngressReservations.AsNoTracking().Where(row => row.ScopeHash == physicalScopeHash &&
                row.StimulusTypeHash == typeHash).ToArrayAsync(ct);
            foreach (var row in existing)
                foreach (var claim in claims.Where(claim => claim.StimulusType == type))
                    RuntimeIngressReservationRules.CheckSemanticOwner(Read(row, scope.Persistence), claim, policies);
        }
        var scopeHash = Hash(scope.Persistence.Value); var ownerHash = Hash(ownerId);
        var tenantKey = Encode(JsonSerializer.Serialize(scope.TenantId));
        // A logical publication slot is owned by its physical scope, even when a replacement captures a new tenant.
        // Standalone resource identities remain tenant-scoped and cannot reconcile another tenant's claims.
        var publicationOwner = ownerKind == RuntimeIngressReservation.PublicationSlotOwner;
        var owned = context.IngressReservations.Where(row => row.ScopeHash == scopeHash && row.OwnerKind == ownerKind && row.OwnerHash == ownerHash);
        if (!publicationOwner) owned = owned.Where(row => row.TenantKey == tenantKey);
        var ownedRows = await owned.ToArrayAsync(ct);
        foreach (var row in ownedRows)
        {
            var old = Read(row, scope.Persistence);
            if (old.OwnerKind != ownerKind || old.OwnerId != ownerId || !publicationOwner && old.Scope.TenantId != scope.TenantId)
                throw new InvalidDataException("The reservation owner projection differs from its actual source and tenant.");
        }
        var ids = claims.Select(item => item.ReservationId).ToHashSet(StringComparer.Ordinal);
        foreach (var row in ownedRows.Where(row => !ids.Contains(row.Id))) context.IngressReservations.Remove(row);
        foreach (var claim in claims)
        {
            if (claim.Scope != scope || claim.OwnerKind != ownerKind || claim.OwnerId != ownerId)
                throw new InvalidDataException("A reservation plan contains another owner.");
            var row = ownedRows.SingleOrDefault(item => item.Id == claim.ReservationId)
                ?? await context.IngressReservations.SingleOrDefaultAsync(item => item.Id == claim.ReservationId, ct);
            if (row is null)
            {
                var created = ToEntity(claim, 1); created.SchemaVersion = RuntimeSchemaWriteVersion.Standalone(context);
                context.IngressReservations.Add(created);
            }
            else
            {
                RuntimeIngressReservationRules.CheckOwner(Read(row, scope.Persistence), claim);
                Copy(row, claim, checked(row.Revision + 1));
            }
        }
    }

    internal async ValueTask FenceAsync(RuntimeIngressReservation expected, CancellationToken ct)
    {
        Require(expected.Scope.Persistence, ct);
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("A route admission fence requires the actual source transaction.");
        var row = await context.IngressReservations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == expected.ReservationId, ct);
        if (row is null || !RuntimeIngressReservationRules.Same(Read(row, expected.Scope.Persistence), expected))
            throw new TriggerOperationException("conflict", "The matched source reservation changed before admission.");
        // A real update fence holds this same reservation against route replacement/deletion through source commit.
        if (await context.IngressReservations.Where(item => item.Id == row.Id && item.Revision == row.Revision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Revision, row.Revision), ct) != 1)
            throw new TriggerOperationException("conflict", "The matched source reservation changed before admission.");
    }

    internal static RuntimeIngressReservation Read(RuntimeIngressReservationEntity row, PersistenceScope scope)
    {
        if (EfSchemaVersion.NotReadable(StandaloneTriggersEfModule.Chain, row.SchemaVersion) || row.Revision <= 0)
            throw new InvalidDataException("The reservation schema or revision is invalid.");
        RuntimeIngressReservation value;
        try { value = JsonSerializer.Deserialize<RuntimeIngressReservation>(StandaloneTriggersEfModule.Chain.Upcast<RuntimeIngressReservationEntity>(row.SchemaVersion,
            (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!, Json) ?? throw new InvalidDataException("A reservation has no content."); }
        catch (Exception e) when (e is JsonException or ArgumentException) { throw new InvalidDataException("The reservation content is invalid.", e); }
        var expected = ToEntity(value, row.Revision);
        if (value.Scope.Persistence != scope || value.ReservationId != RuntimeIngressReservation.CreateIdentity(scope, value.StimulusType, value.StimulusHash) ||
            expected.Id != row.Id || expected.ScopeKey != row.ScopeKey || expected.ScopeHash != row.ScopeHash || expected.TenantKey != row.TenantKey ||
            expected.StimulusType != row.StimulusType || expected.StimulusTypeHash != row.StimulusTypeHash || expected.StimulusHash != row.StimulusHash ||
            expected.StimulusHashHash != row.StimulusHashHash || expected.OwnerKind != row.OwnerKind || expected.OwnerId != row.OwnerId || expected.OwnerHash != row.OwnerHash)
            throw new InvalidDataException("A reservation's physical projections differ from its full authority.");
        return value;
    }

    private static RuntimeIngressReservationEntity ToEntity(RuntimeIngressReservation value, long revision) => new()
    {
        Id = value.ReservationId, ScopeKey = Encode(value.Scope.Persistence.Value), ScopeHash = Hash(value.Scope.Persistence.Value),
        TenantKey = Encode(JsonSerializer.Serialize(value.Scope.TenantId)), StimulusType = Encode(value.StimulusType), StimulusTypeHash = Hash(value.StimulusType),
        StimulusHash = Encode(value.StimulusHash), StimulusHashHash = Hash(value.StimulusHash), OwnerKind = value.OwnerKind,
        OwnerId = Encode(value.OwnerId), OwnerHash = Hash(value.OwnerId), Revision = revision, ContentJson = JsonSerializer.Serialize(value, Json)
    };

    private void Copy(RuntimeIngressReservationEntity row, RuntimeIngressReservation value, long revision)
    {
        var next = ToEntity(value, revision); next.SchemaVersion = RuntimeSchemaWriteVersion.Standalone(context, row.SchemaVersion);
        row.ScopeKey = next.ScopeKey; row.ScopeHash = next.ScopeHash; row.TenantKey = next.TenantKey;
        row.StimulusType = next.StimulusType; row.StimulusTypeHash = next.StimulusTypeHash; row.StimulusHash = next.StimulusHash;
        row.StimulusHashHash = next.StimulusHashHash; row.OwnerKind = next.OwnerKind; row.OwnerId = next.OwnerId;
        row.OwnerHash = next.OwnerHash; row.ContentJson = next.ContentJson; row.Revision = next.Revision; row.SchemaVersion = next.SchemaVersion;
    }

    private void Require(PersistenceScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!SupportsReservations || access.Current.RequireScope() != scope) throw new NotSupportedException("Reservation reads require the selected relational namespace.");
    }
}

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    partial void GetReservationAdmissionFence(RuntimeIngressAdmissionRequest request, ref Func<CancellationToken, ValueTask>? fence)
    {
        var expected = request.Reservation ?? throw new InvalidDataException("A routed source has no reservation.");
        fence = async token =>
        {
            RuntimeIngressReservationRules.CheckAdmission(request, expected);
            await new EfIngressReservationStore(context, access).FenceAsync(expected, token);
        };
    }
}
