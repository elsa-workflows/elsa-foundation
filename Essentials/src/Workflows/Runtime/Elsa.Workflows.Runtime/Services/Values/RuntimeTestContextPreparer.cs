using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Trusted read-only preparation; never creates an execution or a consumable start grant.</summary>
internal sealed class RuntimeTestContextPreparer(WorkflowTestEntryAdmission admission,
    IEnumerable<IRuntimeTestContextSeedSource> sources,
    IWorkflowExecutableInputValidator? valueValidator = null, IWellKnownTypeRegistry? types = null,
    IInlineEventPublisher? publisher = null, IPortableExpressionEvaluator? evaluator = null) : IRuntimeTestContextPreparer
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
            var inputs = workflowInputs.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
            var variables = rootVariableSeeds.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
            await ValidateAsync();
            var owners = sources.ToArray();
            if (owners.Length != 1)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.seed-source-unavailable",
                    "Exactly one authorized context seed source is required.");
            var resolved = await owners[0].ResolveAsync(entry, executable, workflowExecutionId, cancellationToken);
            if (resolved is null || resolved.ContextId != entry.ContextId || resolved.ContextFingerprint != entry.ContextFingerprint)
                throw new RuntimeTestContextUnavailableException("runtime.test-context.seed-changed",
                    "The exact authorized context seed is unavailable or has changed.");
            var seed = resolved with
            {
                Frames = Array.AsReadOnly(resolved.Frames.Select(x => x with
                {
                    Values = Snapshot(x.Values),
                    Inputs = x.Inputs is null ? null : Snapshot(x.Inputs),
                    VariableNames = x.VariableNames is null ? null : new ReadOnlyDictionary<string, string>(
                        x.VariableNames.ToDictionary(y => y.Key, y => y.Value, StringComparer.Ordinal))
                }).ToArray()),
                Results = Array.AsReadOnly(resolved.Results.ToArray()),
                RootVariables = resolved.RootVariables is null ? null : Snapshot(resolved.RootVariables)
            };
            VariableFrameState root;
            try { root = RuntimeRootVariableFrameFactory.Create(workflowExecutionId, variables, executable); }
            catch (InvalidOperationException exception)
            {
                throw new RuntimeTestContextUnavailableException("runtime.test-context.root-unavailable",
                    "The pinned workflow root declarations require an unavailable initialization profile.", innerException: exception);
            }
            // Never delegate admission to the replaceable, untrusted read-only inspector.
            var prepared = await RuntimeTestContextMaterializer.CreateFromSeedAsync(executable, entry.ExecutableNodeId,
                workflowExecutionId, root, seed, publisher, inputs, variables, valueValidator, types, evaluator, cancellationToken);
            await ValidateAsync();
            return RuntimeTestContextPreparation.Available(prepared.Context, prepared.RootFrame);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkflowExecutableStartRejectedException)
        {
            return Unavailable("runtime.test-context.admission-unavailable", "The exact selected entry or physical execution providers are unavailable.");
        }
        catch (TestScopeAdmissionException)
        {
            return Unavailable("runtime.test-context.scope-unavailable", "The finite test scope is not open in the current persistence context.");
        }
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

        ValueTask ValidateAsync() => admission.ValidateAsync(entry, executable, WorkflowRunKind.TestRun, entry.Scope,
            entry.Authority, entry.Scope.TenantId, entry.Scope.Partition, null, null, null, 0, false, cancellationToken);
    }

    private static IReadOnlyDictionary<string, ValueEnvelope> Snapshot(IReadOnlyDictionary<string, ValueEnvelope> values) =>
        new ReadOnlyDictionary<string, ValueEnvelope>(values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static RuntimeTestContextPreparation Unavailable(string code, string message) =>
        RuntimeTestContextPreparation.Unavailable([new(code, message, RuntimeTestContextFindingKind.Unavailable)]);
}
