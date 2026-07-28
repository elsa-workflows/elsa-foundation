using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Serialization;
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
/// explicit version policy. Workflow executables admit only their current fixture; executable activity
/// templates and post-commit outbox documents retain explicit compatible upgrade steps. Future released
/// shapes may add Groundwork upcasters and retained fixtures under the same policy. See the
/// sanctioned-exception entry in <c>docs/serialization.md</c>.
/// </remarks>
public sealed class GroundworkRuntimeDocumentSerializer : IGroundworkRuntimeDocumentSerializer
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { ConfigureDurableDocumentShape }
        }
    };

    private readonly VersionedJsonDocumentCodec _codec;

    public GroundworkRuntimeDocumentSerializer()
    {
        _codec = new VersionedJsonDocumentCodec(
            ElsaRuntimeDocumentVersions.All.Select(pair => new DocumentSchemaVersionPolicy(
                pair.Key,
                ElsaRuntimeDocumentVersions.MinimumReadableFor(pair.Key),
                pair.Value)),
            [
                new ExecutableActivityTemplateDocumentV1ToV2Upcaster(),
                new WorkflowExecutableSourceReferenceDocumentV4ToV5Upcaster(),
                new PostCommitOutboxDocumentV3ToV4Upcaster()
            ],
            new DocumentSchemaVersionFormat(
                (documentKind, schemaVersion) => ElsaRuntimeDocumentVersions.Parse(documentKind, schemaVersion),
                (_, version) => ElsaRuntimeDocumentVersions.Stamp(version)),
            Options);
    }

    public (string SchemaVersion, string ContentJson) Serialize<T>(string documentKind, T document)
    {
        var serialized = _codec.Serialize(documentKind, document);
        return (serialized.SchemaVersion, serialized.ContentJson);
    }

    public string SerializeForComparison<T>(T value) => JsonSerializer.Serialize(value, Options);

    public bool IsCurrentVersion(DocumentEnvelope envelope)
    {
        return _codec.IsCurrentVersion(envelope);
    }

    public T DeserializeElement<T>(JsonElement element) =>
        element.Deserialize<T>(Options)
        ?? throw new InvalidOperationException("A document fragment deserialized to null content.");

    public T Deserialize<T>(DocumentEnvelope envelope) => _codec.Deserialize<T>(envelope);

    // WorkflowExecutable and ExecutableActivityTemplate recompute their node indexes from the root in
    // their constructors. Persisting those projections would duplicate the graph; the constructors
    // rebuild them on load.
    private static void ConfigureDurableDocumentShape(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(BookmarkState))
        {
            AddStimulusLookupKeys<BookmarkState>(
                typeInfo,
                ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField,
                bookmark => bookmark.StimulusType,
                bookmark => bookmark.StimulusHash);
            return;
        }

        if (typeInfo.Type == typeof(WorkflowTriggerBinding))
        {
            AddStimulusLookupKeys<WorkflowTriggerBinding>(
                typeInfo,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusLookupKeyField,
                ElsaRuntimeStorageManifest.WorkflowTriggerBindingStimulusTypeLookupKeyField,
                binding => binding.StimulusType,
                binding => binding.StimulusHash);
            return;
        }

        if (typeInfo.Type == typeof(ExecutableActivityTemplate))
        {
            RemoveProperties(typeInfo, nameof(ExecutableActivityTemplate.NodesById));
            return;
        }

        if (typeInfo.Type == typeof(WorkflowExecutable))
            RemoveProperties(typeInfo, nameof(WorkflowExecutable.Nodes), nameof(WorkflowExecutable.NodesById));
    }

    private static void AddStimulusLookupKeys<T>(
        JsonTypeInfo typeInfo,
        string stimulusLookupField,
        string stimulusTypeLookupField,
        Func<T, string> getStimulusType,
        Func<T, string> getStimulusHash)
    {
        var stimulus = typeInfo.CreateJsonPropertyInfo(
            typeof(string),
            stimulusLookupField);
        stimulus.Get = value =>
        {
            var document = (T)value;
            return StimulusLookupKey.FromPair(
                getStimulusType(document),
                getStimulusHash(document));
        };
        typeInfo.Properties.Add(stimulus);

        var stimulusType = typeInfo.CreateJsonPropertyInfo(
            typeof(string),
            stimulusTypeLookupField);
        stimulusType.Get = value =>
            StimulusLookupKey.FromType(getStimulusType((T)value));
        typeInfo.Properties.Add(stimulusType);
    }

    private static void RemoveProperties(JsonTypeInfo typeInfo, params string[] propertyNames)
    {
        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var memberName = (typeInfo.Properties[i].AttributeProvider as MemberInfo)?.Name;
            if (memberName is not null && propertyNames.Contains(memberName, StringComparer.Ordinal))
                typeInfo.Properties.RemoveAt(i);
        }
    }
}
