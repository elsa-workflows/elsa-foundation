using Elsa.Foundation.Identity.Api.ApiKeys;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity.Api.Applications;

/// <summary>An application and the status of its API key, <c>null</c> when it has none.</summary>
internal sealed record ApplicationState(ApplicationRecord Application, CredentialStatus? ApiKeyStatus);

/// <summary>
/// The application-management rules behind <c>/_elsa/identity/applications</c>: applications carry existing roles, each
/// has at most one API key, a rotated key replaces the previous one at once, and a revoked key stays revoked until the
/// next rotation. Writes compare revisions, so a concurrent change is a conflict rather than a lost update.
/// </summary>
internal sealed class ApplicationManager(
    IApplicationStore applications,
    IRevisionAwareApplicationStore revisionedApplications,
    ICredentialStore credentials,
    IRevisionAwareCredentialStore revisionedCredentials,
    IRoleStore roles)
{
    private const int MaximumDisplayNameLength = 256;
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    public async Task<IamPage<ApplicationState>> ListAsync(string tenantId, IamPageRequest request, CancellationToken cancellationToken)
    {
        var page = await applications.ListPageAsync(tenantId, request, cancellationToken);
        var items = new List<ApplicationState>(page.Items.Count);
        foreach (var application in page.Items)
            items.Add(await StateAsync(application, cancellationToken));
        return new IamPage<ApplicationState>(items, page.TotalCount);
    }

    public async Task<ApplicationState> GetAsync(string tenantId, string applicationId, CancellationToken cancellationToken) =>
        await StateAsync(await FindAsync(tenantId, applicationId, cancellationToken), cancellationToken);

    /// <summary>Creates an active application with a new API key, which the caller shows once.</summary>
    public async Task<(ApplicationState Application, string ApiKey)> CreateAsync(
        string tenantId,
        string? displayName,
        IReadOnlyList<string>? roleIds,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7().ToString("N");
        var application = new ApplicationRecord(
            id,
            tenantId,
            id,
            DisplayName(displayName),
            ApplicationType.Confidential,
            ResourceOwnership.Foundation,
            None,
            None,
            await ResolveRolesAsync(tenantId, roleIds ?? [], cancellationToken),
            ApplicationStatus.Active);
        EnsureSaved(await revisionedApplications.SaveWithRevisionAsync(application, expectedRevision: null, cancellationToken));
        var apiKey = await IssueApiKeyAsync(application, cancellationToken);
        return (new ApplicationState(application, CredentialStatus.Active), apiKey);
    }

    /// <summary>Replaces the display name, roles and status; every one is required.</summary>
    public async Task<ApplicationState> UpdateAsync(
        string tenantId,
        string applicationId,
        string? displayName,
        IReadOnlyList<string>? roleIds,
        bool? enabled,
        CancellationToken cancellationToken)
    {
        var name = DisplayName(displayName);
        if (roleIds is null)
            throw new ArgumentException("The application's role ids are required.", nameof(roleIds));
        if (enabled is null)
            throw new ArgumentException("Whether the application is enabled is required.", nameof(enabled));

        var current = await revisionedApplications.FindWithRevisionAsync(tenantId, applicationId, cancellationToken)
                      ?? throw ApplicationNotFoundException.For(applicationId);
        var updated = current.Record with
        {
            DisplayName = name,
            RoleIds = await ResolveRolesAsync(tenantId, roleIds, cancellationToken),
            Status = enabled.Value ? ApplicationStatus.Active : ApplicationStatus.Disabled
        };
        EnsureSaved(await revisionedApplications.SaveWithRevisionAsync(updated, current.Revision, cancellationToken));
        return await StateAsync(updated, cancellationToken);
    }

    public async Task DeleteAsync(string tenantId, string applicationId, CancellationToken cancellationToken)
    {
        // The key goes first: if deleting the application then fails, what remains is an application without a key,
        // never a key whose application a retry could still find.
        await credentials.DeleteAsync(tenantId, ApplicationApiKey.CredentialId(applicationId), cancellationToken);
        if (!await applications.DeleteAsync(tenantId, applicationId, cancellationToken))
            throw ApplicationNotFoundException.For(applicationId);
    }

    /// <summary>Issues a new API key, which replaces the current one immediately: there is no grace period.</summary>
    public async Task<string> RotateApiKeyAsync(string tenantId, string applicationId, CancellationToken cancellationToken) =>
        await IssueApiKeyAsync(await FindAsync(tenantId, applicationId, cancellationToken), cancellationToken);

    public async Task RevokeApiKeyAsync(string tenantId, string applicationId, CancellationToken cancellationToken)
    {
        _ = await FindAsync(tenantId, applicationId, cancellationToken);
        var current = await revisionedCredentials.FindWithRevisionAsync(tenantId, ApplicationApiKey.CredentialId(applicationId), cancellationToken)
                      ?? throw new ApplicationNotFoundException($"Application '{applicationId}' has no API key.");
        if (current.Record.Status != CredentialStatus.Revoked)
            EnsureSaved(await revisionedCredentials.SaveWithRevisionAsync(
                current.Record with { Status = CredentialStatus.Revoked }, current.Revision, cancellationToken));
    }

    private async Task<string> IssueApiKeyAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        var credentialId = ApplicationApiKey.CredentialId(application.Id);
        var current = await revisionedCredentials.FindWithRevisionAsync(application.TenantId, credentialId, cancellationToken);
        var apiKey = ApplicationApiKey.Generate(application.Id);
        var credential = new CredentialRecord(
            credentialId,
            application.TenantId,
            CredentialSubjectType.Application,
            application.Id,
            CredentialKind.ApiKey,
            ApplicationApiKey.Hash(apiKey),
            ApplicationApiKey.HashAlgorithm,
            CredentialStatus.Active,
            ExpiresAt: null);
        EnsureSaved(await revisionedCredentials.SaveWithRevisionAsync(credential, current?.Revision, cancellationToken));
        return apiKey;
    }

    private async Task<ApplicationRecord> FindAsync(string tenantId, string applicationId, CancellationToken cancellationToken) =>
        await applications.FindAsync(tenantId, applicationId, cancellationToken) ?? throw ApplicationNotFoundException.For(applicationId);

    private async Task<ApplicationState> StateAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        var credential = await credentials.FindAsync(application.TenantId, ApplicationApiKey.CredentialId(application.Id), cancellationToken);
        return new ApplicationState(application, credential?.Status);
    }

    /// <summary>Every role must exist in the tenant; the application keeps each role's own id.</summary>
    private async Task<IReadOnlySet<string>> ResolveRolesAsync(string tenantId, IReadOnlyList<string> roleIds, CancellationToken cancellationToken)
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleId in roleIds)
        {
            if (string.IsNullOrWhiteSpace(roleId))
                throw new ArgumentException("A role id cannot be blank.", nameof(roleIds));

            var role = await roles.FindAsync(tenantId, roleId, cancellationToken)
                       ?? throw new ArgumentException($"Role '{roleId}' does not exist.", nameof(roleIds));
            resolved.Add(role.Id);
        }

        return resolved;
    }

    private static string DisplayName(string? displayName)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("The application's display name is required.", nameof(displayName));
        if (name.Length > MaximumDisplayNameLength)
            throw new ArgumentException($"The application's display name cannot exceed {MaximumDisplayNameLength} characters.", nameof(displayName));
        return name;
    }

    private static void EnsureSaved(IamRevisionSaveResult result)
    {
        switch (result.Status)
        {
            case IamRevisionSaveStatus.Saved:
                return;
            case IamRevisionSaveStatus.NotFound:
                throw new ApplicationNotFoundException("The application no longer exists.");
            default:
                throw new ApplicationConflictException();
        }
    }
}

internal sealed class ApplicationNotFoundException(string message) : Exception(message)
{
    public static ApplicationNotFoundException For(string applicationId) => new($"Application '{applicationId}' was not found.");
}

internal sealed class ApplicationConflictException()
    : Exception("The application or its API key was changed concurrently. Read it again and retry.");
