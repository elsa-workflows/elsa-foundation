using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;

namespace Elsa3.Mapping.Tests;

internal sealed class Elsa3MemoryReferenceImportFixture
{
    private readonly Elsa3WorkflowDefinitionImporter _importer;

    public Elsa3MemoryReferenceImportFixture(params ActivityShape[] shapes)
    {
        var typeRegistry = new TestWellKnownTypeRegistry();
        var activityLookup = new TestActivityDefinitionLookup(shapes);
        var activityMapper = new Elsa3ActivityToState(activityLookup);
        var stateMapper = new Elsa3WorkflowDefinitionToState(
            typeRegistry,
            activityMapper,
            new Elsa3ArgumentDefinitionToInputOutput(typeRegistry));
        var versionMapper = new Elsa3WorkflowDefinitionToWorkflowDefinitionVersion(
            stateMapper,
            new TestWorkflowDefinitionFactory(),
            new TestWorkflowDefinitionVersionFactory());
        var referenceGraph = new Elsa3MemoryReferenceGraph(activityLookup);
        var valueFlowLowerer = new Elsa3ValueFlowLowerer(new Elsa3ExpressionRewriter());
        _importer = new Elsa3WorkflowDefinitionImporter(versionMapper, referenceGraph, valueFlowLowerer);
    }

    public ValueTask<Elsa3MigrationResult<IWorkflowDefinitionVersion>> ImportAsync(
        Elsa3WorkflowDefinition definition,
        string sourceName) =>
        _importer.ImportAsync(
            new Elsa3WorkflowDefinitionImportInput(
                Elsa3MigrationInputKind.WorkflowDefinitionExportJson,
                definition,
                sourceName));

    public Elsa3MigrationResult<IWorkflowDefinitionVersion> RejectWorkflowInstanceState(string? sourceName = null) =>
        _importer.RejectWorkflowInstanceState(sourceName);

    public static Elsa3WorkflowDefinition Workflow(
        string id,
        Elsa3Activity root,
        IEnumerable<Elsa3Variable>? variables = null,
        IEnumerable<Elsa3WorkflowArgumentDefinition>? inputs = null) =>
        new()
        {
            Id = $"{id}-v1",
            DefinitionId = id,
            Name = id,
            Description = $"Import fixture for {id}.",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Version = 1,
            Variables = (variables ?? []).ToList(),
            Inputs = (inputs ?? []).ToList(),
            Root = root,
        };

    public static Elsa3Activity Activity(
        string nodeId,
        string type,
        IReadOnlyDictionary<string, JsonElement>? properties = null,
        IEnumerable<Elsa3Activity>? children = null) =>
        new()
        {
            Id = nodeId,
            NodeId = nodeId,
            Name = nodeId,
            Type = type,
            Version = 1,
            AdditionalProperties = properties?.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, JsonElement>(),
            Activities = children?.ToList(),
        };

    public static JsonElement Argument(string expressionType, string expressionValue, string? memoryReferenceId = null) =>
        memoryReferenceId is null
            ? JsonSerializer.SerializeToElement(new
            {
                typeName = "String",
                expression = new { type = expressionType, value = expressionValue },
            })
            : JsonSerializer.SerializeToElement(new
            {
                typeName = "String",
                expression = new { type = expressionType, value = expressionValue },
                memoryReference = new { id = memoryReferenceId },
            });

    public static JsonElement MemoryReference(string id) =>
        JsonSerializer.SerializeToElement(new
        {
            typeName = "String",
            memoryReference = new { id },
        });

    public static JsonElement FindNode(IWorkflowDefinitionVersion version, string nodeId)
    {
        var state = JsonSerializer.SerializeToElement(version.State);
        return FindObject(state, "NodeId", nodeId)
            ?? throw new Xunit.Sdk.XunitException($"Imported state does not contain activity node '{nodeId}'.");
    }

    public static JsonElement FindInput(IWorkflowDefinitionVersion version, string nodeId, string inputKey)
    {
        var node = FindNode(version, nodeId);
        var inputs = GetProperty(node, "Inputs");
        foreach (var input in inputs.EnumerateArray())
        {
            if (StringComparer.Ordinal.Equals(GetProperty(input, "ReferenceKey").GetString(), inputKey))
                return input;
        }

        throw new Xunit.Sdk.XunitException($"Imported node '{nodeId}' does not contain input '{inputKey}'.");
    }

    public static ArgumentValueView ReadValue(JsonElement argument)
    {
        var value = GetProperty(argument, "Value");
        return new ArgumentValueView(
            GetProperty(value, "ExpressionType").ValueKind == JsonValueKind.Null
                ? null
                : GetProperty(value, "ExpressionType").GetString(),
            GetProperty(value, "Value"));
    }

    public static ExpressionDefinition ReadPortableExpression(ArgumentValueView value)
    {
        if (value.Value.ValueKind != JsonValueKind.Object)
            throw new Xunit.Sdk.XunitException("The imported expression is not a portable ExpressionDefinition object.");

        return value.Value.Deserialize<ExpressionDefinition>()
            ?? throw new Xunit.Sdk.XunitException("The imported portable expression could not be deserialized.");
    }

    public static string SerializeState(IWorkflowDefinitionVersion version) =>
        JsonSerializer.Serialize(version.State);

