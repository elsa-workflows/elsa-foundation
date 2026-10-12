namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Workflow strategy selections. Extension identities use durable aliases/references, never
/// assembly-qualified CLR type names.
/// </summary>
public sealed class WorkflowStrategyOptions
{
    /// <summary>
    /// The alias of the <see cref="IWorkflowActivationStrategy"/> to apply when new instances are requested to be created.
    /// </summary>
    public string? ActivationStrategyType { get; set; }

    /// <summary>
    /// The exact incident strategy to use when a fault occurs, or <see langword="null"/> to inherit
    /// the publishing host's effective default.
    /// </summary>
    public Elsa.Primitives.Models.IncidentStrategyReference? IncidentStrategy { get; set; }

    /// <summary>
    /// The persistence mode this workflow's runs use, by name (<c>"Durable"</c> or <c>"Balanced"</c>), or
    /// <see langword="null"/> to use the runtime host's default. Kept as a string so the authoring model does not
    /// depend on the runtime; publishing rejects a name it does not know. The mode is compiled into the executable and
    /// wins over the host default.
    /// </summary>
    public string? PersistenceMode { get; set; }
}
