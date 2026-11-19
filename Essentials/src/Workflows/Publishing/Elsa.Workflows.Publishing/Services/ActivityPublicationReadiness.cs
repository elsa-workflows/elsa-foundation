using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>The same selected Runtime and durable-storage readiness for draft, Agent and Workflow publication.</summary>
public sealed class ActivityPublicationReadiness(IEnumerable<IActivityActivationStrategy>? strategies = null, IRuntimeDurableValueStorageDriverRegistry? storageDrivers = null) : IActivityPublicationReadiness
{
    private readonly IActivityActivationStrategy[] strategies = strategies?.ToArray() ?? [];

    public IReadOnlyList<PublicationCapabilityReadiness> StorageReadiness(IEnumerable<RuntimeStorageDriverRequirement> requirements) => requirements
        .Distinct().OrderBy(x => x.DriverKey, StringComparer.Ordinal)
        .Select(x => new PublicationCapabilityReadiness("StorageDriver", x.DriverKey, null, storageDrivers?.Contains(x.DriverKey) == true ? "Available" : "Missing", [])).ToArray();

    public IReadOnlyList<PublicationCapabilityReadiness> RuntimeReadiness(IEnumerable<RuntimeRequirement> requirements) => requirements
        .Distinct().OrderBy(x => x.ConsumerKey, StringComparer.Ordinal).ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
        .Select(x =>
        {
            var schemas = strategies.Where(s => s.ConsumerKey == x.ConsumerKey).SelectMany(s => s.SupportedSchemaVersions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new PublicationCapabilityReadiness("RuntimeConsumer", x.ConsumerKey, x.SchemaVersion,
                schemas.Contains(x.SchemaVersion, StringComparer.Ordinal) ? "Available" : schemas.Length == 0 ? "Missing" : "UnsupportedSchema", schemas);
        }).ToArray();

    public IEnumerable<ActivityDiagnostic> ReadinessDiagnostics(ActivityDiagnosticSubject subject, IEnumerable<RuntimeRequirement> runtime, IEnumerable<RuntimeStorageDriverRequirement> storage)
    {
        foreach (var item in StorageReadiness(storage).Where(x => x.Status != "Available"))
            yield return new("activity.runtime.storage-driver-missing", ActivityDiagnosticSeverity.Error,
                $"Required durable value storage driver '{item.Key}' is unavailable.", subject,
                Remediation: "Register the required durable value storage driver before publication.", Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["driverKey"] = item.Key });
        foreach (var item in RuntimeReadiness(runtime).Where(x => x.Status != "Available"))
            yield return new(item.Status == "UnsupportedSchema" ? "activity.runtime.consumer-schema-unsupported" : "activity.runtime.consumer-missing", ActivityDiagnosticSeverity.Error,
                $"Required Runtime consumer '{item.Key}' schema '{item.SchemaVersion}' is unavailable.", subject,
                Remediation: "Deploy and register a Runtime consumer that supports the exact schema before publication.", Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["consumerKey"] = item.Key, ["schemaVersion"] = item.SchemaVersion ?? "" });
    }

    public string Fingerprint(string artifactHash, IEnumerable<RuntimeRequirement> runtime, IEnumerable<RuntimeStorageDriverRequirement> storage) =>
        ExecutableActivityTemplateBehaviorHasher.ComputeCanonicalValueHash(new { artifactHash, Runtime = RuntimeReadiness(runtime), Storage = StorageReadiness(storage) });
}
