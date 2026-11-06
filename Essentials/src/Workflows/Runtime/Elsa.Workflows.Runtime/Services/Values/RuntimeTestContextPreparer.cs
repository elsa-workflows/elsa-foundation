using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Read-only access to the same initialization path queued start validates again.</summary>
internal sealed class RuntimeTestContextPreparer(IEnumerable<IWorkflowTestEntryContextResolver> entryResolvers,
    IEnumerable<IRuntimeTestContextSeedSource> sources, TimeProvider timeProvider,
    IWorkflowExecutableInputValidator? valueValidator = null, IWellKnownTypeRegistry? types = null,
    IInlineEventPublisher? publisher = null, IServiceProvider? services = null) : IRuntimeTestContextPreparer
{
    public async ValueTask<RuntimeTestContextPreparation> PrepareAsync(WorkflowTestEntry entry,
        WorkflowExecutable executable, string workflowExecutionId, IReadOnlyDictionary<string, JsonElement> workflowInputs,
        IReadOnlyDictionary<string, JsonElement> rootVariableSeeds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(workflowInputs);
        ArgumentNullException.ThrowIfNull(rootVariableSeeds);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (services is null)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.provider-unavailable", "The physical test execution providers are unqualified.");
            try { await new RuntimeTestExecutionBoundary(services).RequireAsync(entry, cancellationToken); }
            catch (WorkflowExecutableStartRejectedException)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.provider-unavailable", "The physical test execution providers are unqualified.");
            }
            var resolvers = entryResolvers.ToArray();
            if (resolvers.Length != 1)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.entry-source-unavailable",
                    "Exactly one authorized entry context resolver is required.");
            var resolved = await resolvers[0].FindAsync(entry.ContextId, cancellationToken);
            if (!entry.Matches(resolved))
                throw new RuntimeTestContextUnavailableException("runtime.test-context.entry-changed",
                    "The selected context is unavailable or differs from its authorized descriptor.");
            if (entry.Scope.IsExpired(timeProvider.GetUtcNow()))
                throw new RuntimeTestContextUnavailableException("runtime.test-context.expired", "The finite test context has expired.");
            VariableFrameState root;
            try { root = RuntimeRootVariableFrameFactory.Create(workflowExecutionId, rootVariableSeeds, executable); }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.root-unavailable",
                    "The pinned workflow root declarations require an unavailable initialization profile.", innerException: exception);
            }
            var prepared = await RuntimeTestContextOwner.CreateAsync(entry, executable, workflowExecutionId, root,
                sources, publisher, workflowInputs, rootVariableSeeds, valueValidator, types, cancellationToken);
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
                "The owning test context infrastructure could not complete preparation.", RuntimeTestContextFindingKind.InfrastructureFailure)]);
        }
    }
}
