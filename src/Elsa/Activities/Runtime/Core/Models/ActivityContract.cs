using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Immutable activity contract pinned into a workflow executable.
/// </summary>
public sealed class ActivityContract
{
    [JsonConstructor]
    internal ActivityContract(
        string activityTypeKey,
        string contractVersion,
        string schemaFingerprint,
        string descriptorKind,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, ActivityInputContract> inputs,
        ActivityResultContract result,
        IReadOnlyCollection<string> outcomes,
        ActivityActivationRequirement activation)
        : this(activityTypeKey, contractVersion, descriptorKind, descriptorPayload, inputs.Values, result, outcomes, activation)
    {
        if (!StringComparer.Ordinal.Equals(schemaFingerprint, SchemaFingerprint))
            throw new JsonException($"Activity contract fingerprint '{schemaFingerprint}' does not match computed fingerprint '{SchemaFingerprint}'.");
    }

    public ActivityContract(
        string activityTypeKey,
        string contractVersion,
        string descriptorKind,
        JsonElement descriptorPayload,
        IEnumerable<ActivityInputContract> inputs,
        ActivityResultContract result,
        IEnumerable<string> outcomes,
        ActivityActivationRequirement activation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTypeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorKind);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(activation);

        var inputArray = inputs.ToArray();
        var outcomeArray = outcomes.ToArray();
        ValidateUnique(inputArray.Select(x => x.Key), "input");
        ValidateUnique(outcomeArray, "outcome");

        if (outcomeArray.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Activity outcomes cannot be empty.", nameof(outcomes));

        ActivityTypeKey = activityTypeKey;
        ContractVersion = contractVersion;
        DescriptorKind = descriptorKind;
        DescriptorPayload = descriptorPayload.Clone();
        Inputs = inputArray.ToDictionary(x => x.Key, StringComparer.Ordinal);
        Result = result;
        Outcomes = outcomeArray.ToArray();
        Activation = activation;
        SchemaFingerprint = ComputeFingerprint();
    }

    public string ActivityTypeKey { get; }
    public string ContractVersion { get; }
    public string SchemaFingerprint { get; }
    public string DescriptorKind { get; }
    public JsonElement DescriptorPayload { get; }
    public IReadOnlyDictionary<string, ActivityInputContract> Inputs { get; }
    public ActivityResultContract Result { get; }
    public IReadOnlyCollection<string> Outcomes { get; }
    public ActivityActivationRequirement Activation { get; }

    private string ComputeFingerprint()
    {
        var payload = new
        {
            activityTypeKey = ActivityTypeKey,
            contractVersion = ContractVersion,
            descriptorKind = DescriptorKind,
            descriptorPayload = DescriptorPayload,
            inputs = Inputs.Values
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new
                {
                    x.Key,
                    x.Type,
                    x.IsRequired,
                    x.HasDefault,
                    x.DefaultValue,
                    x.Policy
                }),
            result = new
            {
                Result.Type,
                Result.IsRequired,
                Result.Policy,
                projections = Result.Projections.Values
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new { x.Key, x.Path, x.Type, x.IsRequired, x.Policy })
            },
            outcomes = Outcomes.Order(StringComparer.Ordinal),
            activation = Activation
        };

        var json = JsonSerializer.Serialize(payload);
        var canonical = SortNode(JsonNode.Parse(json)!).ToJsonString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void ValidateUnique(IEnumerable<string> keys, string role)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (!set.Add(key))
                throw new ArgumentException($"Duplicate activity {role} key '{key}'.");
        }
    }

    private static JsonNode SortNode(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => KeyValuePair.Create(x.Key, x.Value is null ? null : SortNode(x.Value)))),
        JsonArray array => new JsonArray(array
            .Select(x => x is null ? null : SortNode(x))
            .ToArray()),
        _ => node.DeepClone()
    };
}

public sealed class ActivityInputContract
{
    public ActivityInputContract(
        string key,
        string name,
        ValueTypeDescriptor type,
        bool isRequired,
        bool hasDefault,
        JsonElement? defaultValue,
        ActivityValuePolicy policy,
        IReadOnlyDictionary<string, string>? editorMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);

        if (hasDefault != defaultValue.HasValue)
            throw new ArgumentException("An activity input default presence flag must match its payload.", nameof(defaultValue));

        Key = key;
        Name = name;
        Type = type;
        IsRequired = isRequired;
        HasDefault = hasDefault;
        DefaultValue = defaultValue?.Clone();
        Policy = policy;
        EditorMetadata = editorMetadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(editorMetadata, StringComparer.Ordinal);
    }

    public string Key { get; }
    public string Name { get; }
    public ValueTypeDescriptor Type { get; }
    public bool IsRequired { get; }
    public bool HasDefault { get; }
    public JsonElement? DefaultValue { get; }
    public ActivityValuePolicy Policy { get; }
    public IReadOnlyDictionary<string, string> EditorMetadata { get; }
}

public sealed class ActivityResultContract
{
    [JsonConstructor]
    internal ActivityResultContract(
        ValueTypeDescriptor type,
        bool isRequired,
        ActivityValuePolicy policy,
        IReadOnlyDictionary<string, ActivityResultProjectionContract> projections)
        : this(type, isRequired, policy, projections.Values)
    {
    }

    public ActivityResultContract(
        ValueTypeDescriptor type,
        bool isRequired,
        ActivityValuePolicy policy,
        IEnumerable<ActivityResultProjectionContract> projections)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(projections);

        var projectionArray = projections.ToArray();
        var duplicate = projectionArray
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Duplicate activity result projection key '{duplicate.Key}'.", nameof(projections));

        Type = type;
        IsRequired = isRequired;
        Policy = policy;
        Projections = projectionArray.ToDictionary(x => x.Key, StringComparer.Ordinal);
    }

    public ValueTypeDescriptor Type { get; }
    public bool IsRequired { get; }
    public ActivityValuePolicy Policy { get; }
    public IReadOnlyDictionary<string, ActivityResultProjectionContract> Projections { get; }
}

public sealed class ActivityResultProjectionContract
{
    public ActivityResultProjectionContract(
        string key,
        string path,
        ValueTypeDescriptor type,
        bool isRequired,
        ActivityValuePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);

        Key = key;
        Path = path;
        Type = type;
        IsRequired = isRequired;
        Policy = policy;
    }

    public string Key { get; }
    public string Path { get; }
    public ValueTypeDescriptor Type { get; }
    public bool IsRequired { get; }
    public ActivityValuePolicy Policy { get; }
}

public sealed record ActivityActivationRequirement
{
    public ActivityActivationRequirement(string descriptorKind, string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        DescriptorKind = descriptorKind;
        Capability = capability;
    }

    public string DescriptorKind { get; }
    public string Capability { get; }
}

public sealed record ActivityValuePolicy(
    bool IsPersistable,
    bool IsSensitive,
    bool RequiresEncryption,
    string? RedactionMode = null)
{
    public static ActivityValuePolicy Default { get; } = new(
        IsPersistable: true,
        IsSensitive: false,
        RequiresEncryption: false);
}
