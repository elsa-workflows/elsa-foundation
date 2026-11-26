using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

public sealed record TriggerFinding(string Code, string Message, string? Field = null, string? InputName = null);
public sealed record TriggerFieldChoice(string Value, string Label);
public sealed record TriggerConfigurationField(string Key, string Label, string Type = "string", bool Required = false,
    JsonElement? DefaultValue = null, IReadOnlyList<TriggerFieldChoice>? Choices = null, string? Description = null,
    bool Sensitive = false, bool ReadOnly = false);
public sealed record TriggerSourceField(IReadOnlyList<string> Path, string Label, string? Type = null);

/// <summary>Detached authoring metadata. Describing a type does not install its source intake or grant invocation authority.</summary>
public sealed record TriggerTypeDescriptor(string TypeKey, int SchemaVersion, string DisplayName, string Family,
    IReadOnlyList<TriggerConfigurationField> ConfigurationFields, IReadOnlyList<TriggerSourceField> SourceFields,
    string? Description = null)
{
    /// <summary>Optional Studio editor contribution. Generic metadata remains usable when that editor is absent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigurationEditorId { get; init; }
}

/// <summary>Metadata contribution, aggregated through StandaloneTriggerTypesInitializing.</summary>
public interface IStandaloneTriggerTypeSource
{
    IEnumerable<TriggerTypeDescriptor> GetTypes();
}

/// <summary>Scoped authoring validation contribution. One owner per type; no source or target execution is permitted.</summary>
public interface ITriggerConfigurationValidator
{
    string TypeKey { get; }
    ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration,
        CancellationToken cancellationToken = default);
}

/// <summary>Replacement contract: immutable installed authoring metadata, independently composed from runtime sources.</summary>
public interface IStandaloneTriggerTypeCatalog
{
    IReadOnlyList<TriggerTypeDescriptor> List();
    TriggerTypeDescriptor? Find(string typeKey);
}

/// <summary>Sequential startup fan-in; one aggregator collects type sources before the catalog freezes.</summary>
public sealed class StandaloneTriggerTypesInitializing : IEvent
{
    public ICollection<TriggerTypeDescriptor> Types { get; } = [];
}

/// <summary>Sequential authoring gate; one aggregator invokes the selected type's configuration validator.</summary>
public sealed class StandaloneTriggerConfigurationValidating(string typeKey, int schemaVersion, JsonElement configuration) : IEvent
{
    public string TypeKey { get; } = typeKey;
    public int SchemaVersion { get; } = schemaVersion;
    public JsonElement Configuration { get; } = configuration.Clone();
    public ICollection<TriggerFinding> Findings { get; } = [];
}

public sealed record TriggerTargetQuery(int Limit = 25, string? After = null, string? Search = null);
public sealed record TriggerTargetPage(IReadOnlyList<TriggerTargetDescription> Items, string? Next);
public enum TriggerTargetState { Available, Unpublished, Unavailable }
/// <summary>Exact logical target resource used by host permission handlers; it is never saved start authority.</summary>
public sealed record TriggerTargetResource(string DefinitionId, string SlotName);

/// <summary>Authorized authoring read model. Runtime admission independently validates the actual slot/material.</summary>
public sealed record TriggerTargetDescription(string DefinitionId, string SlotName, string? DisplayName, string ModelKey,
    TriggerTargetState State, WorkflowExecutableInputContract? InputContract, string? ContractRevision, string? VersionId,
    string? Version, string? PublicationId, long? SlotRevision, bool SupportsWorkflowOwnedHttpResponse,
    IReadOnlyList<TriggerFinding> Findings);

/// <summary>Authoring query replacement contract. It neither inherits nor resolves a runtime target adapter.</summary>
public interface IStandaloneTriggerTargetDiscovery
{
    ValueTask<TriggerTargetPage> ListAsync(TriggerScope scope, TriggerTargetQuery query, CancellationToken cancellationToken = default);
    ValueTask<TriggerTargetDescription?> DescribeAsync(TriggerScope scope, string definitionId, string slotName,
        CancellationToken cancellationToken = default);
}
