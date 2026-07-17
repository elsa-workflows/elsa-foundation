using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityContractAuthoringCapabilityTests
{
    [Fact]
    public void Mutable_contract_validation_rejects_alias_collection_driver_and_nullability_outside_the_catalog()
    {
        var validator = new ActivityContractAuthoringValidator(new Catalog([
            new(
                "String",
                "String",
                "Primitives",
                "text",
                new HashSet<CollectionKind> { CollectionKind.Single },
                SupportsNull: true,
                SupportsDurability: true,
                new HashSet<string>(StringComparer.Ordinal) { "elsa.json" }),
            new(
                "Int32",
                "Integer",
                "Primitives",
                "number",
                new HashSet<CollectionKind> { CollectionKind.Single },
                SupportsNull: false,
                SupportsDurability: true,
                new HashSet<string>(StringComparer.Ordinal) { "elsa.json" })
        ]));
        var subject = new ActivityDiagnosticSubject("ActivityDraft", "draft-1", "definition-1", Revision: 3);
        var contract = new ActivityContract(
            "1",
            [
                Input("unknown", "Unknown", CollectionKind.Single, "elsa.json"),
                Input("list", "String", CollectionKind.List, "elsa.json"),
                Input("driver", "String", CollectionKind.Single, "custom.driver"),
                Input("null", "Int32", CollectionKind.Single, "elsa.json", Json("null"))
            ],
            [],
            []);

        var diagnostics = validator.Validate(contract, subject);

        Assert.Equal([
            "activity.contract.collection-kind-unavailable",
            "activity.contract.null-default-unsupported",
            "activity.contract.storage-driver-unavailable",
            "activity.contract.type-unavailable"
        ], diagnostics.Select(x => x.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Generated_activity_type_keys_are_stable_collision_safe_and_do_not_require_unique_display_names()
    {
        var policy = new DefaultActivityTypeKeyPolicy();

        var first = policy.Generate("Calculate Total", "activity-def-a1");
        var second = policy.Generate("Calculate Total", "activity-def-b2");

        Assert.Equal("elsa.user.calculate-total.activity-def-a1", first);
        Assert.Equal("elsa.user.calculate-total.activity-def-b2", second);
        Assert.NotEqual(first, second);
        Assert.Matches(policy.Rules.Pattern, first);
        Assert.True(policy.Rules.ServerGenerated);
        Assert.True(policy.Rules.Immutable);
    }

    [Fact]
    public void Activity_type_key_override_is_normalized_by_the_authoritative_policy()
    {
        var policy = new DefaultActivityTypeKeyPolicy();

        var normalized = policy.NormalizeAndValidateOverride("  ELSA.USER.Invoice-TOTAL.Activity-DEF  ");

        Assert.Equal("elsa.user.invoice-total.activity-def", normalized);
    }

    [Fact]
    public void Activity_type_key_override_rejects_values_outside_the_advertised_rules()
    {
        var policy = new DefaultActivityTypeKeyPolicy();
        var invalid = new[]
        {
            "",
            "elsa.other.invoice-total.activity-def",
            "elsa.user.invoice_total.activity-def",
            $"elsa.user.{new string('a', policy.Rules.MaximumLength)}.activity-def"
        };

        foreach (var activityTypeKey in invalid)
            Assert.Throws<ArgumentException>(() => policy.NormalizeAndValidateOverride(activityTypeKey));
    }

    [Fact]
    public async Task Capability_snapshot_is_authorization_safe_deterministic_and_includes_provider_constraints()
    {
        var catalog = new Catalog([
            new(
                "String",
                "String",
                "Primitives",
                "text",
                new HashSet<CollectionKind> { CollectionKind.Single, CollectionKind.List },
                true,
                true,
                new HashSet<string>(StringComparer.Ordinal) { "elsa.json" })
        ]);
        var registry = new ActivityProviderRegistry([new Provider("allowed.provider"), new Provider("hidden.provider")]);
        var handler = new GetActivityAuthoringCapabilitiesHandler(registry, catalog, new DefaultActivityTypeKeyPolicy(), new Context());

        var first = await handler.Handle(new GetActivityAuthoringCapabilities(), default);
        var second = await handler.Handle(new GetActivityAuthoringCapabilities(), default);

        var provider = Assert.Single(first.Providers);
        Assert.Equal("allowed.provider", provider.ProviderKey);
        Assert.Equal("done", Assert.Single(provider.RequiredOutcomes).ReferenceKey);
        Assert.Equal(["elsa.json"], first.StorageDriverKeys);
        Assert.True(first.ActivityTypeKeyRules.AllowsPreCreationOverride);
        Assert.Equal(first.SnapshotFingerprint, second.SnapshotFingerprint);
    }

    [Fact]
    public void Provider_registry_rejects_authoring_metadata_that_does_not_match_supported_schemas()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ActivityProviderRegistry([new InvalidMetadataProvider()]));

        Assert.Contains("must exactly match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_catalog_excludes_declared_storage_drivers_that_are_not_activated()
    {
        var descriptor = new TypeDescriptor(
            "String",
            typeof(string),
            "String",
            "Primitives",
            "text",
            new HashSet<CollectionKind> { CollectionKind.Single },
            true,
            true,
            new HashSet<string>(StringComparer.Ordinal) { "elsa.json", "missing.driver" });
        var catalog = new ExpressionActivityContractCapabilityCatalog(
            new TypeCatalog([descriptor]),
            [new StorageDrivers(["elsa.json"])]);

        var capability = Assert.Single(catalog.Types);

        Assert.Equal(["elsa.json"], capability.CompatibleStorageDriverKeys);
        var diagnostics = new ActivityContractAuthoringValidator(catalog).Validate(
            new("1", [Input("value", "String", CollectionKind.Single, "missing.driver")], [], []),
            new("ActivityDraft", "draft-1"));
        Assert.Contains(diagnostics, x => x.Code == "activity.contract.storage-driver-unavailable");
    }

    [Fact]
    public async Task Draft_validation_rejects_readable_but_non_authorable_provider_schema()
    {
        var provider = new NonAuthorableProvider();
        var validator = new ActivityDraftValidator(
            new ActivityProviderRegistry([provider]),
            new ActivityContractAuthoringValidator(new Catalog([])),
            TimeProvider.System);

        var result = await validator.ValidateAsync(new(
            "definition-1",
            "draft-1",
            2,
            new(new("1", [], [], []), new(provider.ProviderKey, "1", Json("{}")), new Dictionary<string, string>())), default);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "activity.provider.schema-not-authorable");
        Assert.Equal(0, provider.ValidationCalls);
    }

    private static ActivityInputContract Input(
        string referenceKey,
        string alias,
        CollectionKind collectionKind,
        string storageDriver,
        JsonElement? defaultValue = null) => new(
        referenceKey,
        referenceKey,
        new(alias, collectionKind),
        false,
        defaultValue is null ? null : new("Literal", defaultValue.Value),
        storageDriver);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class Catalog(IReadOnlyCollection<ActivityContractTypeCapability> types)
        : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => types;
    }

    private sealed class TypeCatalog(IReadOnlyCollection<TypeDescriptor> types) : IVariableTypeDescriptorCatalog
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() => types;
        public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => types.GroupBy(x => x.Category);
    }

    private sealed class StorageDrivers(IReadOnlyCollection<string> driverKeys) : IActivityContractStorageDriverProvider
    {
        public IReadOnlyCollection<string> DriverKeys => driverKeys;
    }

    private sealed class Context : IActivityAuthoringContext
    {
        public string? TenantId => "tenant-a";
        public string AuthorizationProfile => "tenant-a/allowed-provider";
        public bool CanAuthorProvider(string providerKey) => StringComparer.Ordinal.Equals(providerKey, "allowed.provider");
        public bool CanReadProviderPayload(string providerKey) => false;
    }

    private sealed class Provider(string key) : IActivityProvider
    {
        public string ProviderKey => key;
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            key,
            [new("1", true, new HashSet<string> { "1" })],
            new([new("done", "Done", true)]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal(
                [new("outcome:add:done", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "done", Outcome: new("done", "Done", true))],
                []));

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));
    }

    private sealed class InvalidMetadataProvider : ProviderBase
    {
        public override ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Invalid",
            [new("2", true, new HashSet<string> { "1" })],
            new([]));
    }

    private sealed class NonAuthorableProvider : IActivityProvider
    {
        public int ValidationCalls { get; private set; }
        public string ProviderKey => "historical.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Historical", [new("1", false, new HashSet<string>())], new([]));
        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityContractProposal([], []));
        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default)
        {
            ValidationCalls++;
            return ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
        }
        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityManifestMigration(null, []));
    }

    private abstract class ProviderBase : IActivityProvider
    {
        public string ProviderKey => "invalid.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public abstract ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; }
        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityContractProposal([], []));
        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityManifestMigration(null, []));
    }
}
