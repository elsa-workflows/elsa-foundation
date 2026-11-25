using Elsa3.Mapping.Mappings;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>One Elsa 3 definition to convert, with the reusable-reference replacements of a collection plan.</summary>
public sealed record Elsa3ConversionRequest
{
    public Elsa3ConversionRequest(
        Elsa3WorkflowDefinition definition,
        bool isReusable = false,
        IReadOnlyDictionary<string, Elsa3ActivityExactReplacement>? replacements = null,
        IReadOnlyDictionary<string, string>? diagnosticMetadata = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? dispatchTargetInputs = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        IsReusable = isReusable;
        Replacements = replacements ?? new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal);
        DiagnosticMetadata = diagnosticMetadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
        DispatchTargetInputs = dispatchTargetInputs ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }

    /// <summary>The Elsa 3 definition. The converter clones it and never mutates it.</summary>
    public Elsa3WorkflowDefinition Definition { get; }

    /// <summary>Whether the definition is a reusable workflow (workflow-as-activity), which changes SetOutput, Complete and End.</summary>
    public bool IsReusable { get; }

    /// <summary>Nodes the collection plan rewrites to an exact reusable activity version, keyed by node id.</summary>
    public IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> Replacements { get; }

    /// <summary>Metadata copied onto every diagnostic (input kind, source name, plan item, …).</summary>
    public IReadOnlyDictionary<string, string> DiagnosticMetadata { get; }

    /// <summary>
    /// The input names each workflow this import publishes declares, keyed by Elsa 3 definition id: the newest selected
    /// version, which a dispatch to it pins once published (#5808). A dispatch target missing here is not checked here.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DispatchTargetInputs { get; }
}
