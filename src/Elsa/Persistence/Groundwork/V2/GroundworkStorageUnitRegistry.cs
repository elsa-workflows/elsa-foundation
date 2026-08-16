using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Elsa.Persistence.Groundwork.Targets;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// The provider-neutral v2 storage declarations contributed by Elsa features. A declaration is
/// identified by target and unit ID; exact repeats are idempotent, while two shapes claiming the
/// same identity fail during service composition rather than racing at provider startup.
/// </summary>
public sealed class GroundworkStorageUnitRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<(string Target, string UnitId), GroundworkStorageUnitRegistration> registrations = [];

    public IReadOnlyList<GroundworkStorageUnitRegistration> Registrations
    {
        get
        {
            lock (gate)
            {
                return registrations.Values
                    .OrderBy(candidate => candidate.TargetName, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Unit.Id.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public void Declare(StorageUnit unit, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var target = GroundworkTargetNames.Normalize(targetName);
        var subject = new SchemaSubject(unit);
        var registration = new GroundworkStorageUnitRegistration(target, subject.Definition, subject.Fingerprint);
        var key = (target, subject.Id.Value);

        lock (gate)
        {
            if (!registrations.TryGetValue(key, out var existing))
            {
                registrations.Add(key, registration);
                return;
            }

            if (StringComparer.Ordinal.Equals(existing.Fingerprint, registration.Fingerprint))
                return;

            throw new InvalidOperationException(
                $"Groundwork storage unit '{subject.Id.Value}' was declared twice for target '{target}' " +
                "with different schemas. Give distinct units distinct IDs or consolidate the declaration.");
        }
    }

    public GroundworkStorageUnitRegistration Require(string unitId, string? targetName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        var target = GroundworkTargetNames.Normalize(targetName);
        lock (gate)
        {
            if (registrations.TryGetValue((target, unitId), out var registration))
                return registration;
        }

        throw new InvalidOperationException(
            $"Groundwork storage unit '{unitId}' is not declared for target '{target}'. " +
            "Register its v2 storage declaration before opening a session.");
    }
}

public sealed record GroundworkStorageUnitRegistration(
    string TargetName,
    StorageUnit Unit,
    string Fingerprint);
