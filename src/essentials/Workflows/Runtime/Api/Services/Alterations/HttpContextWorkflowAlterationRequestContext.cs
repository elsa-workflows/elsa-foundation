using System.Security.Claims;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Services.Alterations;

/// <summary>Uses the immutable persistence scope as the alteration tenant boundary and captures only safe operator facts.</summary>
public sealed class HttpContextWorkflowAlterationRequestContext(
    IHttpContextAccessor httpContextAccessor,
    IPersistenceAccessContextAccessor persistenceAccessContextAccessor) : IWorkflowAlterationRequestContext
{
    private const string SystemIdentity = "runtime-api";
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    private string TenantPartition => persistenceAccessContextAccessor.Current.Scope?.Value ?? "global";

    public WorkflowAlterationAuthorityScope AuthorityScope => new(
        TenantPartition,
        SystemIdentity,
        SystemIdentity);

    public WorkflowAlterationOperatorProvenance Operator => new(Subject, HttpContext?.TraceIdentifier);

    public bool CanAccess(WorkflowAlterationPlanState plan) =>
        plan is not null &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.TenantPartition, TenantPartition) &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.SystemIdentity, SystemIdentity) &&
        StringComparer.Ordinal.Equals(plan.AuthorityScope.RootInitiator, SystemIdentity) &&
        WorkflowExecutionAuthoritySnapshot.MetadataEquals(plan.AuthorityScope.Metadata, AuthorityScope.Metadata);

    private string Subject
    {
        get
        {
            var principal = HttpContext?.User;
            var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value;
            return string.IsNullOrWhiteSpace(subject) ? "authenticated-operator" : subject;
        }
    }
}
