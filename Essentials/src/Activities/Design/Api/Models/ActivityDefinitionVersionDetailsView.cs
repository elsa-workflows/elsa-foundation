using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionVersionDetailsView(
    string Id,
    string Version,
    string ProviderKey,
    string ProviderSchemaVersion,
    string ConsumerKey,
    string ConsumerSchemaVersion,
    JsonElement DescriptorPayload,
    ActivityDefinitionView Definition,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityDesignFacet>? DesignFacets,
    ActivityExecutionType ExecutionType,
    // Source provenance persisted on the version row, plus the best-effort providing shell feature id
    // for CLR-backed versions (see ActivityAuthoringProvenanceView). Trailing optional keeps the
    // positional construction surface additive.
    ActivityAuthoringProvenanceView? Provenance = null
);
