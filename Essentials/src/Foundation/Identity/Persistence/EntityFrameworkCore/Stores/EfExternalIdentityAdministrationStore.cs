using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Owns link, IAM snapshot and optional global provider CAS changes in one physical transaction.</summary>
internal sealed class EfExternalIdentityAdministrationStore(IdentityIamDbContext context, IPersistenceAccessContextAccessor access,
    IdentityProviderConfigurationDbContext? providerContext = null, IIdentityEmailUniquenessPolicy? emailPolicy = null)
    : IExternalIdentityAdministrationStore
{
    public bool SupportsIdentityRevisionGuards => EfIamRevisionGuards.OwnsConfiguredTarget(context);
    public bool SupportsProviderRevisionGuards => SupportsIdentityRevisionGuards && providerContext is not null &&
        EfIamRevisionGuards.OwnsConfiguredTarget(providerContext) && context.Database.ProviderName == providerContext.Database.ProviderName &&
        string.Equals(context.Database.GetConnectionString(), providerContext.Database.GetConnectionString(), StringComparison.Ordinal);

    public ValueTask<ExternalUserAdmissionResult> AttachAsync(ExternalUserAdmission admission, IamRevisionGuard owner,
        IReadOnlyList<IamRevisionGuard> guards, ProviderConfigurationWrite? providerWrite = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(admission.User);
        ArgumentNullException.ThrowIfNull(owner);
        if (admission.CreateUser || admission.LinkPolicy != ExternalIdentityLinkPolicy.Admin ||
            EfIdentityStoreSupport.RecordId(admission.User.TenantId, admission.User.Id) != EfIdentityStoreSupport.RecordId(owner.TenantId, owner.Id))
            throw new ArgumentException("Administrative attachment requires the guarded existing owner and an explicit admin link policy.", nameof(admission));
        return ExecuteAsync(admission.Key, owner, guards, providerWrite, false, async (owned, tenant, token) =>
        {
            var writer = new EfIdentityAtomicWrite(owned, accessContextAccessor: tenant);
            var aggregates = new EfIdentityAuthorityAggregateCoordinator(owned, tenant, writer);
            var relationships = new EfIdentityAuthorityRelationshipCoordinator(owned, writer, tenant);
            var result = await new EfExternalUserAdmission(owned, tenant, aggregates, relationships,
                emailPolicy ?? IdentityEmailUniquenessPolicy.NonUnique).ApplyAsync(admission, token);
            var saved = result.Admitted && result.UserId == admission.User.Id;
            return (saved ? result : new ExternalUserAdmissionResult(false, Failure: "link-conflict"), saved);
        }, new ExternalUserAdmissionResult(false, Failure: "revision-conflict"), cancellationToken);
    }

    public ValueTask<ExternalIdentityRemovalResult> RemoveAsync(ExternalIdentityKey key, IamRevisionGuard owner, string expectedLinkRevision,
        IReadOnlyList<IamRevisionGuard> guards, ProviderConfigurationWrite? providerWrite = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(key, owner, guards, providerWrite, true, async (owned, tenant, token) =>
        {
            if (!IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedLinkRevision, out var version))
                return (new ExternalIdentityRemovalResult(ExternalIdentityRemovalStatus.Conflict), false);
            var writer = new EfIdentityAtomicWrite(owned, accessContextAccessor: tenant);
            var relationship = new EfIdentityAuthorityRelationshipCoordinator(owned, writer, tenant);
            var removed = await relationship.DeleteQualifiedExternalIdentityAsync(key, owner, version, token);
            var status = removed.Status == EfIdentityWriteStatus.Deleted ? ExternalIdentityRemovalStatus.Removed :
                removed.Status == EfIdentityWriteStatus.NotFound ? ExternalIdentityRemovalStatus.Missing : ExternalIdentityRemovalStatus.Conflict;
            return (new ExternalIdentityRemovalResult(status, removed.Version is { } revision ? IdentityEntityFrameworkRevisionCodec.FromVersion(revision) : null),
                status == ExternalIdentityRemovalStatus.Removed);
        }, new ExternalIdentityRemovalResult(ExternalIdentityRemovalStatus.Conflict), cancellationToken);

    private async ValueTask<TResult> ExecuteAsync<TResult>(ExternalIdentityKey key, IamRevisionGuard owner, IReadOnlyList<IamRevisionGuard> guards,
        ProviderConfigurationWrite? providerWrite, bool preserveOwner,
        Func<IdentityIamDbContext, IPersistenceAccessContextAccessor, CancellationToken, ValueTask<(TResult Result, bool Saved)>> mutate,
        TResult conflict, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(owner);
        EfIdentityStoreSupport.ValidateIssuer(key.Issuer);
        _ = EfIdentityStoreSupport.ExternalIdentityId(key.TenantId, key.Provider, key.Subject, key.Issuer);
        _ = EfIdentityStoreSupport.ExternalOrderKey(key.Provider, key.Subject);
        EfIamRevisionGuards.Validate(guards);
        if (owner.Kind != IamRevisionGuardKind.User || EfIdentityStoreSupport.Normalize(owner.TenantId) != EfIdentityStoreSupport.Normalize(key.TenantId) ||
            guards.Any(guard => EfIdentityStoreSupport.Normalize(guard.TenantId) != EfIdentityStoreSupport.Normalize(key.TenantId)) ||
            !guards.Any(guard => guard.Kind == owner.Kind && guard.Revision == owner.Revision &&
                EfIdentityStoreSupport.RecordId(guard.TenantId, guard.Id) == EfIdentityStoreSupport.RecordId(owner.TenantId, owner.Id)))
            throw new ArgumentException("A link mutation must guard its owner and tenant-local authority roots.", nameof(guards));
        var snapshot = guards.ToArray();
        if (providerWrite is null) IdentityEntityFrameworkAccessGuard.EnsureTenant(access, key.TenantId);
        else
        {
            IdentityEntityFrameworkAccessGuard.EnsurePrivilegedGlobal(access);
            if (providerWrite.Configuration is null || providerWrite.Configuration.TenantId is not null)
                throw new ArgumentException("Coordinated provider configuration must be global.", nameof(providerWrite));
        }
        if (!SupportsIdentityRevisionGuards || providerWrite is not null && !SupportsProviderRevisionGuards)
            throw new NotSupportedException("Link administration requires owned relational IAM and, when selected, the same provider database target.");
        DbContext[] contexts = providerWrite is null ? [context] : [context, providerContext!];
        if (System.Transactions.Transaction.Current is not null || contexts.Any(item => item.Database.CurrentTransaction is not null || item.ChangeTracker.HasChanges()))
            throw new InvalidOperationException("Coordinated link administration owns its transaction and cannot borrow caller changes.");
        context.EnsureProviderBinding();
        if (providerWrite is not null) providerContext!.EnsureProviderBinding();
        try
        {
            await using var transaction = await EfSharedTransaction.BeginSerializableAsync(contexts, token);
            var owned = transaction.Context<IdentityIamDbContext>();
            if (!await EfIamRevisionGuards.AdvanceAsync(owned, snapshot, token, preserveOwner ? owner : null)) return conflict;
            var tenant = new TenantAccess(key.TenantId);
            var result = await mutate(owned, tenant, token);
            if (!result.Saved) return result.Result;
            if (providerWrite is not null)
            {
                var provider = new EfProviderConfigurationStore(transaction.Context<IdentityProviderConfigurationDbContext>(), access);
                if ((await provider.SaveWithRevisionAsync(providerWrite.Configuration, providerWrite.ExpectedRevision, token)).Status != IamRevisionSaveStatus.Saved)
                    return conflict;
            }
            await transaction.CommitAsync(token);
            return result.Result;
        }
        catch (OperationCanceledException) { throw; }
        catch (EfCommitOutcomeUnknownException) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey))
        { return conflict; }
    }

    private sealed class TenantAccess(string tenantId) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(tenantId));
    }
}
