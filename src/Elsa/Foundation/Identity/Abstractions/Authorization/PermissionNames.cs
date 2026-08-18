namespace Elsa.Foundation.Identity.Abstractions.Authorization;

/// <summary>
/// The one permission name that is not owned by any domain.
/// </summary>
/// <remarks>
/// <see cref="All"/> is the wildcard every endpoint accepts in addition to its own permission, so it
/// is the only name that genuinely belongs to the shared endpoint security convention rather than to
/// a domain. <see cref="EndpointPermissionPolicy"/> composes it.
/// <para>
/// This type previously also carried the action-scoped names for Workflow Design, Activity Design,
/// Expressions, Workflow Publishing, Workflow Runtime, API Capabilities, Elsa 3 import, and BPMN
/// interchange. That was a layering compromise inherited from the first-party FastEndpoints project:
/// those names belong to their domains, not to a shared library. They were also redundant, because
/// every one of those owners already declares its own permissions class — for example
/// <c>Elsa.Workflows.Design.Api.Authorization.WorkflowDesignPermissions</c> and
/// <c>Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions</c>. Consumers now use the
/// owning domain's class, which is the single source for that domain's permission names.
/// </para>
/// </remarks>
public static class PermissionNames
{
    /// <summary>The all-access wildcard permission.</summary>
    public const string All = "*";
}
