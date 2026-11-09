using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Module contribution for deliberate logical stimulus delivery to a supported active test wait.</summary>
public interface ITestRunWaitDriver
{
    string DriverId { get; }
    bool Supports(TestRunWaitContext context);
    IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunWaitContext context);
    TestRunTriggerValidation Validate(TestRunWaitContext context, string modeId, JsonElement? data);
    ValueTask<JsonElement?> PrepareAsync(TestRunWaitPrepareContext context, CancellationToken cancellationToken = default);
}
