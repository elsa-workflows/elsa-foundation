using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// The provider-neutral v2 storage declarations contributed by Elsa features. A declaration is
/// identified by target and unit ID; exact repeats are idempotent, while two shapes claiming the
/// same identity fail during service composition rather than racing at provider startup.
/// </summary>
public sealed class GroundworkStorageUnitRegistry : IRuntimePersistenceRegistrationState
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

    /// <summary>Restores a previously captured declaration set when service composition rolls back.</summary>
    public void Restore(IEnumerable<GroundworkStorageUnitRegistration> registrationsSnapshot)
    {
        ArgumentNullException.ThrowIfNull(registrationsSnapshot);
        lock (gate)
        {
            registrations.Clear();
            foreach (var registration in registrationsSnapshot)
                registrations.Add((registration.TargetName, registration.Unit.Id.Value), registration);
        }
    }

    public IRuntimePersistenceRegistrationSnapshot CaptureSnapshot() =>
        new RegistrationSnapshot(this, Registrations);

    private sealed class RegistrationSnapshot(
        GroundworkStorageUnitRegistry registry,
        IReadOnlyList<GroundworkStorageUnitRegistration> registrations) : IRuntimePersistenceRegistrationSnapshot
    {
        public void Rollback() => registry.Restore(registrations);
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

    /// <summary>
    /// Withdraws declarations for <paramref name="unitId"/> before the service provider is built.
    /// An omitted target preserves the historical all-target behavior; a target restricts withdrawal
    /// to that physical store so another target's declaration remains active.
    /// </summary>
    public void Withdraw(string unitId, string? targetName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        var target = targetName is null ? null : GroundworkTargetNames.Normalize(targetName);
        lock (gate)
        {
            foreach (var key in registrations.Keys.Where(candidate =>
                         StringComparer.Ordinal.Equals(candidate.UnitId, unitId) &&
                         (target is null || StringComparer.Ordinal.Equals(candidate.Target, target))).ToArray())
            {
                registrations.Remove(key);
            }
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
