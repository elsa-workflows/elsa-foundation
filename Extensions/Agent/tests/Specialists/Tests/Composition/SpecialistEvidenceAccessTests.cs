using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Inspection.Api;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

// Evidence re-authorizes per run with the runtime's inspection permissions; holding one never implies the next.
public sealed class SpecialistEvidenceAccessTests
{
    [Fact]
    public async Task Structure_and_values_are_granted_separately_and_only_in_the_callers_tenant()
    {
        var workflows = new InMemoryWorkflowExecutionStateStore();
        await workflows.SaveAsync(Run("own", "tenant-1"));
        await workflows.SaveAsync(Run("foreign", "tenant-2"));
        await workflows.SaveAsync(Run("shared", null));

        Assert.Equal(new SpecialistEvidenceGrant(true, true, true),
            await Access(workflows, SpecialistEvidenceAccess.InspectStructure, SpecialistEvidenceAccess.InspectValues).AuthorizeAsync("own", default));
        Assert.Equal(new SpecialistEvidenceGrant(true, true, false),
            await Access(workflows, SpecialistEvidenceAccess.InspectStructure).AuthorizeAsync("own", default));
        // A values grant without structure does not reveal values: the runtime nests them the same way.
        Assert.Equal(new SpecialistEvidenceGrant(true, false, false),
            await Access(workflows, SpecialistEvidenceAccess.InspectValues).AuthorizeAsync("own", default));
        Assert.Equal(new SpecialistEvidenceGrant(false, false, false),
            await Access(workflows, SpecialistEvidenceAccess.InspectStructure, SpecialistEvidenceAccess.InspectValues).AuthorizeAsync("foreign", default));
        Assert.Equal(new SpecialistEvidenceGrant(false, false, false),
            await Access(workflows, SpecialistEvidenceAccess.InspectStructure).AuthorizeAsync("missing", default));
        Assert.Equal(new SpecialistEvidenceGrant(false, false, false),
            await Access(workflows, [SpecialistEvidenceAccess.InspectStructure], trusted: false).AuthorizeAsync("own", default));
        // A run without a tenant is open to any trusted caller, as in the runtime; a caller without a tenant sees only those.
        Assert.Equal(new SpecialistEvidenceGrant(true, true, false),
            await Access(workflows, SpecialistEvidenceAccess.InspectStructure).AuthorizeAsync("shared", default));
        Assert.Equal(new SpecialistEvidenceGrant(false, false, false),
            await Access(workflows, [SpecialistEvidenceAccess.InspectStructure], trusted: true, tenant: null).AuthorizeAsync("own", default));
        Assert.Equal(new SpecialistEvidenceGrant(true, true, false),
            await Access(workflows, [SpecialistEvidenceAccess.InspectStructure], trusted: true, tenant: null).AuthorizeAsync("shared", default));
    }

    [Fact]
    public void Redaction_keeps_the_capture_decision_and_drops_only_the_value()
    {
        var evidence = new SpecialistInvocationEvidence("own", "activity", new("definition", "version-id", "1", "template", "artifact", "hash", false),
            new("anthropic", "connection", "model"), new(), "Completed", null, new("Payload", JsonDocument.Parse("{\"Article\":\"text\"}").RootElement), null);

        Assert.Equal(new SpecialistInvocationOutputEvidence("Payload", null), SpecialistEvidenceAccess.Redact(evidence, values: false)!.Output);
        Assert.Same(evidence, SpecialistEvidenceAccess.Redact(evidence, values: true));
    }

    private static WorkflowExecutionState Run(string id, string? tenant)
    {
        var now = DateTimeOffset.UtcNow;
        return new(id, new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "sha256:hash"),
            WorkflowExecutionStatus.Completed, null, now, now, now, now, null, null, tenant, new Dictionary<string, string>());
    }

    private static SpecialistEvidenceAccess Access(InMemoryWorkflowExecutionStateStore workflows, params string[] granted) => Access(workflows, granted, trusted: true);

    private static SpecialistEvidenceAccess Access(InMemoryWorkflowExecutionStateStore workflows, string[] granted, bool trusted, string? tenant = "tenant-1")
    {
        var claims = new List<Claim>();
        if (tenant is not null) claims.Add(new(IdentityClaimTypes.TenantId, tenant));
        if (trusted) claims.Add(new(IdentityClaimTypes.Normalized, "v1"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        var options = Options.Create(new FoundationIdentityOptions { NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" } });
        return new(new HttpContextAccessor { HttpContext = context }, new NormalizedPrincipalValidator(options), new Grants(granted), workflows);
    }

    private sealed class Grants(string[] granted) : IPermissionAuthorizationService
    {
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(granted.Contains(context.Permission) && context.Resource is WorkflowExecutionState
                ? PermissionEvaluationResult.Success : new PermissionEvaluationResult(false, "denied"));
    }
}
