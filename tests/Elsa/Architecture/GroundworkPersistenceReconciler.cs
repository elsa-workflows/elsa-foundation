using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Elsa.Architecture.Tests;

internal sealed class GroundworkPersistenceReconciler
{
    private static readonly IReadOnlySet<LocalDuplicateBaseline> Existing644Duplicates =
        new HashSet<LocalDuplicateBaseline>
        {
            new("iam", "IUserStore", "GroundworkUserStore", "IdentityUserDocumentKind"),
            new("iam", "IRoleStore", "GroundworkRoleStore", "IdentityRoleDocumentKind"),
            new("iam", "IExternalIdentityStore", "GroundworkExternalIdentityStore", "ExternalLoginDocumentKind")
        };

    private readonly IReadOnlyList<GroundworkPersistenceRowMapping> _mappings;
    private readonly IReadOnlyDictionary<string, GroundworkDeferredPersistenceContract> _deferredContracts;

    private GroundworkPersistenceReconciler(
        IReadOnlyList<GroundworkPersistenceRowMapping> mappings,
        IReadOnlyCollection<GroundworkDeferredPersistenceContract> deferredContracts)
    {
        _mappings = mappings;
        _deferredContracts = deferredContracts.ToDictionary(item => item.Contract, StringComparer.Ordinal);
    }

    public static GroundworkPersistenceReconciler CreateDefault() => new(DefaultMappings, DeferredContracts);

