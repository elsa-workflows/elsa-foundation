using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Resolves the persistence mode to report on instance read models. Prefers the mode the run recorded at its first
/// commit (<see cref="RuntimeMetadataKeys.PersistenceMode"/>), so an instance reports the mode it actually ran under even
/// after the host default is reconfigured. A run recorded without a mode, one faulted into existence by a start that
/// carried none, reports the host default from the registered <see cref="PersistenceModeOptions"/>. A host without them
/// cannot run any mode, so it has no default to report.
/// </summary>
public sealed class RuntimePersistenceModeInspector(IEnumerable<PersistenceModeOptions> hostOptions)
{
    private readonly PersistenceModeOptions? _options = hostOptions.SingleOrDefault();

    /// <summary>Projects the host's default mode and the inspection granularity it implies.</summary>
    public RuntimePersistenceModeProjection Resolve() =>
        RuntimePersistenceModeProjection.For(_options?.Mode ?? throw new InvalidOperationException(
            "This host has no persistence-mode options, so it has no default mode to report. " +
            "Call AddPersistenceModes() after the workflows runtime and its persistence provider are registered."));

    /// <summary>
    /// Projects the mode a specific run executed under, preferring its recorded mode and falling back to the host
    /// default when it recorded none.
    /// </summary>
    public RuntimePersistenceModeProjection Resolve(WorkflowExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var recorded) &&
               Enum.TryParse<PersistenceMode>(recorded, ignoreCase: false, out var mode) &&
               Enum.IsDefined(mode)
            ? RuntimePersistenceModeProjection.For(mode)
            : Resolve();
    }
}
