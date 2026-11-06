using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Graph.Design.Services;

/// <summary>Projects only an authorized graph's authored body and public names for the shared scope picker.</summary>
internal sealed class ActivityGraphExpressionProjection : IActivityDraftExpressionGraphProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ProviderKey => "elsa.activity-graph";

    public WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState state)
    {
        if (state.Provider.ProviderKey != ProviderKey || state.Provider.SchemaVersion is not ("1" or "2") ||
            !ActivityGraphManifest.TryParse(state.Provider.SchemaVersion, state.Provider.Payload, out var graph, out _))
            return null;
        try
        {
            var root = graph!.RootActivity.Deserialize<ActivityNode>(JsonOptions);
            if (root is null)
                return null;
            return new(
                graph.Variables.Select(variable => new VariableDefinition(variable.ReferenceKey, variable.Name, variable.Type, variable.StorageDriverKey, null)).ToArray(),
                root,
                state.Contract.Inputs.Select(input => new InputDefinition(input.ReferenceKey, input.Name, input.Type, input.StorageDriverKey,
                    input.DisplayName ?? input.Name, input.Category, input.IsNullable)).ToArray(),
                [],
                null);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
