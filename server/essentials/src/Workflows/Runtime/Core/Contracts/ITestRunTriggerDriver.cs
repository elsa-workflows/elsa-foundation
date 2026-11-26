using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Module contribution for preparing one selected start trigger in a workflow test run. Drivers own the
/// protocol-specific configuration and firing action; the workflow runtime owns trigger identity, TestRun
/// authority, and the final start dispatch.
/// </summary>
public interface ITestRunTriggerDriver
{
    /// <summary>A stable, non-secret identity for this driver.</summary>
    string DriverId { get; }

    /// <summary>Returns whether this driver can prepare the exact eligible binding.</summary>
    bool Supports(TestRunTriggerContext context);

    /// <summary>Describes the modes and safe read-only configuration for this binding.</summary>
    IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunTriggerContext context);

    /// <summary>Validates module-owned data before the TestRun scope or protocol resource is created.</summary>
    TestRunTriggerValidation Validate(
        TestRunTriggerContext context,
        string modeId,
        JsonElement? data);

    /// <summary>
    /// Prepares the selected mode. The result either contains an immediate logical stimulus or an external action
    /// that will later call <see cref="ITestRunTargetDispatcher"/> with the exact test-run id.
    /// </summary>
    ValueTask<TestRunTriggerPreparation> PrepareAsync(
        TestRunTriggerPrepareContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Releases resources owned by this driver when the existing TestRun scope stops or expires.</summary>
    ValueTask StopAsync(
        TestRunTriggerStopContext context,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
