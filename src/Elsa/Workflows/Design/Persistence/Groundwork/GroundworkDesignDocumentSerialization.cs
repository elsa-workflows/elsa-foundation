using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Builds the JSON serialization settings for the <b>rich</b> workflow-design Groundwork documents
/// (those carrying authored <see cref="WorkflowDefinitionState"/>), implementing the domain-projection
/// model recorded in the design-provider implementation plan:
/// <list type="bullet">
/// <item>The logical <see cref="WorkflowDefinitionState"/> is serialized as first-class JSON via Elsa's
/// <see cref="IPayloadSerializer"/> — the same serializer the EF Core saving/loading handlers use — so the
/// polymorphic <c>ActivityNode</c> graph and expression payloads round-trip identically on documents.</item>
/// <item>The EF shadow <c>StateSource</c> string is excluded; the logical <c>State</c> is the source of truth
/// in a document.</item>
/// <item>Navigation properties (<c>Definition</c> / <c>WorkflowDefinition</c>) are excluded; related
/// aggregates are fetched via an explicit second read (the <c>GetWithDefinition*</c> port methods).</item>
/// <item>The relational identity column <c>RowNumber</c> is excluded; documents are keyed by string id.</item>
/// </list>
/// This keeps the core entities free of persistence attributes — exclusion is driven by a
/// <see cref="DefaultJsonTypeInfoResolver"/> modifier rather than <c>[JsonIgnore]</c> on the domain types.
/// </summary>
public static class GroundworkDesignDocumentSerialization
{
    // Members that are relational-storage artifacts or cross-aggregate navigation. Matched case-insensitively
    // so the camelCase web naming (e.g. "stateSource") lines up with these PascalCase member names.
    private static readonly HashSet<string> ExcludedMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Entity.RowNumber),
        "StateSource",
        "Definition",
        "WorkflowDefinition",
    };

    /// <summary>
    /// Creates options for the rich design entities, delegating <see cref="WorkflowDefinitionState"/> to
    /// <paramref name="payloadSerializer"/> and excluding the persistence-artifact members above.
    /// </summary>
    public static JsonSerializerOptions Create(IPayloadSerializer payloadSerializer)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ExcludePersistenceArtifacts }
            }
        };

        options.Converters.Add(new PayloadDelegatingConverter<WorkflowDefinitionState>(payloadSerializer));
        return options;
    }

    private static void ExcludePersistenceArtifacts(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (!ExcludedMembers.Contains(property.Name))
                continue;

            // Suppress writing the member, but keep it in the contract so the entity's parameterized
            // constructor can still bind (e.g. the WorkflowDefinitionVersion 'stateSource' parameter). On read
            // the member is simply absent from the document and falls back to its default.
            property.ShouldSerialize = static (_, _) => false;
        }
    }

    // Routes a member's JSON through Elsa's IPayloadSerializer so polymorphic authored content (ActivityNode,
    // expressions) is (de)serialized with the same type-discriminator metadata the relational provider uses.
    private sealed class PayloadDelegatingConverter<T>(IPayloadSerializer payloadSerializer) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return payloadSerializer.Deserialize<T>(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            payloadSerializer.SerializeToElement(value).WriteTo(writer);
        }
    }
}
