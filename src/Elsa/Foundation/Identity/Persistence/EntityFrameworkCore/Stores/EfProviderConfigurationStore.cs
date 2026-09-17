using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF-backed tenant/global provider configuration store. It owns only I06/I07 and never delegates
/// to another store or requires a provider engine in this assembly.
/// </summary>
public sealed class EfProviderConfigurationStore(
    IdentityProviderConfigurationDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IProviderConfigurationStore, IRevisionAwareProviderConfigurationStore
{
    public async ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureGlobal(accessContextAccessor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.GlobalProviderId(provider);
            var row = await context.GlobalProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);
            return row is null || !Matches(row, null, provider)
                ? null
                : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to read the global provider configuration.", exception);
        }
    }

    public async ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(tenantId, nameof(tenantId));
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.TenantProviderId(tenantId, provider);
            var row = await context.TenantProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);
            return row is null || !Matches(row, tenantId, provider)
                ? null
                : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to read the tenant provider configuration.", exception);
        }
    }

    public ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default) =>
        new(SaveUnconditionallyAsync(configuration, cancellationToken));

    /// <summary>
    /// Resolves the tenant row first. The optional fallback is an explicit operation authorized by
    /// this contract; it does not make direct global reads legal from a tenant scope.
    /// </summary>
    public async ValueTask<ProviderConfigurationRecord?> FindEffectiveAsync(
        string tenantId,
        string provider,
        bool allowGlobalFallback = false,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(tenantId, nameof(tenantId));
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        context.EnsureProviderBinding();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var tenant = await FindTenantEntityAsync(tenantId, provider, cancellationToken);
            if (tenant is not null)
                return Map(tenant);
            if (!allowGlobalFallback)
                return null;
            var global = await FindGlobalEntityAsync(provider, cancellationToken, requireGlobalAccess: false);
            return global is null ? null : Map(global);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to resolve the effective provider configuration.", exception);
        }
    }

    public async ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureGlobal(accessContextAccessor);
        try
        {
            var row = await FindGlobalEntityAsync(provider, cancellationToken, requireGlobalAccess: false);
            return row is null ? null : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the global provider configuration revision.", exception);
        }
    }

    public async ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindForTenantWithRevisionAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(tenantId, nameof(tenantId));
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        try
        {
            var row = await FindTenantEntityAsync(tenantId, provider, cancellationToken);
            return row is null ? null : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the tenant provider configuration revision.", exception);
        }
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        ProviderConfigurationRecord configuration,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateConfiguration(configuration);
        var expectedVersion = 0L;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion))
            return EfIdentityStoreSupport.InvalidRevision();

        EnsureWriteAccess(configuration);
        context.EnsureProviderBinding();
        if (expectedRevision is null)
            return await EfIdentityRevisionedRowWrite.SaveCreateOnlyAsync(context, Row(configuration), cancellationToken);

        return await EfIdentityRevisionedRowWrite.SaveCompareAndSwapAsync(context, Row(configuration), expectedVersion, cancellationToken);
    }

    private async Task SaveUnconditionallyAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateConfiguration(configuration);
        EnsureWriteAccess(configuration);
        context.EnsureProviderBinding();

        await EfIdentityRevisionedRowWrite.SaveUnconditionallyAsync(context, Row(configuration), cancellationToken);
    }

    private EfIdentityRevisionedRow<ProviderConfigurationEntity> Row(ProviderConfigurationRecord configuration) => new(
        "provider configuration",
        cancellationToken => FindEntityForWriteAsync(configuration, cancellationToken),
        cancellationToken => ExistsAsync(configuration, cancellationToken),
        () => configuration.TenantId is null
            ? new GlobalProviderConfigurationEntity()
            : new TenantProviderConfigurationEntity(),
        entity => Apply(entity, configuration));

    private async Task<ProviderConfigurationEntity?> FindEntityForWriteAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken)
    {
        if (configuration.TenantId is null)
            return await FindGlobalEntityAsync(configuration.Provider, cancellationToken, track: true);
        return await FindTenantEntityAsync(configuration.TenantId, configuration.Provider, cancellationToken, track: true);
    }

    private async Task<GlobalProviderConfigurationEntity?> FindGlobalEntityAsync(
        string provider,
        CancellationToken cancellationToken,
        bool requireGlobalAccess = true,
        bool track = false)
    {
        context.EnsureProviderBinding();
        if (requireGlobalAccess)
            IdentityEntityFrameworkAccessGuard.EnsureGlobal(accessContextAccessor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.GlobalProviderId(provider);
            var rows = track
                ? context.GlobalProviderConfigurations
                : context.GlobalProviderConfigurations.AsNoTracking();
            var row = await rows.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null || !Matches(row, null, provider) ? null : row;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to query the global provider configuration.", exception);
        }
    }

    private async Task<TenantProviderConfigurationEntity?> FindTenantEntityAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken,
        bool track = false)
    {
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.TenantProviderId(tenantId, provider);
            var rows = track
                ? context.TenantProviderConfigurations
                : context.TenantProviderConfigurations.AsNoTracking();
            var row = await rows.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null || !Matches(row, tenantId, provider) ? null : row;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to query the tenant provider configuration.", exception);
        }
    }

    private async Task<bool> ExistsAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken) =>
        configuration.TenantId is null
            ? await FindGlobalEntityAsync(configuration.Provider, cancellationToken) is not null
            : await FindTenantEntityAsync(configuration.TenantId, configuration.Provider, cancellationToken) is not null;

    private void EnsureWriteAccess(ProviderConfigurationRecord configuration)
    {
        if (configuration.TenantId is null)
            IdentityEntityFrameworkAccessGuard.EnsurePrivilegedGlobal(accessContextAccessor);
        else
            IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, configuration.TenantId);
    }

    private static void Apply(ProviderConfigurationEntity entity, ProviderConfigurationRecord configuration)
    {
        entity.Id = configuration.TenantId is null
            ? IdentityProviderConfigurationCanonicalizer.GlobalProviderId(configuration.Provider)
            : IdentityProviderConfigurationCanonicalizer.TenantProviderId(configuration.TenantId, configuration.Provider);
        entity.TenantId = configuration.TenantId;
        entity.TenantLookupKey = configuration.TenantId is null ? null : IdentityProviderConfigurationCanonicalizer.Normalize(configuration.TenantId);
        entity.Provider = configuration.Provider;
        entity.ProviderLookupKey = IdentityProviderConfigurationCanonicalizer.Normalize(configuration.Provider);
        entity.Kind = configuration.Kind;
        entity.Enabled = configuration.Enabled;
        entity.IsDefault = configuration.IsDefault;
        entity.SupportsLocalUserManagement = configuration.Capabilities.SupportsLocalUserManagement;
        entity.SupportsLocalRoleManagement = configuration.Capabilities.SupportsLocalRoleManagement;
        entity.SupportsApplicationManagement = configuration.Capabilities.SupportsApplicationManagement;
        entity.SupportsGroupSync = configuration.Capabilities.SupportsGroupSync;
        entity.SupportsTokenIssuance = configuration.Capabilities.SupportsTokenIssuance;
        entity.SupportsRefresh = configuration.Capabilities.SupportsRefresh;
        entity.SupportsRevocation = configuration.Capabilities.SupportsRevocation;
        entity.PermissionPropagation = (int)configuration.Capabilities.PermissionPropagation;
        entity.SettingsJson = IdentityProviderConfigurationSettingsCodec.Serialize(configuration.Settings);
    }

    private static ProviderConfigurationRecord Map(ProviderConfigurationEntity entity)
    {
        return new ProviderConfigurationRecord(
            entity.Provider,
            entity.TenantId,
            entity.Kind,
            entity.Enabled,
            entity.IsDefault,
            new ProviderCapabilities(
                entity.SupportsLocalUserManagement,
                entity.SupportsLocalRoleManagement,
                entity.SupportsApplicationManagement,
                entity.SupportsGroupSync,
                entity.SupportsTokenIssuance,
                entity.SupportsRefresh,
                entity.SupportsRevocation,
                (PermissionPropagationMode)entity.PermissionPropagation),
            IdentityProviderConfigurationSettingsCodec.Deserialize(entity.SettingsJson));
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => new(message, exception);

    private static void ValidateConfiguration(ProviderConfigurationRecord configuration)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(configuration.TenantId, nameof(configuration.TenantId));
        IdentityProviderConfigurationCanonicalizer.Validate(configuration.Provider, nameof(configuration.Provider));
        ArgumentNullException.ThrowIfNull(configuration.Kind);
        ArgumentNullException.ThrowIfNull(configuration.Capabilities);
        ArgumentNullException.ThrowIfNull(configuration.Settings);
    }

    private static bool Matches(ProviderConfigurationEntity entity, string? tenantId, string provider) =>
        string.Equals(
            entity.Id,
            tenantId is null
                ? IdentityProviderConfigurationCanonicalizer.GlobalProviderId(provider)
                : IdentityProviderConfigurationCanonicalizer.TenantProviderId(tenantId, provider),
            StringComparison.Ordinal) &&
        ((tenantId is null && entity.TenantId is null) ||
         (tenantId is not null && entity.TenantId is not null &&
          string.Equals(
              IdentityProviderConfigurationCanonicalizer.Normalize(entity.TenantId),
              IdentityProviderConfigurationCanonicalizer.Normalize(tenantId),
              StringComparison.Ordinal))) &&
        IdentityProviderConfigurationCanonicalizer.Matches(tenantId, entity.TenantLookupKey, entity.Provider, entity.ProviderLookupKey);
}
