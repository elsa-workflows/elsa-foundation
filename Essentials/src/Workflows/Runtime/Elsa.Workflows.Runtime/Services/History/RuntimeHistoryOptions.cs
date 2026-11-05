using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Explicit production metadata-history composition; changes affect new admissions only.</summary>
public sealed class RuntimeHistoryOptions
{
    public bool Enabled { get; set; } = true;

    internal RuntimeHistoryCapturePolicy? CreatePolicy(IEnumerable<string>? sourceKinds = null) => Enabled
        ? new("runtime-history-v3", 3, true, RuntimeHistoryCapture.Kinds.Concat(RuntimeHistoryCapture.Version2Kinds).Concat(sourceKinds ?? []).ToArray())
        : null;
}
