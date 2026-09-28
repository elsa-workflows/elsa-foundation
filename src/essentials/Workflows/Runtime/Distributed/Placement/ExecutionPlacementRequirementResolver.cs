using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// Resolves an execution's placement requirement before a claim, on both claim paths (spec 184, FR-005 and FR-016).
/// </summary>
/// <remarks>
/// <para>
/// The pin is read from the execution's durable state when the execution exists, and otherwise from a start command
/// for it: the command in hand on the activation path, or a pending one the transport holds on the pump's path. A pin
/// is an immutable, content-addressed executable (ADR 0038), so its check subject is cached per artifact id for the life
/// of the shell; only a successful load is cached, so an artifact that arrives later resolves then.
/// </para>
/// <para>
/// Every failure resolves to an unresolved requirement with its reason, never to "nothing required": an execution whose
/// pin cannot be read or loaded is not runnable here (FR-016).
/// </para>
/// </remarks>
public sealed class ExecutionPlacementRequirementResolver
{
    private readonly ConcurrentDictionary<string, RuntimeRequirementCheckSubject> _subjects = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the requirement of <paramref name="workflowExecutionId"/> in the operation scope
    /// <paramref name="services"/>. When no durable state exists, the pin is read from a start command among
    /// <paramref name="inHand"/>, and then among the commands <paramref name="pending"/> reads, which is only called
    /// then.
    /// </summary>
    public async ValueTask<ExecutionPlacementRequirement> ResolveAsync(
        string workflowExecutionId,
        IServiceProvider services,
        IEnumerable<WorkflowExecutionCommandEnvelope> inHand,
        Func<CancellationToken, ValueTask<IReadOnlyList<WorkflowExecutionCommandEnvelope>>>? pending = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(inHand);

        WorkflowExecutableIdentity? pin;
        try
        {
            var state = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId, cancellationToken);
            pin = state?.PinnedExecutable
                  ?? StartPin(inHand)
                  ?? (pending is null ? null : StartPin(await pending(cancellationToken)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ExecutionPlacementRequirement.Unresolved($"the execution's pinned executable cannot be read ({exception.GetType().Name}: {exception.Message})");
        }

        return pin is null
            ? ExecutionPlacementRequirement.None
            : await ResolvePinAsync(pin, services, cancellationToken);
    }

    private async ValueTask<ExecutionPlacementRequirement> ResolvePinAsync(
        WorkflowExecutableIdentity pin,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (_subjects.TryGetValue(pin.ArtifactId, out var cached))
            return ExecutionPlacementRequirement.Of(cached);

        WorkflowExecutable? executable;
        try
        {
            executable = await services.GetRequiredService<IWorkflowExecutableReader>().FindAsync(pin.ArtifactId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ExecutionPlacementRequirement.Unresolved($"pinned executable '{pin.ArtifactId}' cannot be loaded ({exception.GetType().Name}: {exception.Message})");
        }

        if (executable is null)
            return ExecutionPlacementRequirement.Unresolved($"pinned executable '{pin.ArtifactId}' cannot be loaded: it is not in this member's executable store");

        var subject = _subjects.GetOrAdd(pin.ArtifactId, RuntimeRequirementCheckSubject.FromExecutable(executable));
        return ExecutionPlacementRequirement.Of(subject);
    }

    /// <summary>The pin of the first start command in <paramref name="commands"/>. A start command whose payload cannot
    /// be read is reported rather than skipped, so it resolves to an unresolved requirement.</summary>
    private static WorkflowExecutableIdentity? StartPin(IEnumerable<WorkflowExecutionCommandEnvelope> commands)
    {
        var start = commands.FirstOrDefault(envelope => envelope.Command.Kind == WorkflowExecutionCommandKind.Start);
        if (start is null)
            return null;
        if (start.Command.Payload is not { } payload)
            throw new InvalidOperationException("The start command carries no payload.");

        return (payload.Deserialize<WorkflowExecutionStartCommandPayload>()
                ?? throw new InvalidOperationException("The start command's payload resolved to null.")).PinnedExecutable;
    }
}
