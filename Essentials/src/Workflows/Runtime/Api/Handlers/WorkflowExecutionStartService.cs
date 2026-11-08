using System.Text.Json;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class WorkflowExecutionStartService : IWorkflowExecutionStartService
{
    private const string RequestedBy = "runtime-api";
    private static readonly RuntimeVariableDeclarationProjector VariableDeclarations = new();
    private readonly IWorkflowStartDispatcher _startDispatcher;
    private readonly IWorkflowExecutableStore _executableStore;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly NormalizedPrincipalValidator? _principalValidator;

    /// <summary>
    /// Creates an embedded start service with no HTTP identity context. Starts through this
    /// compatibility constructor remain tenant-unspecified.
    /// </summary>
    public WorkflowExecutionStartService(
        IWorkflowStartDispatcher startDispatcher,
        IWorkflowExecutableStore executableStore)
    {
        _startDispatcher = startDispatcher ?? throw new ArgumentNullException(nameof(startDispatcher));
        _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    }

    /// <summary>Creates the HTTP start service, deriving tenant authority from a normalized principal.</summary>
    public WorkflowExecutionStartService(
        IWorkflowStartDispatcher startDispatcher,
        IWorkflowExecutableStore executableStore,
        IHttpContextAccessor httpContextAccessor,
        NormalizedPrincipalValidator? principalValidator = null)
        : this(startDispatcher, executableStore)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _principalValidator = principalValidator;
    }

    public async Task<WorkflowExecutionStartDispatchView> ExecuteAsync(ExecuteWorkflow request, CancellationToken cancellationToken)
    {
        // Resolve authority before looking up the requested artifact. A malformed HTTP principal
        // must not be interpreted as a global-tenant request or learn whether an artifact exists.
        var tenantId = ResolveTenantId();

        // Seed authored workflow variable defaults so `variables.*` input expressions resolve to their
        // declared values in production (Seam C, #254). The defaults are read from the compiled executable's
        // workflow-scope declarations, not the design document, keeping authored content separate from the
        // runtime artifact. A missing executable is left to the dispatcher to reject with its own diagnostics.
        var executable = await _executableStore.FindAsync(request.ArtifactId, cancellationToken);
        var variables = executable is null
            ? EmptyVariables
            : VariableDeclarations.ProjectDeclaredVariableDefaultsByName(executable.WorkflowVariables);

        // Caller-supplied workflow inputs (#286): threaded into the start dispatch so `input.*` expressions
        // resolve to them in production. Unlike variables (which carry authored defaults), inputs have no
        // artifact-side default, so an empty channel simply leaves `input.*` empty.
        var inputs = ToInputValues(request.Inputs);

        var result = await _startDispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                request.ArtifactId,
                RequestedBy,
                workflowExecutionId: null,
                idempotencyKey: null,
                metadata: null,
                variables: variables,
                inputs: inputs,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: request.SourceReferenceId is null
                    ? null
                    : new WorkflowExecutableSourceSelection(sourceReferenceId: request.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: null,
                tenantId: tenantId,
                partition: null,
                authority: null),
            cancellationToken: cancellationToken);
        return WorkflowExecutionStartDispatchView.From(result);
    }

    private string? ResolveTenantId()
    {
        // A service composed without an HTTP context is an embedded/runtime use, not an HTTP
        // request with missing identity data. Preserve the old tenant-unspecified behavior there.
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
            return null;

        if (_principalValidator is null || !_principalValidator.TryGetNormalizedPrincipal(httpContext.User, out var principal))
            throw new RuntimeResourceMissingSignal();

        var tenantClaims = principal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (tenantClaims.Length > 1 || tenantClaims.Any(claim => string.IsNullOrWhiteSpace(claim.Value)))
            throw new RuntimeResourceMissingSignal();

        // Absence is the explicit global-tenant identity. Do not fall back to alternate claim names
        // or infer tenancy from the independently selected persistence partition.
        return tenantClaims.SingleOrDefault()?.Value;
    }

    private static IReadOnlyDictionary<string, object?> ToInputValues(IReadOnlyDictionary<string, JsonElement>? inputs) =>
        inputs is not { Count: > 0 }
            ? EmptyVariables
            : inputs.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> EmptyVariables = new Dictionary<string, object?>(StringComparer.Ordinal);
}

/// <summary>The published-workflow start operation the runtime execute endpoint dispatches to.</summary>
public interface IWorkflowExecutionStartService
{
    Task<WorkflowExecutionStartDispatchView> ExecuteAsync(ExecuteWorkflow request, CancellationToken cancellationToken);
}
