using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime.Constructors;

/// <summary>Stable <c>elsa.graph-activity/1</c> Runtime constructor.</summary>
public sealed class GraphActivityConstructor : IActivityConstructor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> Schemas =
        new HashSet<string>(StringComparer.Ordinal) { RuntimeActivityDescriptor.InitialSchemaVersion };

    public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;
    public IReadOnlySet<string> SupportedSchemaVersions => Schemas;

    public ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!StringComparer.Ordinal.Equals(descriptor.ConsumerKey, ConsumerKey) ||
            !Schemas.Contains(descriptor.SchemaVersion))
        {
            throw new InvalidOperationException(
                $"Graph constructor cannot activate consumer/schema '{descriptor.ConsumerKey}/{descriptor.SchemaVersion}'.");
        }

        GraphActivityDescriptor payload;
        try
        {
            payload = descriptor.Payload.Deserialize<GraphActivityDescriptor>(SerializerOptions)
                      ?? throw new InvalidOperationException("Graph activity descriptor payload resolved to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Graph activity descriptor payload is invalid for schema 1.", exception);
        }

        var activity = new GraphActivity(payload, inputs, outputs);
        foreach (var (name, input) in inputs)
            activity.SyntheticProperties[name] = input;
        foreach (var (name, output) in outputs)
            activity.SyntheticProperties[name] = output;
        return ValueTask.FromResult<IActivity>(activity);
    }
}
