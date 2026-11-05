using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Explicit production metadata-history composition; changes affect new admissions only.</summary>
public sealed class RuntimeHistoryOptions
{
    public bool Enabled { get; set; } = true;

    internal RuntimeHistoryCapturePolicy? CreatePolicy() => Enabled
        ? new("runtime-history-v1", 1, true, RuntimeHistoryCapture.Kinds)
        : null;
}
