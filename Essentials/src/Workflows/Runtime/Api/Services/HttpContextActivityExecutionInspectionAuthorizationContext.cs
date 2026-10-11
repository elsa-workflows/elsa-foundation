using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>
/// Fail-closed HTTP authorization adapter with independent structure and captured-value grants.
/// Permission decisions use Foundation Identity's canonical asynchronous evaluator.
/// </summary>
public sealed class HttpContextActivityExecutionInspectionAuthorizationContext : IActivityInspectionContextAsync
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
    private readonly CancellationToken _requestCancellationToken;
    private Lazy<Task<AuthorizationSnapshot>>? _snapshot;

    [ActivatorUtilitiesConstructor]
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
        _requestCancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;
    }

    public string TenantScope => _tenantId is null ? "global" : $"tenant:{_tenantId}";
    public string AuditSubject => _auditSubject;
    public string RequestCorrelationId => _requestCorrelationId;

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
        var canInspectStructure = await AuthorizeAsync(StructurePermission, resource: null, _requestCancellationToken).ConfigureAwait(false);
        var canInspectSensitiveValues = await AuthorizeAsync(SensitiveValuesPermission, resource: null, _requestCancellationToken).ConfigureAwait(false);
        var canResolveValuePayloads = await AuthorizeAsync(ResolveValuePayloadsPermission, resource: null, _requestCancellationToken).ConfigureAwait(false);
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

    private sealed record AuthorizationSnapshot(
        bool CanInspectStructure,
        bool CanInspectSensitiveValues,
        bool CanResolveValuePayloads,
        string Profile);

    private static readonly AuthorizationSnapshot DeniedSnapshot =
        new(false, false, false, "untrusted");
}
