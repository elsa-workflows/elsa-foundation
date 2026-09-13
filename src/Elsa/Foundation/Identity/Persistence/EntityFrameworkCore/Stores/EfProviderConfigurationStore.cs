using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF-backed tenant/global provider configuration store. It owns only I06/I07 and never delegates
/// to Groundwork or requires a provider engine in this assembly.
/// </summary>
public sealed class EfProviderConfigurationStore(
    IdentityProviderConfigurationDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IProviderConfigurationStore, IRevisionAwareProviderConfigurationStore
{
    private const int MaximumWriteAttempts = 3;

    public async ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to read the tenant provider configuration.", exception);
        }
    }

    public ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default) =>
        new(SaveUnconditionallyAsync(configuration, cancellationToken));

    public async ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(provider, nameof(provider));
        try
        {
            var row = await FindGlobalEntityAsync(provider, cancellationToken);
            return row is null ? null : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), IdentityProviderConfigurationRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
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
        try
        {
            var row = await FindTenantEntityAsync(tenantId, provider, cancellationToken);
            return row is null ? null : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), IdentityProviderConfigurationRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
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
        if (expectedRevision is not null && !IdentityProviderConfigurationRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion))
            return Conflict();

        if (expectedRevision is null)
            return await SaveCreateOnlyAsync(configuration, cancellationToken);

        return await SaveCompareAndSwapAsync(configuration, expectedVersion, cancellationToken);
    }

    private async Task SaveUnconditionallyAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateConfiguration(configuration);
        EnsureWriteAccess(configuration);

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var row = await FindEntityForWriteAsync(configuration, cancellationToken);
                if (row is null)
                {
                    context.Add(CreateEntity(configuration, revision: 1));
                }
                else
                {
                    Apply(row, configuration);
                    row.Revision = checked(row.Revision + 1);
                }

                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaximumWriteAttempts)
            {
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) && attempt + 1 < MaximumWriteAttempts)
            {
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw Failure("Unable to save the provider configuration.", exception);
            }
        }

        context.ChangeTracker.Clear();
        throw Failure("Unable to save the provider configuration after bounded concurrency retries.", new InvalidOperationException("The provider configuration was concurrently modified."));
    }

    private async Task<IamRevisionSaveResult> SaveCreateOnlyAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken)
    {
        EnsureWriteAccess(configuration);
        try
        {
            if (await FindEntityForWriteAsync(configuration, cancellationToken) is not null)
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }

            context.Add(CreateEntity(configuration, revision: 1));
            await context.SaveChangesAsync(cancellationToken);
            return Saved(1);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            context.ChangeTracker.Clear();
            return Conflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return Conflict();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to create the provider configuration.", exception);
        }
    }

    private async Task<IamRevisionSaveResult> SaveCompareAndSwapAsync(ProviderConfigurationRecord configuration, long expectedVersion, CancellationToken cancellationToken)
    {
        EnsureWriteAccess(configuration);
        try
        {
            var row = await FindEntityForWriteAsync(configuration, cancellationToken);
            if (row is null)
            {
                context.ChangeTracker.Clear();
                return new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
            }
            if (row.Revision != expectedVersion)
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }

            var nextRevision = checked(row.Revision + 1);
            Apply(row, configuration);
            row.Revision = nextRevision;
            await context.SaveChangesAsync(cancellationToken);
            return Saved(nextRevision);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            try
            {
                return await ExistsAsync(configuration, cancellationToken) ? Conflict() : new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                throw Failure("Unable to classify the provider configuration concurrency conflict.", exception);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to update the provider configuration.", exception);
        }
    }

    private async Task<ProviderConfigurationEntity?> FindEntityForWriteAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken)
    {
        if (configuration.TenantId is null)
            return await FindGlobalEntityAsync(configuration.Provider, cancellationToken);
        return await FindTenantEntityAsync(configuration.TenantId, configuration.Provider, cancellationToken);
    }

    private async Task<GlobalProviderConfigurationEntity?> FindGlobalEntityAsync(string provider, CancellationToken cancellationToken)
    {
        IdentityEntityFrameworkAccessGuard.EnsureGlobal(accessContextAccessor);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.GlobalProviderId(provider);
            var row = await context.GlobalProviderConfigurations.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null || !Matches(row, null, provider) ? null : row;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to query the global provider configuration.", exception);
        }
    }

    private async Task<TenantProviderConfigurationEntity?> FindTenantEntityAsync(string tenantId, string provider, CancellationToken cancellationToken)
    {
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var id = IdentityProviderConfigurationCanonicalizer.TenantProviderId(tenantId, provider);
            var row = await context.TenantProviderConfigurations.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            return row is null || !Matches(row, tenantId, provider) ? null : row;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            throw Failure("Unable to query the tenant provider configuration.", exception);
        }
    }

    private async Task<bool> ExistsAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken) =>
        await FindEntityForWriteAsync(configuration, cancellationToken) is not null;

    private void EnsureWriteAccess(ProviderConfigurationRecord configuration)
    {
        if (configuration.TenantId is null)
            IdentityEntityFrameworkAccessGuard.EnsurePrivilegedGlobal(accessContextAccessor);
        else
            IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, configuration.TenantId);
    }

    private static ProviderConfigurationEntity CreateEntity(ProviderConfigurationRecord configuration, long revision)
    {
        ProviderConfigurationEntity entity = configuration.TenantId is null
            ? new GlobalProviderConfigurationEntity()
            : new TenantProviderConfigurationEntity();
        entity.Revision = revision;
        Apply(entity, configuration);
        return entity;
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
        entity.SettingsJson = SerializeSettings(configuration.Settings);
    }

    private static ProviderConfigurationRecord Map(ProviderConfigurationEntity entity)
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.SettingsJson) ?? new(StringComparer.Ordinal);
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
            settings);
    }

    private static string SerializeSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var ordered = settings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(ordered);
    }

    private static IamRevisionSaveResult Saved(long revision) => new(IamRevisionSaveStatus.Saved, IdentityProviderConfigurationRevisionCodec.FromVersion(revision));
    private static IamRevisionSaveResult Conflict() => new(IamRevisionSaveStatus.Conflict);
    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => new(message, exception);

    private static void ValidateConfiguration(ProviderConfigurationRecord configuration)
    {
        IdentityProviderConfigurationCanonicalizer.Validate(configuration.TenantId, nameof(configuration.TenantId));
        IdentityProviderConfigurationCanonicalizer.Validate(configuration.Provider, nameof(configuration.Provider));
        IdentityProviderConfigurationCanonicalizer.Validate(configuration.Kind, nameof(configuration.Kind));
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
