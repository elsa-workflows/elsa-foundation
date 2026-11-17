using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Activities.Design.Tests.ClrIconFixture;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivitySerializedShapeSourceTests
{
    [Fact]
    public async Task Nullable_enum_converter_is_opaque_for_whole_and_property_metadata_without_constructing_the_converter()
    {
        ObjectEnumConverter.ResetConstructionCount();
        using var whole = ResultHost(typeof(NullableConvertedEnumResultActivity));
        var root = (await whole.Source.DescribeResultAsync(whole.Version<NullableConvertedEnumResultActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Unknown, root.State);
        Assert.Equal(ActivitySerializedValueKind.Unknown, root.Kind);
        Assert.Equal("activity.shape.converter-opaque", root.ReasonCode);
        Assert.Empty(root.Members);

        using var properties = ResultHost(typeof(NullableEnumPropertiesResultActivity));
        var version = properties.Version<NullableEnumPropertiesResultActivity>();
        var payload = (await properties.Source.DescribeResultAsync(version, "$result", [], new()))!;
        var property = Member(payload, "converted");
        Assert.Equal(ActivitySerializedShapeState.Unknown, property.State);
        Assert.Equal(ActivitySerializedValueKind.Unknown, property.Kind);
        Assert.Equal("activity.shape.converter-opaque", property.ReasonCode);
        Assert.True(property.IsNullable);
        var selected = (await properties.Source.DescribeResultAsync(version, "$result", ["converted"], new()))!;
        Assert.Equal("activity.shape.converter-opaque", selected.ReasonCode);
        Assert.Equal(ActivitySerializedShapeState.Unknown, selected.State);
        var nested = (await properties.Source.DescribeResultAsync(version, "$result", ["converted", "state"], new()))!;
        Assert.Equal("activity.shape.converter-opaque", nested.ReasonCode);
        Assert.Null(nested.Type);
        Assert.Equal(0, ObjectEnumConverter.ConstructionCount);
    }

    [Fact]
    public async Task Plain_nullable_enum_remains_a_known_scalar_for_whole_and_property_metadata()
    {
        using var whole = ResultHost(typeof(NullablePlainEnumResultActivity));
        var root = (await whole.Source.DescribeResultAsync(whole.Version<NullablePlainEnumResultActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Known, root.State);
        Assert.Equal(ActivitySerializedValueKind.Scalar, root.Kind);
        using var properties = ResultHost(typeof(NullableEnumPropertiesResultActivity));
        var selected = (await properties.Source.DescribeResultAsync(properties.Version<NullableEnumPropertiesResultActivity>(), "$result", ["plain"], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Known, selected.State);
        Assert.Equal(ActivitySerializedValueKind.Scalar, selected.Kind);
        Assert.True(selected.IsNullable);
    }

    [Fact]
    public void Actual_activity_profile_uses_the_underlying_enum_converter_for_nullable_whole_and_property_values()
    {
        ObjectEnumConverter.ResetConstructionCount();
        var options = ActivityValueJsonProfile.CreateOptions();
        var whole = JsonSerializer.SerializeToElement<ConvertedEnum?>(ConvertedEnum.Ready, options);
        Assert.Equal(JsonValueKind.Object, whole.ValueKind);
        Assert.Equal("ready", whole.GetProperty("state").GetString());
        var payload = JsonSerializer.SerializeToElement(new NullableEnumProperties { Converted = ConvertedEnum.Ready, Plain = PlainEnum.Ready }, options);
        Assert.Equal(JsonValueKind.Object, payload.GetProperty("converted").ValueKind);
        Assert.Equal("ready", payload.GetProperty("converted").GetProperty("state").GetString());
        Assert.Equal(7, payload.GetProperty("plain").GetInt32());
        Assert.Equal(7, JsonSerializer.SerializeToElement<PlainEnum?>(PlainEnum.Ready, options).GetInt32());
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement<ConvertedEnum?>(null, options).ValueKind);
        Assert.True(ObjectEnumConverter.ConstructionCount > 0);
    }

    [Fact]
    public async Task Typed_whole_result_without_output_rows_has_literal_serialized_members_and_unchanged_catalog_hash()
    {
        using var host = new Host();
        var version = host.Version<SerializedCustomerFixtureActivity>();
        var originalHash = version.Hash;
        Assert.Empty(version.Outputs);

        var shape = await host.Source.DescribeResultAsync(version, "$result", [], new());
        Assert.NotNull(shape);
        Assert.Equal(ActivitySerializedShapeState.Known, shape.State);
        Assert.Equal(ActivitySerializedValueKind.Object, shape.Kind);
        Assert.Equal(ValueRepresentation.TypedValue, shape.Representation);
        Assert.False(shape.IsNullable);
        Assert.Contains(shape.Members, x => x.SerializedName == "customer.id");
        Assert.Contains(shape.Members, x => x.SerializedName == "");
        Assert.DoesNotContain(shape.Members, x => x.SerializedName is "hidden" or "publicField");
        Assert.False(Member(shape, "nonNullableRead").IsNullable); // AllowNull changes WriteState only.
        Assert.True(Member(shape, "nullableRead").IsNullable); // MaybeNull changes ReadState only.
        Assert.Equal(ActivitySerializedPresence.Optional, Member(shape, "defaultCount").Presence);
        Assert.Null(shape.IsSensitive);
        Assert.Null(shape.IsCredential);
        Assert.Equal(ActivityValueJsonProfile.Revision, shape.ProfileRevision);
        Assert.StartsWith("sha256:", shape.SourceContractRevision);
        Assert.StartsWith("sha256:", shape.MetadataRevision);
        Assert.Equal(originalHash, new DefaultActivityDefinitionHasher().Hash(version.Definition, version));
        var serialized = JsonSerializer.SerializeToElement(new SerializedCustomer(), ActivityValueJsonProfile.CreateOptions());
        Assert.All(serialized.EnumerateObject(), property => Assert.Contains(shape.Members, member => member.SerializedName == property.Name));
        Assert.False(serialized.TryGetProperty("hidden", out _));
        Assert.False(serialized.TryGetProperty("defaultCount", out _));

        var id = await host.Source.DescribeResultAsync(version, "$result", ["customer.id"], new());
        Assert.Equal(new TypeReference("Guid"), id!.Type);
        Assert.Equal(ActivitySerializedShapeState.Known, id.State);
        var empty = await host.Source.DescribeResultAsync(version, "$result", [""], new());
        Assert.Equal(new TypeReference("String"), empty!.Type);
        var split = await host.Source.DescribeResultAsync(version, "$result", ["customer", "id"], new());
        Assert.Equal("activity.shape.member-not-declared", split!.ReasonCode);
    }

    [Fact]
    public async Task Member_query_keeps_optional_nullable_prefixes_and_collection_map_contracts()
    {
        using var host = new Host();
        var version = host.Version<SerializedCustomerFixtureActivity>();
        var root = (await host.Source.DescribeResultAsync(version, "$result", [], new()))!;
        var addresses = Member(root, "addresses");
        Assert.Equal(CollectionKind.List, addresses.Type!.CollectionKind);
        Assert.Equal(ActivitySerializedValueKind.Collection, addresses.Kind);
        Assert.Equal(ValueRepresentation.StructuredValue, addresses.Representation);
        Assert.NotNull(addresses.Element);
        Assert.Equal(CollectionKind.HashSet, Member(root, "tags").Type!.CollectionKind);
        Assert.Equal(ActivitySerializedValueKind.Map, Member(root, "addressByKey").Kind);

        var optional = (await host.Source.DescribeResultAsync(version, "$result", ["optionalAddress", "city"], new()))!;
        Assert.Equal(ActivitySerializedPresence.Optional, optional.Presence);
        Assert.False(optional.IsNullable); // Null is omitted at this prefix, not emitted as a null member.
        var nullable = (await host.Source.DescribeResultAsync(version, "$result", ["nullableAddress", "city"], new()))!;
        Assert.True(nullable.IsNullable);
        var keyed = (await host.Source.DescribeResultAsync(version, "$result", ["addressByKey", "Paris.London", "city"], new()))!;
        Assert.Equal(ActivitySerializedPresence.Optional, keyed.Presence);
        Assert.True(keyed.IsNullable);
        var indexed = (await host.Source.DescribeResultAsync(version, "$result", ["addresses", "0", "city"], new()))!;
        Assert.Equal("activity.shape.member-parent-not-object", indexed.ReasonCode);
    }

    [Fact]
    public async Task Override_JSON_attributes_follow_the_actual_declaration_instead_of_inheriting_base_policy()
    {
        using var host = ResultHost(typeof(OverrideResultActivity));
        var version = host.Version<OverrideResultActivity>();
        var shape = (await host.Source.DescribeResultAsync(version, "$result", [], new()))!;
        var json = JsonSerializer.SerializeToElement(new SerializedOverridePayload {
            Name = "Ada", Optional = null, Removed = "not emitted", Hidden = "not emitted"
        }, ActivityValueJsonProfile.CreateOptions());
        Assert.Equal(ActivitySerializedShapeState.Known, shape.State);
        Assert.True(json.TryGetProperty("name", out _));
        Assert.True(json.TryGetProperty("optional", out var nullable));
        Assert.Equal(JsonValueKind.Null, nullable.ValueKind);
        Assert.False(json.TryGetProperty("removed", out _));
        Assert.False(json.TryGetProperty("hidden", out _));
        Assert.Equal(ActivitySerializedPresence.Required, Member(shape, "optional").Presence);
        Assert.True(Member(shape, "optional").IsNullable);
        var selected = (await host.Source.DescribeResultAsync(version, "$result", ["name"], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Known, selected.State);
        Assert.Equal(new TypeReference("String"), selected.Type);
        foreach (var ignored in new[] { "removed", "hidden" })
            Assert.Equal("activity.shape.member-not-declared", (await host.Source.DescribeResultAsync(version, "$result", [ignored], new()))!.ReasonCode);
    }

    [Theory]
    [InlineData(typeof(RenamedOverrideResultActivity))]
    [InlineData(typeof(RenamedShadowResultActivity))]
    [InlineData(typeof(IgnoredShadowResultActivity))]
    public async Task Unsupported_override_or_shadow_serialized_name_collisions_stay_unknown(Type activityType)
    {
        using var host = ResultHost(activityType);
        var shape = (await host.Source.DescribeResultAsync(host.Version(activityType), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Unknown, shape.State);
        Assert.Equal("activity.shape.override-contract-unsupported", shape.ReasonCode);
        Assert.Empty(shape.Members);
    }

    [Fact]
    public async Task Named_projection_is_bound_to_the_exact_source_version_and_output_identity()
    {
        using var host = new Host();
        var version = host.Version<UnannotatedFixtureActivity>();
        var output = Assert.Single(version.Outputs);
        var shape = await host.Source.DescribeResultAsync(version, output.ReferenceKey, [], new());
        Assert.Equal(new TypeReference("String"), shape!.Type);
        Assert.Equal(ActivitySerializedShapeState.Known, shape.State);
        Assert.Equal(ActivitySerializedPresence.Required, shape.Presence);

        var missing = await host.Source.DescribeResultAsync(version, "not-an-output", [], new());
        Assert.Equal("activity.shape.projection-not-declared", missing!.ReasonCode);
        var stale = version with { Hash = "not-the-current-projection" };
        Assert.Equal("activity.shape.source-contract-mismatch", (await host.Source.DescribeResultAsync(stale, "$result", [], new()))!.ReasonCode);
        Assert.Null(await host.Source.DescribeResultAsync(version with { SourceId = "another-source" }, "$result", [], new()));
        Assert.Equal(ActivitySerializedShapeState.Unavailable,
            (await host.Source.DescribeResultAsync(version with { ConsumerSchemaVersion = "unsupported" }, "$result", [], new()))!.State);
    }

    [Fact]
    public async Task Declared_type_admission_retains_whole_type_without_inventing_a_serialized_profile_or_selected_type()
    {
        using var host = new Host([Descriptor("shop.address", typeof(SerializedAddress))]);
        var reference = new TypeReference("shop.address", CollectionKind.Dictionary);
        var shape = await host.Source.DescribeDeclaredTypeAsync(reference, "workflow:draft:revision:input", [], new());
        Assert.Equal(reference, shape!.Type);
        Assert.Equal(ActivitySerializedShapeState.Unknown, shape.State);
        Assert.Equal("activity.shape.serialization-profile-unbound", shape.ReasonCode);
        Assert.Equal(ActivitySerializedValueKind.Unknown, shape.Kind);
        Assert.Empty(shape.Members);
        Assert.Null(shape.MapValue);
        Assert.Null(shape.IsNullable);
        Assert.Equal(ActivitySerializedPresence.Unknown, shape.Presence);
        Assert.Empty(shape.ProfileRevision);
        Assert.Equal("workflow:draft:revision:input", shape.SourceContractRevision);
        var selected = (await host.Source.DescribeDeclaredTypeAsync(reference, "owner", ["any.key", "city"], new()))!;
        Assert.Null(selected.Type);
        Assert.Null(selected.IsNullable);
        Assert.Equal(ActivitySerializedPresence.Unknown, selected.Presence);
        var unknown = await host.Source.DescribeDeclaredTypeAsync(new(typeof(MetadataOnlyPayload).FullName!), "owner", [], new());
        Assert.Equal("activity.shape.type-not-admitted", unknown!.ReasonCode);
        Assert.Equal(0, host.RuntimeRegistryResolutionCount);
    }

    [Fact]
    public async Task Discovery_never_constructs_a_dto_attribute_getter_or_converter_and_reports_opaque_contracts()
    {
        using var host = ResultHost(typeof(MetadataOnlyResultActivity));
        var safe = (await host.Source.DescribeResultAsync(host.Version<MetadataOnlyResultActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Known, safe.State);
        Assert.Contains(safe.Members, x => x.SerializedName == "throwsOnRead");
        foreach (var (activityType, reason) in new[]
                 {
                     (typeof(ConverterResultActivity), "activity.shape.converter-opaque"), (typeof(ExtensionResultActivity), "activity.shape.extension-data-opaque"),
                     (typeof(IncludeResultActivity), "activity.shape.include-unsupported"), (typeof(PolymorphicResultActivity), "activity.shape.polymorphic-opaque"),
                     (typeof(JsonResultActivity), "activity.shape.dynamic-json")
                 })
        {
            using var opaque = ResultHost(activityType);
            var shape = (await opaque.Source.DescribeResultAsync(opaque.Version(activityType), "$result", [], new()))!;
            Assert.Equal(ActivitySerializedShapeState.Unknown, shape.State);
            Assert.Equal(reason, shape.ReasonCode);
            Assert.Empty(shape.Members);
        }
        using var propertyHost = ResultHost(typeof(PropertyConverterResultActivity));
        var property = await propertyHost.Source.DescribeResultAsync(propertyHost.Version<PropertyConverterResultActivity>(), "$result", ["value", "anything"], new());
        Assert.Equal("activity.shape.converter-opaque", property!.ReasonCode);
        Assert.True(property.IsNullable);

        using var convertedEnum = ResultHost(typeof(ConvertedNullableEnumResultActivity));
        var converted = (await convertedEnum.Source.DescribeResultAsync(convertedEnum.Version<ConvertedNullableEnumResultActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Unknown, converted.State);
        Assert.Equal("activity.shape.converter-opaque", converted.ReasonCode);
        using var holderHost = ResultHost(typeof(NullableEnumHolderActivity));
        var holder = (await holderHost.Source.DescribeResultAsync(holderHost.Version<NullableEnumHolderActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedShapeState.Known, holder.State);
        Assert.Equal(ActivitySerializedValueKind.Scalar, Member(holder, "plain").Kind);
        Assert.True(Member(holder, "plain").IsNullable);
        Assert.Equal("activity.shape.converter-opaque", Member(holder, "converted").ReasonCode);
    }

    [Fact]
    public async Task Member_budget_is_global_and_recursive_depth_is_bounded()
    {
        using var host = ResultHost(typeof(RecursiveResultActivity));
        var version = host.Version<RecursiveResultActivity>();
        var bounded = (await host.Source.DescribeResultAsync(version, "$result", [], new(3, 5)))!;
        Assert.Equal(ActivitySerializedShapeState.Truncated, bounded.State);
        Assert.InRange(CountMembers(bounded), 0, 5);
        Assert.InRange(Depth(bounded), 0, 3);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await host.Source.DescribeResultAsync(version, "$result", [], new(), cancellation.Token));
    }

    [Fact]
    public async Task Default_headless_projection_has_API_byte_parity_and_host_replacement_wins()
    {
        var descriptors = new[] { Descriptor("shop.address", typeof(SerializedAddress)) };
        using var host = new Host(descriptors);
        var api = new ExpressionActivityContractCapabilityCatalog(new Types(descriptors), [new Drivers()]);
        Assert.Equal(JsonSerializer.Serialize(api.Types), JsonSerializer.Serialize(host.Capabilities.Types));
        using var blocked = new Host(descriptors, new Capabilities([]));
        Assert.Same(blocked.Replacement, blocked.Capabilities);
        var denied = await blocked.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", [], new());
        Assert.Equal("activity.shape.type-not-admitted", denied!.ReasonCode);
    }

    [Fact]
    public async Task Ambiguous_or_case_changed_catalog_aliases_cannot_authorize_a_CLR_shape()
    {
        var descriptor = Descriptor("shop.address", typeof(SerializedAddress));
        using var ambiguous = new Host([descriptor, descriptor]);
        var duplicate = await ambiguous.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", [], new());
        Assert.Equal(ActivitySerializedShapeState.Unknown, duplicate!.State);
        using var exact = new Host([descriptor]);
        var wrongCase = await exact.Source.DescribeDeclaredTypeAsync(new("SHOP.ADDRESS"), "owner", [], new());
        Assert.Equal("activity.shape.type-not-admitted", wrongCase!.ReasonCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task API_and_endpoint_free_hosts_share_the_same_type_facts_in_either_registration_order(bool apiFirst)
    {
        var descriptors = new[] { Descriptor("shop.address", typeof(SerializedAddress)) };
        using var headless = new Host(descriptors);
        using var combined = new Host(descriptors, apiBeforeMetadataFeature: apiFirst);
        Assert.Equal(JsonSerializer.Serialize(headless.Capabilities.Types), JsonSerializer.Serialize(combined.Capabilities.Types));
        var left = await headless.Source.DescribeDeclaredTypeAsync(new("shop.address"), "same-owner", [], new());
        var right = await combined.Source.DescribeDeclaredTypeAsync(new("shop.address"), "same-owner", [], new());
        Assert.Equal(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right));
        var leftResult = await headless.Source.DescribeResultAsync(headless.Version<SerializedCustomerFixtureActivity>(), "$result", [], new());
        var rightResult = await combined.Source.DescribeResultAsync(combined.Version<SerializedCustomerFixtureActivity>(), "$result", [], new());
        Assert.Equal(ActivitySerializedShapeState.Known, leftResult!.State);
        Assert.Equal(JsonSerializer.Serialize(leftResult), JsonSerializer.Serialize(rightResult));
    }

    [Fact]
    public async Task Changing_CLR_binding_behind_selectable_alias_does_not_establish_a_serialized_profile()
    {
        using var first = new Host([Descriptor("shop.payload", typeof(SerializedAddress))]);
        using var second = new Host([Descriptor("shop.payload", typeof(SerializedCustomer))]);
        Assert.Equal(JsonSerializer.Serialize(first.Capabilities.Types), JsonSerializer.Serialize(second.Capabilities.Types));
        var left = (await first.Source.DescribeDeclaredTypeAsync(new("shop.payload"), "same-owner", [], new()))!;
        var right = (await second.Source.DescribeDeclaredTypeAsync(new("shop.payload"), "same-owner", [], new()))!;
        Assert.Equal(left.SourceContractRevision, right.SourceContractRevision);
        Assert.Equal(ActivitySerializedShapeState.Unknown, left.State);
        Assert.Equal(ActivitySerializedShapeState.Unknown, right.State);
        Assert.Equal("activity.shape.serialization-profile-unbound", left.ReasonCode);
        Assert.Empty(left.Members);
        Assert.Empty(right.Members);
        Assert.Equal(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right));
    }

    [Fact]
    public async Task Default_CLR_activity_profile_and_preserved_caller_JSON_keys_do_not_share_member_name_authority()
    {
        using var host = new Host([Descriptor("shop.address", typeof(SerializedAddress))]);
        var value = new SerializedAddress { City = "Paris" };
        var defaultJson = JsonSerializer.SerializeToElement(value);
        var activityJson = JsonSerializer.SerializeToElement(value, ActivityValueJsonProfile.CreateOptions());
        using var caller = JsonDocument.Parse("{\"CITY\":\"Paris\"}");
        Assert.True(defaultJson.TryGetProperty("City", out _));
        Assert.True(activityJson.TryGetProperty("city", out _));
        Assert.True(caller.RootElement.Clone().TryGetProperty("CITY", out _));
        foreach (var path in new[] { "City", "city", "CITY" })
        {
            var selection = (await host.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", [path], new()))!;
            Assert.Equal("activity.shape.serialization-profile-unbound", selection.ReasonCode);
            Assert.Null(selection.Type);
            Assert.Empty(selection.Members);
        }
    }

    [Fact]
    public async Task Definitive_absence_of_typed_result_is_distinct_from_missing_metadata_authority()
    {
        using var host = ResultHost(typeof(UntypedActivity));
        var version = host.Version<UntypedActivity>();
        var absent = (await host.Source.DescribeResultAsync(version, "$result", [], new()))!;
        Assert.Equal("activity.shape.no-typed-result", absent.ReasonCode);
        Assert.StartsWith("sha256:", absent.MetadataRevision);
        var unavailable = (await host.Source.DescribeResultAsync(version with { Hash = "stale" }, "$result", [], new()))!;
        Assert.Equal("activity.shape.source-contract-mismatch", unavailable.ReasonCode);
    }

    [Fact]
    public async Task Profile_binary_JSON_scalar_keeps_array_type_reference_and_binary_representation()
    {
        using var host = ResultHost(typeof(BinaryResultActivity));
        var shape = (await host.Source.DescribeResultAsync(host.Version<BinaryResultActivity>(), "$result", [], new()))!;
        Assert.Equal(ActivitySerializedValueKind.Scalar, shape.Kind);
        Assert.Equal(CollectionKind.Array, shape.Type!.CollectionKind);
        Assert.Equal(ValueRepresentation.BinaryContent, shape.Representation);
        Assert.Null(shape.Element);
        Assert.Equal(JsonValueKind.String, JsonSerializer.SerializeToElement(new byte[] { 1, 2 }, ActivityValueJsonProfile.CreateOptions()).ValueKind);
    }

    [Fact]
    public async Task Inherited_member_metadata_asset_enters_the_revision_even_with_unchanged_assembly_identity()
    {
        var descriptor = new ClrActivityDescriptor(typeof(InheritedResultActivity).FullName!);
        var current = new ActivityVersionReconciliationModel(null, "1.0.0", descriptor.TypeAlias, "Inherited result", "Tests", null,
            "elsa.clr-activity", "1", "elsa.clr-activity", "1", descriptor, [], [], []);
        using var host = new Host(scanner: new FixedScanner(current));
        host.CopyMetadataAssembly(typeof(InheritedResultActivity).Assembly);
        var baseAsset = host.CopyMetadataAssembly(typeof(SerializedShapeBasePayload).Assembly);
        var version = host.Version<InheritedResultActivity>();
        var before = (await host.Source.DescribeResultAsync(version, "$result", [], new()))!;
        Assert.Contains(before.Members, x => x.SerializedName == "inherited.id");
        // A private copied PE overlay preserves assembly identity and metadata, but changes this
        // consulted asset's content. It cannot alter the running test assembly or another test's files.
        using (var overlay = new FileStream(baseAsset, FileMode.Append, FileAccess.Write))
            overlay.WriteByte(0);
        var after = (await host.Source.DescribeResultAsync(version, "$result", [], new()))!;
        Assert.Equal(before.SourceContractRevision, after.SourceContractRevision);
        Assert.Equal(before.Members.Select(x => x.SerializedName), after.Members.Select(x => x.SerializedName));
        Assert.NotEqual(before.MetadataRevision, after.MetadataRevision);
    }

    public sealed class InheritedResult : SerializedShapeBasePayload;
    public sealed class InheritedResultActivity : Activity<InheritedResult>
    {
        protected override ValueTask<ActivityTransition<InheritedResult>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new InvalidOperationException("Metadata discovery cannot execute an activity.");
    }

    public abstract class MetadataResultActivity<T> : Activity<T>
    {
        protected override ValueTask<ActivityTransition<T>> ExecuteAsync(ActivityExecutionContext context) =>
            throw new InvalidOperationException("Metadata discovery cannot execute an activity.");
    }
    public sealed class MetadataOnlyResultActivity : MetadataResultActivity<MetadataOnlyPayload>;
    public sealed class ConverterResultActivity : MetadataResultActivity<ConverterPayload>;
    public sealed class ConvertedNullableEnumResultActivity : MetadataResultActivity<ConvertedEnum?>;
    public sealed class NullableEnumHolderActivity : MetadataResultActivity<NullableEnumHolder>;
    public sealed class PropertyConverterResultActivity : MetadataResultActivity<PropertyConverterPayload>;
    public sealed class ExtensionResultActivity : MetadataResultActivity<ExtensionDataPayload>;
    public sealed class IncludeResultActivity : MetadataResultActivity<IncludedFieldPayload>;
    public sealed class PolymorphicResultActivity : MetadataResultActivity<PolymorphicPayload>;
    public sealed class JsonResultActivity : MetadataResultActivity<JsonElement>;
    public sealed class RecursiveResultActivity : MetadataResultActivity<SerializedRecursive>;
    public sealed class BinaryResultActivity : MetadataResultActivity<byte[]>;
    public sealed class OverrideResultActivity : MetadataResultActivity<SerializedOverridePayload>;
    public sealed class RenamedOverrideResultActivity : MetadataResultActivity<SerializedRenamedOverride>;
    public sealed class RenamedShadowResultActivity : MetadataResultActivity<SerializedRenamedShadow>;
    public sealed class IgnoredShadowResultActivity : MetadataResultActivity<SerializedIgnoredShadow>;
    public sealed class NullableConvertedEnumResultActivity : MetadataResultActivity<ConvertedEnum?>;
    public sealed class NullablePlainEnumResultActivity : MetadataResultActivity<PlainEnum?>;
    public sealed class NullableEnumPropertiesResultActivity : MetadataResultActivity<NullableEnumProperties>;
    [JsonConverter(typeof(ObjectEnumConverter))]
    public enum ConvertedEnum { Ready = 7 }
    public enum PlainEnum { Ready = 7 }
    public sealed class NullableEnumProperties
    {
        public ConvertedEnum? Converted { get; set; }
        public PlainEnum? Plain { get; set; }
    }
    public sealed class ObjectEnumConverter : JsonConverter<ConvertedEnum>
    {
        private static int _constructionCount;
        public ObjectEnumConverter() => Interlocked.Increment(ref _constructionCount);
        public static int ConstructionCount => Volatile.Read(ref _constructionCount);
        public static void ResetConstructionCount() => Interlocked.Exchange(ref _constructionCount, 0);
        public override ConvertedEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, ConvertedEnum value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("state", "ready");
            writer.WriteEndObject();
        }
    }
    public sealed class UntypedActivity : IActivity
    {
        public ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context) =>
            throw new InvalidOperationException("Metadata discovery cannot execute an activity.");
    }

    private static Host ResultHost(Type activityType)
    {
        var descriptor = new ClrActivityDescriptor(activityType.FullName!);
        var model = new ActivityVersionReconciliationModel(null, "1.0.0", descriptor.TypeAlias, "Metadata result", "Tests", null,
            "elsa.clr-activity", "1", "elsa.clr-activity", "1", descriptor, [], [], []);
        var host = new Host(scanner: new FixedScanner(model));
        host.CopyMetadataAssembly(activityType.Assembly);
        return host;
    }

    private static ActivitySerializedShape Member(ActivitySerializedShape shape, string name) => Assert.Single(shape.Members, x => x.SerializedName == name).Shape;
    private static int CountMembers(ActivitySerializedShape shape) => shape.Members.Length + shape.Members.Sum(x => CountMembers(x.Shape)) +
        (shape.Element is null ? 0 : CountMembers(shape.Element)) + (shape.MapValue is null ? 0 : CountMembers(shape.MapValue));
    private static int Depth(ActivitySerializedShape shape) => new[] { 0 }.Concat(shape.Members.Select(x => 1 + Depth(x.Shape)))
        .Concat(shape.Element is null ? [] : new[] { 1 + Depth(shape.Element) })
        .Concat(shape.MapValue is null ? [] : new[] { 1 + Depth(shape.MapValue) }).Max();
    private static TypeDescriptor Descriptor(string alias, Type type) => new(alias, type, "Payload & Co", "Tests", "json",
        Enum.GetValues<CollectionKind>().ToHashSet(), true, true, new HashSet<string> { "elsa.json", "not-installed" });

    private sealed class Types(params TypeDescriptor[] descriptors) : IVariableTypeDescriptorCatalog
    {
        public IEnumerable<TypeDescriptor> GetDescriptors() => descriptors;
        public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() => descriptors.GroupBy(x => x.Category);
    }
    private sealed class Drivers : IActivityContractStorageDriverProvider
    {
        public IReadOnlyCollection<string> DriverKeys => ["elsa.json"];
    }
    private sealed class FixedScanner(ActivityVersionReconciliationModel model) : IClrAssemblyScanner
    {
        public IReadOnlyList<ActivityVersionReconciliationModel> Scan(string folderPath) => [model];
    }
    private sealed class Capabilities(IReadOnlyCollection<ActivityContractTypeCapability> entries) : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => entries;
    }

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "serialized-shape-" + Guid.NewGuid().ToString("N"));
        public IActivitySerializedShapeSource Source { get; }
        public IActivityContractCapabilityCatalog Capabilities => _scope.ServiceProvider.GetRequiredService<IActivityContractCapabilityCatalog>();
        public IActivityContractCapabilityCatalog? Replacement { get; }
        public int RuntimeRegistryResolutionCount { get; private set; }

        public Host(TypeDescriptor[]? descriptors = null, IActivityContractCapabilityCatalog? replacement = null,
            IClrAssemblyScanner? scanner = null, bool? apiBeforeMetadataFeature = null)
        {
            Directory.CreateDirectory(_folder);
            var assembly = typeof(SerializedCustomerFixtureActivity).Assembly.Location;
            File.Copy(assembly, Path.Combine(_folder, Path.GetFileName(assembly)));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IVariableTypeDescriptorCatalog>(new Types(descriptors ?? []));
            services.AddSingleton<IActivityContractStorageDriverProvider, Drivers>();
            services.AddSingleton<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
            services.AddSingleton<IWellKnownTypeRegistry>(_ =>
            {
                RuntimeRegistryResolutionCount++;
                throw new InvalidOperationException("Shape discovery cannot resolve a runtime registry.");
            });
            Replacement = replacement;
            if (replacement is not null)
                services.AddSingleton(replacement);
            if (apiBeforeMetadataFeature == true)
                new ActivitiesDesignApiFeature().ConfigureServices(services);
            new ClrActivityReconciliationFeature { Options = { FolderPath = _folder, SourceId = "shape-tests" } }.ConfigureServices(services);
            if (apiBeforeMetadataFeature == false)
                new ActivitiesDesignApiFeature().ConfigureServices(services);
            if (scanner is not null)
                services.AddSingleton(scanner);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            Source = Assert.Single(_scope.ServiceProvider.GetServices<IActivitySerializedShapeSource>());
            Assert.Same(Capabilities, _scope.ServiceProvider.GetRequiredService<IActivityContractCapabilityCatalog>());
        }

        public ActivityDefinitionVersionModel Version<T>() => Version(typeof(T));

        public ActivityDefinitionVersionModel Version(Type activityType)
        {
            var model = _scope.ServiceProvider.GetRequiredService<IClrAssemblyScanner>().Scan(_folder).Single(x => x.ActivityTypeKey == activityType.FullName);
            var definition = new ActivityDefinitionModel("definition", model.ActivityTypeKey, model.Category ?? string.Empty, model.DisplayName, model.Description);
            var version = new ActivityDefinitionVersionModel("version", model.Version, definition.Id, model.ProviderKey, model.ProviderSchemaVersion,
                model.ConsumerKey, model.ConsumerSchemaVersion, JsonSerializer.SerializeToElement(model.Descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "CLR", "shape-tests", definition, model.Inputs, model.Outputs, model.DesignFacets, model.ExecutionType, string.Empty);
            return version with { Hash = new DefaultActivityDefinitionHasher().Hash(definition, version) };
        }

        public string CopyMetadataAssembly(System.Reflection.Assembly assembly)
        {
            var destination = Path.Combine(_folder, Path.GetFileName(assembly.Location));
            File.Copy(assembly.Location, destination, overwrite: true);
            return destination;
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
            Directory.Delete(_folder, recursive: true);
        }
    }
}
