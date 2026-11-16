using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Bounded projection of prepared protocol targets. Each target inherits identity and expiry from one existing
/// TestRun scope; implementations must reject a conflicting target for the same test-run id.
/// </summary>
public interface ITestRunTargetStore
{
    ValueTask<TestRunTarget> SaveAsync(
        TestRunTarget target,
        CancellationToken cancellationToken = default);

    ValueTask<TestRunTarget?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default);

    ValueTask<TestRunTargetAdmission> BeginDispatchAsync(
        string testRunId,
        string stimulusDigest,
        CancellationToken cancellationToken = default);

    ValueTask<TestRunTarget?> CompleteDispatchAsync(
        string testRunId,
        TestRunTargetDispatchResult result,
        CancellationToken cancellationToken = default);

    ValueTask<TestRunTarget?> MarkStoppedAsync(
        string testRunId,
        CancellationToken cancellationToken = default);

    ValueTask<TestRunTarget?> RemoveAsync(
        string testRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>Starts a prepared target using only its server-resolved trigger and existing TestRun authority.</summary>
public interface ITestRunTargetDispatcher
{
    ValueTask<TestRunTargetDispatchResult> DispatchAsync(
        string testRunId,
        System.Text.Json.JsonElement? stimulusInput,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a target with the caller's ambient services and metadata for synchronous runtime work.
    /// Existing dispatchers remain compatible and may ignore these optional transport details.
    /// </summary>
    ValueTask<TestRunTargetDispatchResult> DispatchAsync(
        string testRunId,
        System.Text.Json.JsonElement? stimulusInput,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions,
        CancellationToken cancellationToken = default) =>
        DispatchAsync(testRunId, stimulusInput, cancellationToken);
}

/// <summary>Releases the module-owned resources for a prepared target before its existing scope closes.</summary>
public interface ITestRunTargetCleaner
{
    ValueTask StopAsync(string testRunId, CancellationToken cancellationToken = default);
}
