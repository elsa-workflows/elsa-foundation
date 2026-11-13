using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Checks an admitted history responsibility before callback/activation effects, even after module removal.</summary>
internal static class ActivityHistoryPreflight
{
    internal static async ValueTask<RuntimeHistoryCapturePolicy?> ReadPolicyAndValidateAsync(IServiceProvider services,
        string workflowId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var nativeStore = services.GetService<IWorkflowExecutionStateStore>();
            var native = nativeStore is null ? null : await nativeStore.FindAsync(workflowId, cancellationToken);
            if (native?.HistoryCapturePolicy is not { } policy) return null;
            // Short-circuit before any diagnostic reader for legacy runs or a missing source/capture module.
            if (services.GetService<IRuntimeHistorySourceExtension>() is null || !RuntimeHistorySourceFacts.IsFinalProfile(policy) ||
                !services.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<IRuntimeHistoryCaptureSupport>().Any(capture => capture.CanCapture(policy)) ||
                services.GetService<IRuntimeHistoryStore>() is not { } reader ||
                services.GetRequiredService<IRuntimeCheckpointCommitStore>() is not { SupportsHistory: true } writer ||
                !writer.CanCommitHistory(reader))
            {
                if (policy.Required)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                        "The callback cannot acknowledge its admitted history profile.");
                return null;
            }
            return policy;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw new RuntimeHistoryIntegrityException(workflowId, "the callback could not interpret its admitted history policy", exception);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsUnavailable(exception))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                "Callback history source metadata is unavailable.", exception);
        }
    }

    private static bool IsMalformed(Exception exception) =>
        exception is InvalidDataException or JsonException or RuntimeHistoryIntegrityException ||
        exception.InnerException is { } cause && IsMalformed(cause);

    private static bool IsUnavailable(Exception exception)
    {
        if (IsMalformed(exception) || RuntimeCheckpointCommitValidationException.IsCauseOf(exception)) return false;
        var unavailable = false;
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        {
            if (cause is ArgumentException or RuntimeSchedulerWorkUnavailableException or RuntimeCheckpointInconsistentDurabilityException) return false;
            unavailable |= cause is IOException or DbException or TimeoutException or NotSupportedException or OperationCanceledException or RuntimeHistoryUnavailableException;
        }
        return unavailable;
    }
}