    public static JsonElement GetProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new Xunit.Sdk.XunitException($"JSON object does not contain property '{name}'.");
    }

    private static JsonElement? FindObject(JsonElement element, string propertyName, string propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    StringComparer.Ordinal.Equals(property.Value.GetString(), propertyValue))
                    return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindObject(property.Value, propertyName, propertyValue);
                if (found is not null)
                    return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindObject(item, propertyName, propertyValue);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    internal sealed record ActivityShape(
        string Type,
        IReadOnlyList<string> Inputs,
        IReadOnlyList<string> Outputs,
        string InputType,
        string OutputType)
    {
        public static ActivityShape Create(
            string type,
            string[]? inputs = null,
            string[]? outputs = null,
            string inputType = "String",
            string outputType = "String") =>
            new(type, inputs ?? [], outputs ?? [], inputType, outputType);
    }

    internal readonly record struct ArgumentValueView(string? ExpressionType, JsonElement Value);

    private sealed class TestActivityDefinitionLookup : IActivityDefinitionLookup
    {
        private readonly IReadOnlyDictionary<string, TestActivityDefinitionVersion> _versionsByType;
        private readonly IReadOnlyDictionary<string, TestActivityDefinitionVersion> _versionsById;

        public TestActivityDefinitionLookup(IEnumerable<ActivityShape> shapes)
        {
            _versionsByType = shapes
                .DistinctBy(x => x.Type, StringComparer.Ordinal)
                .Select(shape => new TestActivityDefinitionVersion(shape))
                .ToDictionary(x => x.Definition.ActivityTypeKey, StringComparer.Ordinal);
            _versionsById = _versionsByType.Values.ToDictionary(x => x.Id, StringComparer.Ordinal);
        }

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinition>(_versionsByType[idOrActivityTypeKey].Definition);

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(
            string? id = null,
            string? category = null,
            string? searchTerm = null,
            string? displayName = null,
            string? description = null,
            bool? tenantAgnostic = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<IActivityDefinition>>(_versionsByType.Values.Select(x => x.Definition));

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion>(_versionsById[versionId]);

        public Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IActivityDefinitionVersion?>(_versionsById.GetValueOrDefault(versionId));

        public Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(
            string definitionId,
            CancellationToken cancellationToken = default)
        {
            var version = _versionsByType.Values.Single(x => x.Definition.Id == definitionId);
            return Task.FromResult<IEnumerable<ActivityDefinitionVersionSummary>>(
            [
                new(version.Id, version.Version, DateTimeOffset.UnixEpoch, version.ExecutionType),
            ]);
        }
    }

    private sealed class TestActivityDefinitionVersion : IActivityDefinitionVersion
    {
        public TestActivityDefinitionVersion(ActivityShape shape)
        {
            Definition = new TestActivityDefinition(shape.Type);
            Id = $"catalog:{shape.Type}@1";
            Inputs = shape.Inputs.Select(name => new InputDefinition(
                StableKey(name), name, new TypeReference(shape.InputType), null, name, null, false)).ToArray();
            Outputs = shape.Outputs.Select(name => new OutputDefinition(
                StableKey(name), name, new TypeReference(shape.OutputType), null, name, null, false)).ToArray();
        }

        public string Id { get; }
        public string Version => "1.0.0";
        public string DefinitionId => Definition.Id;
        public string ProviderKey => "test.provider";
        public string ProviderSchemaVersion => "1";
        public string ConsumerKey => "test.consumer";
        public string ConsumerSchemaVersion => "1";
        public string DescriptorType => "test";
        public JsonElement DescriptorPayload => JsonSerializer.SerializeToElement(new { });
        public string SourceKind => "test";
        public string SourceId => Definition.ActivityTypeKey;
        public IActivityDefinition Definition { get; }
        public IEnumerable<InputDefinition> Inputs { get; }
        public IEnumerable<OutputDefinition> Outputs { get; }
        public IEnumerable<ActivityDesignFacet> DesignFacets => [];
        public ActivityExecutionType ExecutionType => ActivityExecutionType.Action;
        public string? Hash => null;

        private static string StableKey(string name) => $"key:{ToKebabCase(name)}";

        private static string ToKebabCase(string name) => string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
    }

    private sealed class TestActivityDefinition(string activityTypeKey) : IActivityDefinition
    {
        public string Id => $"definition:{activityTypeKey}";
        public string ActivityTypeKey => activityTypeKey;
        public string Category => "Test";
        public string? DisplayName => activityTypeKey;
        public string? Description => null;
    }

    private sealed class TestWorkflowDefinitionFactory : IWorkflowDefinitionFactory
    {
        public IWorkflowDefinition Create(string name, string? description = null, string? id = null, bool deleted = false) =>
            new WorkflowDefinitionModel(
                id ?? $"definition:{name}",
                name,
                description,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                deleted ? DateTimeOffset.UnixEpoch : null);
    }

    private sealed class TestWorkflowDefinitionVersionFactory : IWorkflowDefinitionVersionFactory
    {
        public IWorkflowDefinitionVersion Create(
            IWorkflowDefinition definition,
            string version,
            WorkflowDefinitionState state,
            DateTimeOffset? sourceCreatedAt = null,
            string? id = null) =>
            new WorkflowDefinitionVersionModel(
                id ?? $"{definition.Id}:{version}",
                version,
                definition.Id,
                definition,
                state,
                sourceCreatedAt,
                DateTimeOffset.UnixEpoch);
    }

    private sealed class TestWellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        private static readonly IReadOnlyDictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Object"] = typeof(object),
            ["String"] = typeof(string),
            ["Boolean"] = typeof(bool),
            ["Int32"] = typeof(int),
        };

        public void RegisterType(Type type, string alias)
        {
        }

        public bool TryGetAlias(Type type, out string alias)
        {
            alias = type.Name;
            return true;
        }

        public bool TryGetType(string alias, out Type type) => Types.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => Types.Values;
        public string GetAliasOrDefault(Type type) => type.Name;
        public Type GetTypeOrDefault(string alias) => Types.GetValueOrDefault(alias, typeof(object));
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = GetTypeOrDefault(alias);
            return true;
        }
    }
}
