using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Fail-closed HTTP authorization adapter with independent structure and captured-value grants.
/// Permission decisions use Foundation Identity's canonical asynchronous evaluator. Synchronous
/// members remain only for source compatibility and fail closed during the advisory window.
/// </summary>
public sealed class HttpContextActivityExecutionInspectionAuthorizationContext :
    IActivityInspectionContext,
    IActivityInspectionContextAsync
{
    public const string StructurePermission = "workflows.activity-executions.inspect";
    public const string SensitiveValuesPermission = "workflows.activity-executions.inspect-values";
    public const string ResolveValuePayloadsPermission = "workflows.activity-executions.resolve-value-payloads";
    private readonly IPermissionAuthorizationService _authorization;
    private readonly NormalizedPrincipalValidator _principalValidator;
    private readonly ClaimsPrincipal _principal;
    private readonly bool _trusted;
    private readonly string? _tenantId;
    private readonly string _auditSubject;
    private readonly string _requestCorrelationId;
    private Lazy<Task<AuthorizationSnapshot>>? _snapshot;

    public HttpContextActivityExecutionInspectionAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        IPermissionAuthorizationService authorization,
        NormalizedPrincipalValidator principalValidator)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _principalValidator = principalValidator ?? throw new ArgumentNullException(nameof(principalValidator));

        var httpContext = httpContextAccessor.HttpContext;
        var rawPrincipal = httpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        _trusted = _principalValidator.TryGetNormalizedPrincipal(rawPrincipal, out var normalizedPrincipal);
        _principal = _trusted ? normalizedPrincipal : new ClaimsPrincipal(new ClaimsIdentity());
        _tenantId = _trusted ? FindTenantId(_principal) : null;
        _auditSubject = _trusted ? FindAuditSubject(_principal) : string.Empty;
        _requestCorrelationId = httpContext?.TraceIdentifier ?? string.Empty;
    }

    public string TenantScope => _tenantId is null ? "global" : $"tenant:{_tenantId}";
    public string AuditSubject => _auditSubject;
    public string RequestCorrelationId => _requestCorrelationId;

    [Obsolete("Use IActivityInspectionContextAsync.GetAuthorizationProfileAsync.")]
    public string AuthorizationProfile => throw SynchronousAccess();

    [Obsolete("Use IActivityInspectionContextAsync.CanInspectStructureAsync.")]
    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => throw SynchronousAccess();

    [Obsolete("Use IActivityInspectionContextAsync.CanInspectSensitiveValuesAsync.")]
    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => throw SynchronousAccess();

    [Obsolete("Use IActivityInspectionContextAsync.CanResolveSensitiveValuePayloadsAsync.")]
    public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => throw SynchronousAccess();

    public async ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profile;
    }

    public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        CanAccess(workflowExecution)
            ? AuthorizeAsync(StructurePermission, workflowExecution, cancellationToken)
            : ValueTask.FromResult(false);

    public async ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
    {
        if (!CanAccess(workflowExecution) || !await CanInspectStructureAsync(workflowExecution, cancellationToken).ConfigureAwait(false))
            return false;

        return await AuthorizeAsync(SensitiveValuesPermission, workflowExecution, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
    {
        if (!CanAccess(workflowExecution) || !await CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken).ConfigureAwait(false))
            return false;

        return await AuthorizeAsync(ResolveValuePayloadsPermission, workflowExecution, cancellationToken).ConfigureAwait(false);
    }

    private bool CanAccess(WorkflowExecutionState workflowExecution) =>
        _trusted && (workflowExecution.TenantId is null || StringComparer.Ordinal.Equals(workflowExecution.TenantId, _tenantId));

    private async ValueTask<bool> AuthorizeAsync(
        string permission,
        WorkflowExecutionState workflowExecution,
        CancellationToken cancellationToken)
    {
        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(_principal, permission, _tenantId, workflowExecution),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private async Task<AuthorizationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!_trusted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DeniedSnapshot;
        }

        var existing = Volatile.Read(ref _snapshot);
        if (existing is null)
        {
            var created = new Lazy<Task<AuthorizationSnapshot>>(
                CreateSnapshotAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
            existing = Interlocked.CompareExchange(ref _snapshot, created, null) ?? created;
        }

        var snapshotTask = existing.Value;
        try
        {
            // Keep caller cancellation local to this wait. One canceled caller must not
            // cancel the shared computation used by concurrent callers.
            return await snapshotTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (snapshotTask.IsCanceled || snapshotTask.IsFaulted)
                Interlocked.CompareExchange(ref _snapshot, null, existing);
            throw;
        }
    }

    private async Task<AuthorizationSnapshot> CreateSnapshotAsync()
    {
        var canInspectStructure = await AuthorizeAsync(StructurePermission, resource: null, CancellationToken.None).ConfigureAwait(false);
        var canInspectSensitiveValues = await AuthorizeAsync(SensitiveValuesPermission, resource: null, CancellationToken.None).ConfigureAwait(false);
        var canResolveValuePayloads = await AuthorizeAsync(ResolveValuePayloadsPermission, resource: null, CancellationToken.None).ConfigureAwait(false);
        var material = $"{TenantScope}|structure:{canInspectStructure}|values:{canInspectSensitiveValues}|resolve:{canResolveValuePayloads}";
        var profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return new AuthorizationSnapshot(canInspectStructure, canInspectSensitiveValues, canResolveValuePayloads, profile);
    }

    private async ValueTask<bool> AuthorizeAsync(string permission, object? resource, CancellationToken cancellationToken)
    {
        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(_principal, permission, _tenantId, resource),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static string? FindTenantId(ClaimsPrincipal principal) =>
        principal.FindFirst(IdentityClaimTypes.TenantId)?.Value
        ?? principal.FindFirst("tenant_id")?.Value;

    private static string FindAuditSubject(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
            return subject;

        subject = principal.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(subject) ? string.Empty : subject;
    }

    private static InvalidOperationException SynchronousAccess() =>
        new("Synchronous activity inspection authorization access is obsolete and intentionally unavailable. Use the asynchronous authorization context.");

    private sealed record AuthorizationSnapshot(
        bool CanInspectStructure,
        bool CanInspectSensitiveValues,
        bool CanResolveValuePayloads,
        string Profile);

    private static readonly AuthorizationSnapshot DeniedSnapshot =
        new(false, false, false, "untrusted");
}

public sealed class LegacyActivityInspectionContextAdapter : IActivityInspectionContext, IActivityInspectionContextAsync
{
    private readonly IActivityExecutionInspectionAuthorizationContext _legacy;

    public LegacyActivityInspectionContextAdapter(IActivityExecutionInspectionAuthorizationContext legacy) =>
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));

    public string TenantScope => _legacy.TenantScope;
    public string AuthorizationProfile => _legacy.AuthorizationProfile;
    public string AuditSubject => _legacy.AuditSubject;
    public string RequestCorrelationId => _legacy.RequestCorrelationId;
    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => _legacy.CanInspectStructure(workflowExecution);
    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => _legacy.CanInspectSensitiveValues(workflowExecution);
    public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => _legacy.CanResolveSensitiveValuePayloads(workflowExecution);
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<string>(cancellationToken) : ValueTask.FromResult(_legacy.AuthorizationProfile);
    public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanInspectStructure(workflowExecution));
    public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanInspectSensitiveValues(workflowExecution));
    public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanResolveSensitiveValuePayloads(workflowExecution));
}

public sealed class LegacyActivityInspectionAsyncAliasAdapter : IActivityInspectionContextAsync
{
    private readonly IActivityExecutionInspectionAuthorizationContextAsync _legacy;

    public LegacyActivityInspectionAsyncAliasAdapter(IActivityExecutionInspectionAuthorizationContextAsync legacy) =>
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));

    public string TenantScope => _legacy.TenantScope;
    public string AuditSubject => _legacy.AuditSubject;
    public string RequestCorrelationId => _legacy.RequestCorrelationId;
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
        _legacy.GetAuthorizationProfileAsync(cancellationToken);
    public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        _legacy.CanInspectStructureAsync(workflowExecution, cancellationToken);
    public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        _legacy.CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken);
    public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        _legacy.CanResolveSensitiveValuePayloadsAsync(workflowExecution, cancellationToken);
}
