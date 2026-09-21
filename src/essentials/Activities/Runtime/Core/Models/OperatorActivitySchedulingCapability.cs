using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Immutable, publish-time evidence that an activity module explicitly permits an operator to schedule a particular
/// direct child relation. Its stable identity becomes part of the executable artifact; Runtime must never infer this
/// permission merely from topology.
/// </summary>
public sealed record OperatorActivitySchedulingCapability
{
    public OperatorActivitySchedulingCapability(string policyKey, int schemaVersion, JsonElement configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyKey);
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "An operator scheduling capability schema version must be positive.");

        PolicyKey = policyKey;
        SchemaVersion = schemaVersion;
        Configuration = configuration.Clone();
    }

    public string PolicyKey { get; }
    public int SchemaVersion { get; }
    public JsonElement Configuration { get; }
}

/// <summary>Compile-time input supplied to an activity-owned operator-scheduling capability contributor.</summary>
public sealed record OperatorActivitySchedulingCapabilityCompilationContext(
    string ParentActivityType,
    string ParentExecutableNodeId,
    string ChildSlotName,
    IReadOnlyCollection<string> ChildExecutableNodeIds);

/// <summary>
/// Activity-module contribution used by publishing to pin explicit operator-scheduling authorization onto an
/// executable child relation. Returning <see langword="false"/> leaves that relation unschedulable.
/// </summary>
public interface IOperatorActivitySchedulingCapabilityProvider
{
    bool TryDescribe(
        OperatorActivitySchedulingCapabilityCompilationContext context,
        out OperatorActivitySchedulingCapability? capability);
}
