using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Default <see cref="IGroundworkRuntimeDocumentSerializer"/>. Owns the frozen serializer options of
/// the runtime persistence bridge (web defaults emit camelCase property names, so the declared
/// keyword index fields — for example <c>workflowExecutionId</c> — match the serialized JSON the
/// relational/document providers index) and enforces the per-kind schema-version contract declared
/// in <see cref="ElsaRuntimeDocumentVersions"/>.
/// </summary>
/// <remarks>
/// These options are deliberately independent of <c>IPayloadSerializer</c>: this is the durability
/// format of suspended workflow state, frozen by the golden-fixture suite and only changed through
/// explicit version bumps with upcasters. See the sanctioned-exception entry in
/// <c>docs/serialization.md</c>.
/// </remarks>
public sealed class GroundworkRuntimeDocumentSerializer(IGroundworkRuntimeDocumentUpcasterRegistry upcasterRegistry) : IGroundworkRuntimeDocumentSerializer
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropDerivedExecutableProjections }
        }
    };

    public (string SchemaVersion, string ContentJson) Serialize<T>(string documentKind, T document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var currentVersion = ElsaRuntimeDocumentVersions.CurrentFor(documentKind);
        var contentJson = JsonSerializer.Serialize(document, Options);
        return (ElsaRuntimeDocumentVersions.Stamp(currentVersion), contentJson);
    }

    public string SerializeForComparison<T>(T value) => JsonSerializer.Serialize(value, Options);

    public bool IsCurrentVersion(DocumentEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var foundVersion = ElsaRuntimeDocumentVersions.Parse(envelope.DocumentKind, envelope.SchemaVersion);
        return foundVersion == ElsaRuntimeDocumentVersions.CurrentFor(envelope.DocumentKind);
    }

    public T DeserializeElement<T>(JsonElement element) =>
        element.Deserialize<T>(Options)
        ?? throw new GroundworkRuntimeDocumentVersionException("A document fragment deserialized to null content.");

    public T Deserialize<T>(DocumentEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var documentKind = envelope.DocumentKind;
        var foundVersion = ElsaRuntimeDocumentVersions.Parse(documentKind, envelope.SchemaVersion);
        var currentVersion = ElsaRuntimeDocumentVersions.CurrentFor(documentKind);

        if (foundVersion > currentVersion)
        {
            throw new GroundworkRuntimeDocumentVersionException(
                $"Document '{envelope.Id}' of kind '{documentKind}' carries schema version {foundVersion}, but this build only supports " +
                $"versions up to {currentVersion}. It was written by a newer version of Elsa; refusing to deserialize.");
        }

        if (foundVersion == currentVersion)
            return DeserializeContent<T>(documentKind, envelope.Id, envelope.ContentJson);

        var content = ParseContent(documentKind, envelope.Id, envelope.ContentJson);
        var upcasted = upcasterRegistry.Upcast(documentKind, foundVersion, currentVersion, content);
        return upcasted.Deserialize<T>(Options)
               ?? throw new GroundworkRuntimeDocumentVersionException(
                   $"Upcasting document '{envelope.Id}' of kind '{documentKind}' from version {foundVersion} to {currentVersion} produced null content.");
    }

    private static T DeserializeContent<T>(string documentKind, string id, string contentJson)
    {
        return JsonSerializer.Deserialize<T>(contentJson, Options)
               ?? throw new GroundworkRuntimeDocumentVersionException(
                   $"Document '{id}' of kind '{documentKind}' deserialized to null content.");
    }

    private static JsonObject ParseContent(string documentKind, string id, string contentJson)
    {
        return JsonNode.Parse(contentJson) as JsonObject
               ?? throw new GroundworkRuntimeDocumentVersionException(
                   $"Document '{id}' of kind '{documentKind}' does not contain a JSON object; it cannot be upcasted.");
    }

    // WorkflowExecutable recomputes Nodes and NodesById from RootActivity in its constructor, so both
    // are derived projections of the same tree. Persisting them would store the executable graph three
    // times; dropping them keeps the stored document compact and the constructor rebuilds them on load.
    private static void DropDerivedExecutableProjections(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(WorkflowExecutable))
            return;

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var memberName = (typeInfo.Properties[i].AttributeProvider as MemberInfo)?.Name;
            if (memberName is nameof(WorkflowExecutable.Nodes) or nameof(WorkflowExecutable.NodesById))
                typeInfo.Properties.RemoveAt(i);
        }
    }
}
