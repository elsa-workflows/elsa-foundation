using Elsa.Expressions.Core.Contracts;
using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Shares the admitted initializer's data validation without constructing execution authority.</summary>
internal sealed class RuntimeTestContextInspector(IWorkflowExecutableInputValidator? valueValidator = null,
    IWellKnownTypeRegistry? types = null, IInlineEventPublisher? publisher = null, IPortableExpressionEvaluator? evaluator = null) : IRuntimeTestContextInspector
{
    public async ValueTask<RuntimeTestContextPreparation> InspectAsync(RuntimeTestContextInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Executable);
        ArgumentNullException.ThrowIfNull(request.Seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Seed.ContextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Seed.ContextFingerprint);
        ArgumentNullException.ThrowIfNull(request.WorkflowInputs);
        ArgumentNullException.ThrowIfNull(request.RootVariableSeeds);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            VariableFrameState root;
            try { root = RuntimeRootVariableFrameFactory.Create(request.WorkflowExecutionId, request.RootVariableSeeds, request.Executable); }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.root-unavailable",
                    "The pinned workflow root declarations require an unavailable initialization profile.", innerException: exception);
            }
            var prepared = await RuntimeTestContextMaterializer.CreateFromSeedAsync(request.Executable,
                request.ExecutableNodeId, request.WorkflowExecutionId, root, request.Seed, publisher,
                request.WorkflowInputs, request.RootVariableSeeds, valueValidator, types, evaluator, cancellationToken);
            return RuntimeTestContextPreparation.Available(prepared.Context, prepared.RootFrame);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimeTestContextUnavailableException exception)
        {
            return RuntimeTestContextPreparation.Unavailable([new(exception.Code, exception.Message,
                RuntimeTestContextFindingKind.Unavailable, exception.ScopeId, exception.MemberKey)]);
        }
        catch (Exception)
        {
            return RuntimeTestContextPreparation.Unavailable([new("runtime.test-context.owner-failed",
                "The owning test context infrastructure could not complete inspection.", RuntimeTestContextFindingKind.InfrastructureFailure)]);
        }
    }
}
