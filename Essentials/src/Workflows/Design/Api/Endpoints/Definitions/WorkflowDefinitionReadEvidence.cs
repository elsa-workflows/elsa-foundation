using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// What one successful <see cref="WorkflowDefinitionDetailsReader.ReadAsync"/> established in the current request, so
/// the built-in <c>HttpWorkflowDefinitionAuthorizer</c> need not repeat it for the same definition straight after.
/// </summary>
/// <remarks>
/// <para>Only the built-in reader records it, at the end of a successful read, and only the built-in authorizer consumes
/// it. The type and its key are internal, so a replacement reader can never produce it and a replacement authorizer never
/// sees it. It lives in the request's own <see cref="HttpContext.Items"/>, so it cannot cross requests.</para>
/// <para>It is single-use: the authorizer takes it on its next decision, whichever definition that decision is for, and
/// every new read discards the previous evidence first, so a failed read leaves none. It stands for a decision only when
/// the same reader instance (hence the same DI scope), the same definition store, the same principal, the same definition
/// id and the principal's own tenant all match. It stands for the Read permission and the organization checks only when
/// the reader made them, with the same scoped permission service the authorizer uses. It never stands for Manage.</para>
/// </remarks>
internal sealed class WorkflowDefinitionReadEvidence
{
    private static readonly object ItemKey = new();

    private readonly WorkflowDefinitionDetailsReader _reader;
    private readonly IWorkflowDefinitionStore _store;
    private readonly IPermissionAuthorizationService? _permissions;
    private readonly ClaimsPrincipal _user;
    private readonly string _definitionId;
    private readonly string? _tenantId;

    private WorkflowDefinitionReadEvidence(WorkflowDefinitionDetailsReader reader, IWorkflowDefinitionStore store,
        IPermissionAuthorizationService? permissions, ClaimsPrincipal user, string definitionId, string? tenantId)
    {
        _reader = reader;
        _store = store;
        _permissions = permissions;
        _user = user;
        _definitionId = definitionId;
        _tenantId = tenantId;
    }

    /// <summary>Discards any earlier evidence, so a read that does not complete leaves none behind.</summary>
    internal static void Discard(HttpContext? context) => context?.Items.Remove(ItemKey);

    /// <summary>Records what a completed built-in read established.</summary>
    /// <param name="permissions">The scoped permission service the reader's Read permission and organization checks
    /// used, or <c>null</c> when it made none (no workflow library store is registered).</param>
    internal static void Record(HttpContext context, WorkflowDefinitionDetailsReader reader, IWorkflowDefinitionStore store,
        IPermissionAuthorizationService? permissions, string requestedId, WorkflowDefinition definition)
    {
        if (!string.Equals(definition.Id, requestedId, StringComparison.Ordinal))
            return;
        context.Items[ItemKey] = new WorkflowDefinitionReadEvidence(reader, store, permissions, context.User, definition.Id, definition.TenantId);
    }

    /// <summary>Removes and returns the request's evidence, if any.</summary>
    internal static WorkflowDefinitionReadEvidence? Take(HttpContext context)
    {
        if (!context.Items.Remove(ItemKey, out var value))
            return null;
        return value as WorkflowDefinitionReadEvidence;
    }

    /// <summary>Whether the read confirmed, for this exact owner composition and principal, that the definition exists
    /// in the principal's tenant and that the reader's organization preflight would pass.</summary>
    internal bool Confirms(IWorkflowDefinitionDetailsReader reader, IWorkflowDefinitionStore store, ClaimsPrincipal user,
        string definitionId, string tenantId) =>
        ReferenceEquals(_reader, reader)
        && ReferenceEquals(_store, store)
        && ReferenceEquals(_user, user)
        && string.Equals(_definitionId, definitionId, StringComparison.Ordinal)
        && string.Equals(_tenantId, tenantId, StringComparison.Ordinal);

    /// <summary>Whether the read also made the Read permission decision through <paramref name="permissions"/>.</summary>
    internal bool DecidedReadPermissionWith(IPermissionAuthorizationService permissions) =>
        _permissions is not null && ReferenceEquals(_permissions, permissions);
}
