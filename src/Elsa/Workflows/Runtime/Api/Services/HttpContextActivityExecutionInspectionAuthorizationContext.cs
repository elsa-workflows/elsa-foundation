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
    IActivityExecutionInspectionAuthorizationContext,
    IActivityExecutionInspectionAuthorizationContextAsync
{
    public const string StructurePermission = "workflows.activity-executions.inspect";
    public const string SensitiveValuesPermission = "workflows.activity-executions.inspect-values";
    public const string ResolveValuePayloadsPermission = "workflows.activity-executions.resolve-value-payloads";
    private readonly IPermissionAuthorizationService _authorization;
    private readonly ClaimsPrincipal _principal;
    private readonly string? _tenantId;
    private readonly string _auditSubject;
    private readonly string _requestCorrelationId;
    private Lazy<Task<AuthorizationSnapshot>>? _snapshot;

    public HttpContextActivityExecutionInspectionAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        IPermissionAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

        var httpContext = httpContextAccessor.HttpContext;
        _principal = httpContext?.User is { } user
            ? new ClaimsPrincipal(user)
            : new ClaimsPrincipal(new ClaimsIdentity());
        _tenantId = FindTenantId(_principal);
        _auditSubject = FindAuditSubject(_principal);
        _requestCorrelationId = httpContext?.TraceIdentifier ?? string.Empty;
    }

    public string TenantScope => _tenantId is null ? "global" : $"tenant:{_tenantId}";
    public string AuditSubject => _auditSubject;
    public string RequestCorrelationId => _requestCorrelationId;

    [Obsolete("Use IActivityExecutionInspectionAuthorizationContextAsync.GetAuthorizationProfileAsync.")]
    public string AuthorizationProfile => throw SynchronousAccess();

    [Obsolete("Use IActivityExecutionInspectionAuthorizationContextAsync.CanInspectStructureAsync.")]
    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => throw SynchronousAccess();

    [Obsolete("Use IActivityExecutionInspectionAuthorizationContextAsync.CanInspectSensitiveValuesAsync.")]
    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => throw SynchronousAccess();

    [Obsolete("Use IActivityExecutionInspectionAuthorizationContextAsync.CanResolveSensitiveValuePayloadsAsync.")]
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
        workflowExecution.TenantId is null || StringComparer.Ordinal.Equals(workflowExecution.TenantId, _tenantId);

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

    private Task<AuthorizationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _snapshot);
        if (existing is not null)
            return existing.Value;

        var created = new Lazy<Task<AuthorizationSnapshot>>(
            () => CreateSnapshotAsync(cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var winner = Interlocked.CompareExchange(ref _snapshot, created, null) ?? created;
        return winner.Value;
    }

    private async Task<AuthorizationSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken)
    {
        var canInspectStructure = await AuthorizeAsync(StructurePermission, resource: null, cancellationToken).ConfigureAwait(false);
        var canInspectSensitiveValues = await AuthorizeAsync(SensitiveValuesPermission, resource: null, cancellationToken).ConfigureAwait(false);
        var canResolveValuePayloads = await AuthorizeAsync(ResolveValuePayloadsPermission, resource: null, cancellationToken).ConfigureAwait(false);
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
}
