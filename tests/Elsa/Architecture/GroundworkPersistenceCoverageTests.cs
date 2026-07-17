using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkPersistenceCoverageTests
{
    [Fact]
    public void Checked_in_contract_registration_and_manifest_inventory_reconciles()
    {
        var ledger = ReadLedger();
        var inventory = new GroundworkPersistenceInventoryScanner(RepoRoot).Scan();

        var findings = Reconcile(ledger, inventory);

        Assert.Empty(findings);
    }

    [Fact]
    public void Discovered_durable_contract_without_an_explicit_ledger_mapping_is_rejected()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(durableContracts: ["INewDurableStore"]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["INewDurableStore: discovered durable contract has no explicit coverage-ledger mapping."],
            findings);
    }

    [Fact]
    public void Workflow_dispatch_persistence_has_an_explicit_ledger_mapping()
    {
        var findings = Reconcile(
            ReadLedger(),
            Inventory(durableContracts: ["IWorkflowDispatchStore"]));

        Assert.Empty(findings);
    }

    [Fact]
    public void Workflow_dispatch_registration_matches_its_manifest_storage_unit()
    {
        var findings = Reconcile(
            ReadLedger(),
            Inventory(
                durableContracts: ["IWorkflowDispatchStore"],
                registrations:
                [
                    Registration(
                        "runtime",
                        "IWorkflowDispatchStore",
                        "GroundworkWorkflowDispatchStore",
                        "WorkflowDispatchDocumentKind")
                ],
                manifestStorageUnits: [ManifestUnit("runtime", "WorkflowDispatchDocumentKind")]));

        Assert.Empty(findings);
    }

    [Fact]
    public void Registered_Groundwork_implementation_without_an_explicit_ledger_mapping_is_rejected()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "runtime",
                    "INewDurableStore",
                    "GroundworkNewDurableStore",
                    "NewDurableStoreDocumentKind")
            ],
            manifestStorageUnits: [ManifestUnit("runtime", "NewDurableStoreDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["GroundworkNewDurableStore (INewDurableStore): registered Groundwork implementation has no explicit coverage-ledger mapping."],
            findings);
    }

    [Fact]
    public void Provider_configuration_registration_maps_explicitly_to_both_tenant_and_global_rows()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            durableContracts: ["IProviderConfigurationStore"],
            registrations:
            [
                Registration(
                    "iam",
                    "IProviderConfigurationStore",
                    "GroundworkProviderConfigurationStore",
                    "IdentityProviderConfigurationDocumentKind")
            ],
            manifestStorageUnits:
            [
                ManifestUnit("iam", "IdentityProviderConfigurationDocumentKind")
            ]);

        var findings = Reconcile(ledger, inventory);

        Assert.Empty(findings);
    }

    [Fact]
    public void Registered_local_implementation_must_identify_every_explicit_storage_unit()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "runtime",
                    "IActivityExecutionInspectionStore",
                    "GroundworkActivityExecutionInspectionStore")
            ]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["GroundworkActivityExecutionInspectionStore (IActivityExecutionInspectionStore): local Groundwork implementation does not identify mapped storage unit 'ActivityExecutionInspectionDocumentKind'."],
            findings);
    }

    [Fact]
    public void Registered_local_implementation_must_reference_a_declared_manifest_storage_unit()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "runtime",
                    "IActivityExecutionInspectionStore",
                    "GroundworkActivityExecutionInspectionStore",
                    "ActivityExecutionInspectionDocumentKind")
            ]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["GroundworkActivityExecutionInspectionStore (IActivityExecutionInspectionStore): storage unit 'ActivityExecutionInspectionDocumentKind' is not declared by the runtime manifest."],
            findings);
    }

    [Fact]
    public void Registration_cannot_borrow_a_storage_unit_from_another_family_manifest()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "runtime",
                    "IActivityExecutionInspectionStore",
                    "GroundworkActivityExecutionInspectionStore",
                    "ActivityExecutionInspectionDocumentKind")
            ],
            manifestStorageUnits: [ManifestUnit("iam", "ActivityExecutionInspectionDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["GroundworkActivityExecutionInspectionStore (IActivityExecutionInspectionStore): storage unit 'ActivityExecutionInspectionDocumentKind' belongs to the iam manifest, not runtime."],
            findings);
    }

    [Theory]
    [InlineData("iam-application", "#644", "IApplicationStore", "IUserStore, IRoleStore, or IExternalIdentityStore")]
    [InlineData("runtime-workflow-execution-state", "#660", "IWorkflowExecutionStateStore", "IRuntimeDiagnosticsSettingsStore")]
    public void External_authority_cannot_claim_an_unapproved_contract(
        string entryId,
        string authority,
        string contract,
        string expectedOwnerDescription)
    {
        var ledger = ReadLedger();
        var entry = Entry(ledger, entryId);
        entry["outcome"] = "external-authority-adapter";
        entry["authority"] = authority;
        entry["dependency"] = authority;
        entry["status"] = "externally-blocked";
        var inventory = Inventory(
            durableContracts: [contract],
            registrations:
            [
                Registration(
                    entry["family"]!.GetValue<string>(),
                    contract,
                    $"{contract}Adapter")
            ]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            [$"{entryId}: authority {authority} may own only {expectedOwnerDescription}; found {contract}."],
            findings);
    }

    [Theory]
    [InlineData("iam-user", "IUserStore", "GroundworkUserStore", "IdentityUserDocumentKind")]
    [InlineData("iam-role", "IRoleStore", "GroundworkRoleStore", "IdentityRoleDocumentKind")]
    [InlineData("iam-external-identity", "IExternalIdentityStore", "GroundworkExternalIdentityStore", "ExternalLoginDocumentKind")]
    public void Exact_pre_existing_644_local_duplicate_is_a_shrink_only_baseline(
        string entryId,
        string contract,
        string implementation,
        string storageUnit)
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations: [Registration("iam", contract, implementation, storageUnit)],
            manifestStorageUnits: [ManifestUnit("iam", storageUnit)]);

        var findings = Reconcile(ledger, inventory);

        Assert.Empty(findings);
        Assert.Equal("externally-blocked", Entry(ledger, entryId)["status"]!.GetValue<string>());
    }

    [Fact]
    public void Removing_a_pre_existing_644_duplicate_does_not_require_replacement()
    {
        var ledger = ReadLedger();

        var findings = Reconcile(ledger, Inventory());

        Assert.Empty(findings);
    }

    [Fact]
    public void New_644_local_duplicate_cannot_hide_behind_an_externally_blocked_row()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "iam",
                    "IUserStore",
                    "GroundworkReplacementUserStore",
                    "IdentityUserDocumentKind")
            ],
            manifestStorageUnits: [ManifestUnit("iam", "IdentityUserDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["iam-user: new local #644 duplicate 'GroundworkReplacementUserStore' / 'IdentityUserDocumentKind' is forbidden; the exact pre-existing duplicate baseline is shrink-only."],
            findings);
    }

    [Fact]
    public void Spec095_replaces_the_legacy_iam_document_authority_units()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "Persistence",
            "Groundwork",
            "IdentityStorageManifest.cs"));

        var legacyUnits = new[]
            {
                "public const string UserDocumentKind",
                "public const string RoleDocumentKind",
                "public const string ExternalIdentityDocumentKind",
                "public const string TenantMembershipDocumentKind"
            }
            .Where(source.Contains)
            .ToArray();

        Assert.True(
            legacyUnits.Length == 0,
            "Spec 095 must replace the legacy IAM document authority units with the ASP.NET Core Identity authority units: " +
            string.Join(", ", legacyUnits));
    }

    [Fact]
    public void Unified_groundwork_provider_registration_does_not_select_identity_implicitly()
    {
        var providerRegistrations = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa", "Persistence", "Groundwork"), "*UnifiedRegistration.cs", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'), Source: File.ReadAllText(path)))
            .Where(candidate => candidate.Source.Contains("AddGroundworkIdentityStores", StringComparison.Ordinal))
            .Select(candidate => candidate.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            providerRegistrations.Length == 0,
            "Unified Groundwork providers must expose substrate only; Identity must be selected by the explicit ASP.NET Core Identity Groundwork feature. Offending files: " +
            string.Join(", ", providerRegistrations));
    }

    [Fact]
    public void Unified_groundwork_provider_projects_do_not_reference_identity_implementations()
    {
        var providerProjects = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa", "Persistence", "Groundwork"), "*.Unified.csproj", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'), Source: File.ReadAllText(path)))
            .Where(candidate => candidate.Source.Contains("Foundation\\Identity", StringComparison.Ordinal) ||
                                candidate.Source.Contains("Foundation/Identity", StringComparison.Ordinal))
            .Select(candidate => candidate.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            providerProjects.Length == 0,
            "Unified Groundwork provider projects must not directly reference Identity implementations. Offending projects: " +
            string.Join(", ", providerProjects));
    }

    [Fact]
    public void Every_660_local_duplicate_is_rejected_even_while_the_row_is_externally_blocked()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            registrations:
            [
                Registration(
                    "runtime",
                    "IRuntimeDiagnosticsSettingsStore",
                    "GroundworkRuntimeDiagnosticsSettingsStore",
                    "RuntimeDiagnosticsSettingsDocumentKind")
            ],
            manifestStorageUnits: [ManifestUnit("runtime", "RuntimeDiagnosticsSettingsDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["runtime-diagnostics-settings: local #660 duplicate 'GroundworkRuntimeDiagnosticsSettingsStore' / 'RuntimeDiagnosticsSettingsDocumentKind' is forbidden."],
            findings);
    }

    [Fact]
    public void A_660_manifest_unit_is_rejected_even_without_a_registration()
    {
        var ledger = ReadLedger();
        var inventory = Inventory(
            manifestStorageUnits: [ManifestUnit("runtime", "RuntimeDiagnosticsSettingsDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["runtime-diagnostics-settings: local #660 storage unit 'RuntimeDiagnosticsSettingsDocumentKind' is forbidden even without a registration."],
            findings);
    }

    [Fact]
    public void Pre_existing_644_duplicate_cannot_advance_past_externally_blocked()
    {
        var ledger = ReadLedger();
        Entry(ledger, "iam-user")["status"] = "implemented";
        var inventory = Inventory(
            registrations: [Registration("iam", "IUserStore", "GroundworkUserStore", "IdentityUserDocumentKind")],
            manifestStorageUnits: [ManifestUnit("iam", "IdentityUserDocumentKind")]);

        var findings = Reconcile(ledger, inventory);

        Assert.Equal(
            ["iam-user: external-authority row at status 'implemented' cannot retain local parallel storage unit 'IdentityUserDocumentKind'."],
            findings);
    }

    [Fact]
    public void Scanner_discovers_registration_files_added_anywhere_under_an_in_scope_Groundwork_family()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "Nested/AdditionalRegistration.cs",
            "services.AddSingleton<IBookmarkStateStore, GroundworkBookmarkStateStore>();");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var inventory = new GroundworkPersistenceInventoryScanner(fixture.Root).Scan();

        var registration = Assert.Single(inventory.Registrations);
        Assert.Equal("runtime", registration.Owner);
        Assert.Equal("IBookmarkStateStore", registration.Contract);
        Assert.Equal("GroundworkBookmarkStateStore", registration.Implementation);
        Assert.Equal(["BookmarkStateDocumentKind"], registration.StorageUnits);
    }

    [Theory]
    [InlineData("Contracts.IBookmarkStateStore", "Stores.GroundworkBookmarkStateStore")]
    [InlineData("global::Contracts.IBookmarkStateStore", "global::Stores.GroundworkBookmarkStateStore")]
    public void Scanner_normalizes_qualified_registration_contracts_and_implementations(
        string contract,
        string implementation)
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "DependencyInjection/QualifiedRegistration.cs",
            $"services.AddSingleton<{contract}, {implementation}>();");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var inventory = new GroundworkPersistenceInventoryScanner(fixture.Root).Scan();

        var registration = Assert.Single(inventory.Registrations);
        Assert.Equal("IBookmarkStateStore", registration.Contract);
        Assert.Equal("GroundworkBookmarkStateStore", registration.Implementation);
    }

    [Fact]
    public void Scanner_fails_closed_when_a_durable_descriptor_is_added_indirectly()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "DependencyInjection/DescriptorRegistration.cs",
            "var descriptor = ServiceDescriptor.Singleton<IBookmarkStateStore, GroundworkBookmarkStateStore>(); services.Add(descriptor);");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBookmarkStateStore", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_fails_closed_when_descriptor_types_flow_through_separate_statements()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "DependencyInjection/DescriptorDataflowRegistration.cs",
            "var serviceType = typeof(IBookmarkStateStore); var implementationType = typeof(GroundworkBookmarkStateStore); var descriptor = ServiceDescriptor.Singleton(serviceType, implementationType); services.Add(descriptor);");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBookmarkStateStore", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("new ServiceDescriptor(serviceType, implementationType, ServiceLifetime.Singleton)")]
    [InlineData("ServiceDescriptor.Describe(serviceType, implementationType, ServiceLifetime.Singleton)")]
    [InlineData("ServiceDescriptor.DescribeKeyed(serviceType, \"groundwork\", implementationType, ServiceLifetime.Singleton)")]
    [InlineData("ServiceDescriptor.KeyedSingleton(serviceType, \"groundwork\", implementationType)")]
    public void Scanner_fails_closed_for_nonfactory_descriptor_construction(string descriptorExpression)
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "DependencyInjection/NonFactoryDescriptorRegistration.cs",
            $"var serviceType = typeof(IBookmarkStateStore); var implementationType = typeof(GroundworkBookmarkStateStore); var descriptor = {descriptorExpression}; services.Add(descriptor);");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBookmarkStateStore", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_fails_closed_when_a_durable_registration_shape_cannot_be_parsed()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeContract("public interface IBookmarkStateStore { }");
        fixture.WriteRuntimeRegistration(
            "DependencyInjection/BypassRegistration.cs",
            "services.Add(ServiceDescriptor.Singleton<IBookmarkStateStore, GroundworkBookmarkStateStore>());");
        fixture.WriteRuntimeManifest("Unit(BookmarkStateDocumentKind, \"Bookmark\", [], [])");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
        Assert.Contains("IBookmarkStateStore", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_preserves_manifest_owner_instead_of_pooling_storage_units()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeManifest("Unit(SharedDocumentKind, \"Runtime\", [], [])");
        fixture.WriteIamManifest("Unit(SharedDocumentKind, \"IAM\", [], [])");

        var inventory = new GroundworkPersistenceInventoryScanner(fixture.Root).Scan();

        Assert.Equal(
            new[] { ManifestUnit("iam", "SharedDocumentKind"), ManifestUnit("runtime", "SharedDocumentKind") },
            inventory.ManifestStorageUnits.OrderBy(unit => unit.Owner, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Scanner_discovers_manifest_units_added_in_secondary_family_source_files()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeGroundworkSource(
            "Nested/AdditionalStorageManifest.cs",
            "public static StorageManifest Create() => new([Unit(SecondaryDocumentKind, \"Secondary\", [], [])]);");

        var inventory = new GroundworkPersistenceInventoryScanner(fixture.Root).Scan();

        Assert.Contains(
            ManifestUnit("runtime", "SecondaryDocumentKind"),
            inventory.ManifestStorageUnits);
    }

    [Fact]
    public void Scanner_fails_closed_when_a_manifest_declaration_shape_cannot_be_parsed()
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeGroundworkSource(
            "Nested/BypassStorageManifest.cs",
            "public const string BypassDocumentKind = \"bypass\"; private static StorageUnit Create() => CustomUnit(BypassDocumentKind);");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("manifest declaration", exception.Message, StringComparison.Ordinal);
        Assert.Contains("BypassDocumentKind", exception.Message, StringComparison.Ordinal);
        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unit(\"untracked\", \"Untracked\", [], [])")]
    [InlineData("new StorageUnitIdentity(\"untracked\")")]
    public void Scanner_fails_closed_on_literal_storage_unit_identities(string declaration)
    {
        using var fixture = ScannerFixture.Create();
        fixture.WriteRuntimeGroundworkSource(
            "Nested/LiteralStorageManifest.cs",
            $"public static StorageManifest Create() => {declaration};");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new GroundworkPersistenceInventoryScanner(fixture.Root).Scan());

        Assert.Contains("manifest declaration", exception.Message, StringComparison.Ordinal);
        Assert.Contains("could not be parsed safely", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Reconcile(
        JsonObject ledger,
        GroundworkPersistenceInventorySnapshot inventory) =>
        GroundworkPersistenceReconciler.CreateDefault().Reconcile(ledger, inventory);

    private static GroundworkPersistenceInventorySnapshot Inventory(
        IReadOnlyCollection<string>? durableContracts = null,
        IReadOnlyCollection<GroundworkPersistenceRegistrationSnapshot>? registrations = null,
        IReadOnlyCollection<GroundworkManifestStorageUnit>? manifestStorageUnits = null) =>
        new(
            durableContracts ?? [],
            registrations ?? [],
            manifestStorageUnits ?? []);

    private static GroundworkPersistenceRegistrationSnapshot Registration(
        string owner,
        string contract,
        string implementation,
        params string[] storageUnits) =>
        new(owner, contract, implementation, storageUnits);

    private static GroundworkManifestStorageUnit ManifestUnit(string owner, string storageUnit) =>
        new(owner, storageUnit);

    private static JsonObject ReadLedger() =>
        JsonNode.Parse(File.ReadAllText(LedgerPath))?.AsObject()
        ?? throw new InvalidOperationException($"Coverage ledger '{LedgerPath}' is empty.");

    private static IEnumerable<JsonObject> Entries(JsonObject ledger) =>
        ledger["entries"]?.AsArray().Select(node => node?.AsObject()
            ?? throw new InvalidOperationException("Coverage ledger contains a null entry."))
        ?? throw new InvalidOperationException("Coverage ledger has no entries array.");

    private static JsonObject Entry(JsonObject ledger, string id) =>
        Entries(ledger).Single(entry => EntryIdOf(entry) == id);

    private static string EntryIdOf(JsonObject entry) =>
        entry["id"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Coverage ledger entry has no id.");

    private static string LedgerPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "coverage-ledger.json");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private sealed class ScannerFixture : IDisposable
    {
        private static readonly string[] RequiredFiles =
        [
            "src/Elsa/Workflows/Runtime/Core/Contracts/Stores.cs",
            "src/Elsa/Foundation/Identity/Abstractions/Iam/IamContracts.cs",
            "src/Elsa/Secrets/Core/Contracts/ISecretRepository.cs",
            "src/Elsa/Workflows/Runtime/Distributed/Contracts/Stores.cs",
            "src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs",
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs",
            "src/Elsa/Secrets/Persistence/Groundwork/SecretsStorageManifest.cs",
            "src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/DistributedGroundworkStorageManifest.cs"
        ];

        private ScannerFixture(string root) => Root = root;

        public string Root { get; }

        public static ScannerFixture Create()
        {
            var fixture = new ScannerFixture(Path.Combine(Path.GetTempPath(), $"elsa-groundwork-inventory-{Guid.NewGuid():N}"));
            foreach (var file in RequiredFiles)
                fixture.Write(file, string.Empty);
            return fixture;
        }

        public void WriteRuntimeContract(string source) =>
            Write("src/Elsa/Workflows/Runtime/Core/Contracts/Stores.cs", source);

        public void WriteRuntimeRegistration(string relativePath, string source) =>
            Write($"src/Elsa/Persistence/Groundwork/{relativePath}", $"public static void Register(IServiceCollection services) {{ {source} }}");

        public void WriteRuntimeGroundworkSource(string relativePath, string source) =>
            Write($"src/Elsa/Persistence/Groundwork/{relativePath}", source);

        public void WriteRuntimeManifest(string source) =>
            Write("src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs", source);

        public void WriteIamManifest(string source) =>
            Write("src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityStorageManifest.cs", source);

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private void Write(string relativePath, string source)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }
    }
}