    public IReadOnlyList<string> Reconcile(
        JsonObject ledger,
        GroundworkPersistenceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(inventory);

        var findings = new List<string>();
        var entries = EntriesById(ledger);

        ValidateDurableContracts(inventory.DurableContracts, entries, findings);
        ValidateMappedLedgerRows(entries, findings);
        ValidateRegistrations(inventory, entries, findings);
        ValidateManifestUnits(inventory, entries, findings);

        return findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private void ValidateDurableContracts(
        IEnumerable<string> durableContracts,
        IReadOnlyDictionary<string, JsonObject> entries,
        ICollection<string> findings)
    {
        foreach (var contract in durableContracts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var mappings = MappingsForContract(contract);
            if (mappings.Count == 0)
            {
                if (_deferredContracts.ContainsKey(contract))
                    continue;

                findings.Add($"{contract}: discovered durable contract has no explicit coverage-ledger mapping.");
                continue;
            }

            foreach (var mapping in mappings)
            {
                if (!entries.ContainsKey(mapping.LedgerRow))
                    findings.Add($"{contract}: explicit coverage-ledger mapping targets missing row '{mapping.LedgerRow}'.");
            }
        }
    }

    private void ValidateMappedLedgerRows(
        IReadOnlyDictionary<string, JsonObject> entries,
        ICollection<string> findings)
    {
        foreach (var mapping in _mappings)
        {
            if (!entries.TryGetValue(mapping.LedgerRow, out var entry))
                continue;

            if (mapping.Contract is not null && !ContractNames(entry).Contains(mapping.Contract, StringComparer.Ordinal))
            {
                findings.Add(
                    $"{mapping.LedgerRow}: explicit mapping for {mapping.Contract} no longer matches the ledger contract description.");
            }

            var family = StringValue(entry, "family");
            if (family != mapping.Owner)
            {
                findings.Add(
                    $"{mapping.LedgerRow}: explicit mapping owner '{mapping.Owner}' does not match ledger family '{family ?? "<missing>"}'.");
            }
        }

        foreach (var (entryId, entry) in entries)
        {
            foreach (var contract in ContractNames(entry))
            {
                if (_mappings.Any(mapping => mapping.Contract == contract && mapping.LedgerRow == entryId))
                    continue;

                if (_mappings.Any(mapping => mapping.Contract == contract))
                    findings.Add($"{entryId}: ledger row for {contract} has no explicit persistence mapping.");
            }
        }
    }

    private void ValidateRegistrations(
        GroundworkPersistenceInventorySnapshot inventory,
        IReadOnlyDictionary<string, JsonObject> entries,
        ICollection<string> findings)
    {
        foreach (var registration in inventory.Registrations
                     .OrderBy(candidate => candidate.Owner, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.Contract, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.Implementation, StringComparer.Ordinal))
        {
            var identity = $"{registration.Implementation} ({registration.Contract})";
            var mappings = MappingsForContract(registration.Contract);
            if (mappings.Count == 0)
            {
                if (_deferredContracts.TryGetValue(registration.Contract, out var deferred))
                {
                    findings.Add(
                        $"{identity}: Groundwork persistence is explicitly deferred to {deferred.Authority}; remove the deferral and add a coverage-ledger mapping before registration.");
                    continue;
                }

                findings.Add($"{identity}: registered Groundwork implementation has no explicit coverage-ledger mapping.");
                continue;
            }

            var wrongOwners = mappings
                .Where(mapping => mapping.Owner != registration.Owner)
                .Select(mapping => mapping.Owner)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (wrongOwners.Length > 0)
            {
                findings.Add(
                    $"{identity}: registration owner '{registration.Owner}' does not match mapped owner(s) {string.Join(", ", wrongOwners)}.");
                continue;
            }

            var mappedEntries = mappings
                .Select(mapping => entries.GetValueOrDefault(mapping.LedgerRow))
                .Where(entry => entry is not null)
                .Cast<JsonObject>()
                .ToArray();
            var isExternal = mappedEntries.Any(entry => StringValue(entry, "outcome") == "external-authority-adapter");
            var expectedStorageUnits = mappings
                .Select(mapping => mapping.StorageUnit)
                .Where(unit => unit is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (!isExternal)
            {
                foreach (var missingStorageUnit in expectedStorageUnits.Except(registration.StorageUnits, StringComparer.Ordinal))
                {
                    findings.Add(
                        $"{identity}: local Groundwork implementation does not identify mapped storage unit '{missingStorageUnit}'.");
                }
            }

            foreach (var storageUnit in registration.StorageUnits.Distinct(StringComparer.Ordinal))
            {
                if (!expectedStorageUnits.Contains(storageUnit, StringComparer.Ordinal))
                {
                    findings.Add(
                        $"{identity}: storage unit '{storageUnit}' has no explicit row mapping for {registration.Contract}.");
                    continue;
                }

                ValidateManifestOwnership(registration, storageUnit, inventory.ManifestStorageUnits, findings);
            }

            foreach (var entry in mappedEntries)
                ValidateExternalAuthority(entry, registration, findings);
        }
    }

    private static void ValidateManifestOwnership(
        GroundworkPersistenceRegistrationSnapshot registration,
        string storageUnit,
        IReadOnlyCollection<GroundworkManifestStorageUnit> manifestStorageUnits,
        ICollection<string> findings)
    {
        var identity = $"{registration.Implementation} ({registration.Contract})";
        if (manifestStorageUnits.Contains(new GroundworkManifestStorageUnit(registration.Owner, storageUnit)))
            return;

        var otherOwners = manifestStorageUnits
            .Where(unit => unit.StorageUnit == storageUnit)
            .Select(unit => unit.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (otherOwners.Length > 0)
        {
            findings.Add(
                $"{identity}: storage unit '{storageUnit}' belongs to the {string.Join(", ", otherOwners)} manifest, not {registration.Owner}.");
            return;
        }

        findings.Add(
            $"{identity}: storage unit '{storageUnit}' is not declared by the {registration.Owner} manifest.");
    }

    private static void ValidateExternalAuthority(
        JsonObject entry,
        GroundworkPersistenceRegistrationSnapshot registration,
        ICollection<string> findings)
    {
        if (StringValue(entry, "outcome") != "external-authority-adapter")
            return;

        var entryId = StringValue(entry, "id") ?? registration.Contract;
        var authority = StringValue(entry, "authority");
        var status = StringValue(entry, "status");
        var approvedContracts = authority switch
        {
            "#644" => new[] { "IUserStore", "IRoleStore", "IExternalIdentityStore" },
            "#660" => new[] { "IRuntimeDiagnosticsSettingsStore" },
            _ => []
        };
        if (!approvedContracts.Contains(registration.Contract, StringComparer.Ordinal))
        {
            var expectedOwnerDescription = authority switch
            {
                "#644" => "IUserStore, IRoleStore, or IExternalIdentityStore",
                "#660" => "IRuntimeDiagnosticsSettingsStore",
                _ => "an approved external contract"
            };
            findings.Add(
                $"{entryId}: authority {authority ?? "<missing>"} may own only {expectedOwnerDescription}; found {registration.Contract}.");
            return;
        }

        foreach (var storageUnit in registration.StorageUnits.Distinct(StringComparer.Ordinal))
        {
            if (authority == "#660")
            {
                findings.Add(
                    $"{entryId}: local #660 duplicate '{registration.Implementation}' / '{storageUnit}' is forbidden.");
                continue;
            }

            var duplicate = new LocalDuplicateBaseline(
                registration.Owner,
                registration.Contract,
                registration.Implementation,
                storageUnit);
            if (authority == "#644" && !Existing644Duplicates.Contains(duplicate))
            {
                findings.Add(
                    $"{entryId}: new local #644 duplicate '{registration.Implementation}' / '{storageUnit}' is forbidden; the exact pre-existing duplicate baseline is shrink-only.");
                continue;
            }

            if (status != "externally-blocked")
            {
                findings.Add(
                    $"{entryId}: external-authority row at status '{status ?? "<missing>"}' cannot retain local parallel storage unit '{storageUnit}'.");
            }
        }
    }

    private void ValidateManifestUnits(
        GroundworkPersistenceInventorySnapshot inventory,
        IReadOnlyDictionary<string, JsonObject> entries,
        ICollection<string> findings)
    {
        foreach (var unit in inventory.ManifestStorageUnits
                     .OrderBy(candidate => candidate.Owner, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.StorageUnit, StringComparer.Ordinal))
        {
            var mappings = _mappings
                .Where(mapping => mapping.Owner == unit.Owner && mapping.StorageUnit == unit.StorageUnit)
                .ToArray();
            if (mappings.Length == 0)
            {
                var representedByRegistration = inventory.Registrations.Any(registration =>
                    registration.StorageUnits.Contains(unit.StorageUnit, StringComparer.Ordinal));
                if (representedByRegistration)
                    continue;

                findings.Add(
                    $"{unit.Owner} manifest storage unit '{unit.StorageUnit}' has no explicit coverage-ledger row mapping.");
                continue;
            }

            foreach (var mapping in mappings)
            {
                if (!entries.TryGetValue(mapping.LedgerRow, out var entry))
                    continue;

                var authority = StringValue(entry, "authority");
                if (authority != "#660")
                    continue;

                var representedByRegistration = inventory.Registrations.Any(registration =>
                    registration.Owner == unit.Owner &&
                    registration.Contract == mapping.Contract &&
                    registration.StorageUnits.Contains(unit.StorageUnit, StringComparer.Ordinal));
                if (!representedByRegistration)
                {
                    findings.Add(
                        $"{mapping.LedgerRow}: local #660 storage unit '{unit.StorageUnit}' is forbidden even without a registration.");
                }
            }
        }
    }

    private IReadOnlyList<GroundworkPersistenceRowMapping> MappingsForContract(string contract) =>
        _mappings.Where(mapping => mapping.Contract == contract).ToArray();

    private static Dictionary<string, JsonObject> EntriesById(JsonObject ledger) =>
        ledger["entries"]?.AsArray()
            .OfType<JsonObject>()
            .Where(entry => entry["id"] is JsonValue)
            .ToDictionary(entry => entry["id"]!.GetValue<string>(), StringComparer.Ordinal)
        ?? new Dictionary<string, JsonObject>(StringComparer.Ordinal);

    private static IReadOnlyList<string> ContractNames(JsonObject entry)
    {
        var contract = StringValue(entry, "contract");
        if (contract is null)
            return [];

        return Regex.Matches(contract, @"\bI[A-Za-z0-9_]*(?:Store|Repository|Transport|Service|Scanner|Writer|WorkQueue)\b")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? StringValue(JsonObject value, string propertyName) =>
        value[propertyName] is JsonValue property && property.TryGetValue<string>(out var result)
            ? result
            : null;

    private static readonly GroundworkPersistenceRowMapping[] DefaultMappings =
    [
        Map("runtime", "IActivityExecutionInspectionStore", "runtime-activity-execution-inspection", "ActivityExecutionInspectionDocumentKind"),
        Map("runtime", "IActivityExecutionInspectionWriter", "runtime-activity-execution-inspection", "ActivityExecutionInspectionDocumentKind"),
        Map("runtime", "IActivityExecutionHierarchyStore", "runtime-activity-execution-inspection", "ActivityExecutionHierarchyDocumentKind"),
        Map("runtime", "IActivityExecutionHierarchyWriter", "runtime-activity-execution-inspection", "ActivityExecutionHierarchyDocumentKind"),
        Map("runtime", "IActivityExecutionStateStore", "runtime-activity-execution-state", "ActivityExecutionStateDocumentKind"),
        new("runtime", "IActivityScopeCleanupStore", "runtime-activity-execution-state", null),
        Map("runtime", "IBookmarkStateStore", "runtime-bookmark-state", "BookmarkStateDocumentKind"),
        Map("runtime", "IDurableTimerStore", "runtime-durable-timer", "DurableTimerDocumentKind"),
        Map("runtime", "IDurableValueStateStore", "runtime-durable-value-state", "DurableValueStateDocumentKind"),
        Map("runtime", "IExecutionLivenessStateStore", "runtime-execution-liveness", "ExecutionLivenessStateDocumentKind"),
        Map("runtime", "IRuntimeExecutionOwnershipService", "runtime-execution-liveness", "ExecutionLivenessStateDocumentKind"),
        Map("runtime", "IRuntimeRecoveryScanner", "runtime-execution-liveness", "ExecutionLivenessStateDocumentKind"),
        Map("runtime", "IIncidentStateStore", "runtime-incident-state", "IncidentStateDocumentKind"),
        Map("runtime", "IRecurringTriggerScheduleStore", "runtime-recurring-trigger-schedule", "RecurringTriggerScheduleDocumentKind"),
        Map("runtime", "IRuntimeCheckpointCommitStore", "runtime-checkpoint-commit", "CheckpointCommitDocumentKind"),
        Map("runtime", "IRuntimeDiagnosticsSettingsStore", "runtime-diagnostics-settings", "RuntimeDiagnosticsSettingsDocumentKind"),
        Map("runtime", "IRuntimePostCommitOutboxStore", "runtime-post-commit-outbox", "PostCommitOutboxDocumentKind"),
        Map("runtime", "ISchedulerStateStore", "runtime-scheduler-state", "SchedulerStateDocumentKind"),
        Map("runtime", "IWorkflowExecutableSourceReferenceStore", "runtime-executable-source-reference", "WorkflowExecutableSourceReferenceDocumentKind"),
        Map("runtime", "IWorkflowExecutableSourceReferenceWriter", "runtime-executable-source-reference", "WorkflowExecutableSourceReferenceDocumentKind"),
        Map("runtime", "IWorkflowExecutableStore", "runtime-workflow-executable", "WorkflowExecutableDocumentKind"),
        Map("runtime", "IExecutableActivityTemplateStore", "runtime-workflow-executable", "ExecutableActivityTemplateDocumentKind"),
        Map("runtime", "IExecutableActivityTemplateWriter", "runtime-workflow-executable", "ExecutableActivityTemplateDocumentKind"),
        Map("runtime", "IWorkflowExecutionStateStore", "runtime-workflow-execution-state", "WorkflowExecutionStateDocumentKind"),
        Map("runtime", "IWorkflowHoldStateStore", "runtime-workflow-hold-state", "WorkflowHoldStateDocumentKind"),
        Map("runtime", "IWorkflowSchedulerPoisonStore", "runtime-scheduler-poison", "SchedulerPoisonDocumentKind"),
        Map("runtime", "IWorkflowSchedulerWorkQueue", "runtime-scheduler-work-queue", "SchedulerWorkItemDocumentKind"),
        Map("runtime", "IWorkflowTriggerBindingStore", "runtime-trigger-binding", "WorkflowTriggerBindingDocumentKind"),
        Map("runtime", null, "runtime-publication-projection-state", "PublicationProjectionStateDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "IdentityUserDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "UserClaimDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "UserRoleDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "UserTokenDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "UserNameReservationDocumentKind"),
        Map("iam", "IUserStore", "iam-user", "EmailReservationDocumentKind"),
        Map("iam", null, "iam-user", "IdentityMutationReceiptDocumentKind"),
        Map("iam", "IRevisionAwareUserStore", "iam-user", "IdentityUserDocumentKind"),
        Map("iam", "IRoleStore", "iam-role", "IdentityRoleDocumentKind"),
        Map("iam", "IRoleStore", "iam-role", "RoleClaimDocumentKind"),
        Map("iam", "IRoleStore", "iam-role", "RoleNameReservationDocumentKind"),
        Map("iam", "IRevisionAwareRoleStore", "iam-role", "IdentityRoleDocumentKind"),
        Map("iam", "IApplicationStore", "iam-application", "IdentityApplicationDocumentKind"),
        Map("iam", "ICredentialStore", "iam-credential", "IdentityCredentialDocumentKind"),
        Map("iam", "IExternalIdentityStore", "iam-external-identity", "ExternalLoginDocumentKind"),
        Map("iam", "IRevisionAwareExternalIdentityStore", "iam-external-identity", "ExternalLoginDocumentKind"),
        Map("iam", "IClaimMappingStore", "iam-claim-mapping", "IdentityClaimMappingDocumentKind"),
        Map("iam", "IRevisionAwareClaimMappingStore", "iam-claim-mapping", "IdentityClaimMappingDocumentKind"),
        Map("iam", "IProviderConfigurationStore", "iam-provider-configuration-tenant", "IdentityProviderConfigurationDocumentKind"),
        Map("iam", "IProviderConfigurationStore", "iam-provider-configuration-global", "IdentityProviderConfigurationDocumentKind"),
        Map("iam", "IRevisionAwareProviderConfigurationStore", "iam-provider-configuration-tenant", "IdentityProviderConfigurationDocumentKind"),
        Map("iam", "IRevisionAwareProviderConfigurationStore", "iam-provider-configuration-global", "IdentityProviderConfigurationDocumentKind"),
        Map("iam", "ITenantMembershipStore", "iam-tenant-membership", "IdentityTenantMembershipDocumentKind"),
        Map("iam", "IRevisionAwareTenantMembershipStore", "iam-tenant-membership", "IdentityTenantMembershipDocumentKind"),
        Map("secrets", "ISecretRepository", "secrets-repository", "SecretDocumentKind"),
        Map("distributed-runtime", "IExecutionPlacementStore", "distributed-execution-placement", "ExecutionPlacementDocumentKind"),
        Map("distributed-runtime", "IExecutionCommandTransport", "distributed-command-transport", "ExecutionCommandTransportDocumentKind")
    ];

    private static readonly GroundworkDeferredPersistenceContract[] DeferredContracts =
    [
        // #676 introduces the in-memory projection and rejects non-empty Groundwork checkpoint changes.
        // #678 owns the provider-backed storage unit, restart convergence, and permanent ledger row.
        new("IWorkflowDispatchStore", "#678")
    ];

    private static GroundworkPersistenceRowMapping Map(
        string owner,
        string? contract,
        string ledgerRow,
        string storageUnit) =>
        new(owner, contract, ledgerRow, storageUnit);

    private sealed record LocalDuplicateBaseline(
        string Owner,
        string Contract,
        string Implementation,
        string StorageUnit);
}

internal sealed record GroundworkPersistenceInventorySnapshot(
    IReadOnlyCollection<string> DurableContracts,
    IReadOnlyCollection<GroundworkPersistenceRegistrationSnapshot> Registrations,
    IReadOnlyCollection<GroundworkManifestStorageUnit> ManifestStorageUnits);

internal sealed record GroundworkPersistenceRegistrationSnapshot(
    string Owner,
    string Contract,
    string Implementation,
    IReadOnlyCollection<string> StorageUnits);

internal sealed record GroundworkManifestStorageUnit(string Owner, string StorageUnit);

internal sealed record GroundworkPersistenceRowMapping(
    string Owner,
    string? Contract,
    string LedgerRow,
    string? StorageUnit);

internal sealed record GroundworkDeferredPersistenceContract(string Contract, string Authority);
