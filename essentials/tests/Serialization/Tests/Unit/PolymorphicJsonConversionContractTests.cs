using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class PolymorphicJsonConversionContractTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("read")]
    [InlineData("write")]
    public void An_inherited_scope_keeps_native_polymorphic_metadata(string operation)
    {
        const string raw = """{ "kind":"derived", "CUSTOMERNAME":"Ada" }""";
        var input = Element(raw);
        var contract = JsonConversionContract.For(typeof(InheritedBase));
        if (operation == "scan") Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(InheritedBase), contract));
        else if (operation == "read")
        {
            var diagnostics = new List<JsonConversionDiagnostic>();
            var value = Assert.IsType<InheritedDerived>(contract.Read(input, typeof(InheritedBase), diagnostics.Add));
            Assert.Equal("Ada", value.CustomerName);
            Assert.Empty(diagnostics);
        }
        else
        {
            var written = contract.Write(new InheritedDerived { CustomerName = "Ada" }, typeof(InheritedBase));
            Assert.Equal("derived", written.GetProperty("kind").GetString());
            Assert.Equal("Ada", written.GetProperty("customerName").GetString());
            Assert.Empty(UnmatchedMemberScanner.Scan(written, typeof(InheritedBase), contract));
        }
        Assert.Equal(raw, input.GetRawText());
    }

    [Fact]
    public void A_nested_polymorphic_boundary_keeps_its_type_scope_and_strict_member_paths()
    {
        var contract = JsonConversionContract.For(typeof(StrictHolder));
        var input = Element("""{"order":{"kind":"strict","CUSTOMERNAME":"Ada"}}""");
        var diagnostics = new List<JsonConversionDiagnostic>();
        var value = (StrictHolder)contract.Read(input, typeof(StrictHolder), diagnostics.Add)!;
        Assert.Equal("Ada", Assert.IsType<StrictDerived>(value.Order).CustomerName);
        Assert.Empty(diagnostics);
        var written = contract.Write(value, typeof(StrictHolder));
        Assert.Equal("strict", written.GetProperty("order").GetProperty("kind").GetString());
        Assert.Empty(UnmatchedMemberScanner.Scan(written, typeof(StrictHolder), contract));
        var refused = Assert.Throws<JsonConversionException>(() => contract.Read(
            Element("""{"order":{"kind":"strict","CUSTOMERNAME":"Ada","unexpected":1}}"""), typeof(StrictHolder)));
        Assert.Equal("json-unmatched-member-refused", refused.Code);
        Assert.Equal("$[\"order\"][\"unexpected\"]", refused.Path);
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(StrictHolder), contract));
    }

    [Fact]
    public void The_declared_base_scope_applies_to_its_discriminator_selected_shape()
    {
        var contract = JsonConversionContract.For(typeof(DeclaredBase));
        var input = Element("""{"kind":"explicit","customerName":"Ada"}""");
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(DeclaredBase), contract));
        var value = Assert.IsType<ExplicitDerived>(contract.Read(input, typeof(DeclaredBase)));
        Assert.Equal("Ada", value.CustomerName);
        var written = contract.Write(value, typeof(DeclaredBase));
        Assert.Equal("explicit", written.GetProperty("kind").GetString());
        Assert.Equal("Ada", written.GetProperty("customerName").GetString());
        Assert.False(written.TryGetProperty("CustomerName", out _));

        var direct = JsonConversionContract.For(typeof(ExplicitDerived));
        var directlyRead = (ExplicitDerived)direct.Read(Element("""{"CustomerName":"Direct"}"""), typeof(ExplicitDerived))!;
        Assert.Equal("Direct", directlyRead.CustomerName);
        Assert.Equal("Direct", direct.Write(directlyRead, typeof(ExplicitDerived)).GetProperty("CustomerName").GetString());
        Assert.Throws<JsonConversionException>(() => direct.Read(Element("""{"customerName":"Wrong case"}"""), typeof(ExplicitDerived)));
    }

    [Theory]
    [InlineData("declaredChild")]
    [InlineData("declaredChildren")]
    [InlineData("declaredNamed")]
    public void A_declared_derived_child_still_selects_its_own_type_contract(string shape)
    {
        const string child = """{"CustomerName":"Nested"}""";
        var contents = shape switch { "declaredChild" => child, "declaredChildren" => "[" + child + "]", _ => "{\"KeepKey\":" + child + "}" };
        var input = Element("{\"kind\":\"explicit\",\"customerName\":\"Root\",\"" + shape + "\":" + contents + "}");
        var contract = JsonConversionContract.For(typeof(DeclaredBase));
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(DeclaredBase), contract));
        var value = Assert.IsType<ExplicitDerived>(contract.Read(input, typeof(DeclaredBase)));
        var nested = shape switch { "declaredChild" => value.DeclaredChild!, "declaredChildren" => value.DeclaredChildren![0], _ => value.DeclaredNamed!["KeepKey"] };
        Assert.Equal("Nested", nested.CustomerName);
        var written = contract.Write(value, typeof(DeclaredBase));
        var nestedJson = shape switch { "declaredChild" => written.GetProperty(shape), "declaredChildren" => written.GetProperty(shape)[0], _ => written.GetProperty(shape).GetProperty("KeepKey") };
        Assert.Equal("Nested", nestedJson.GetProperty("CustomerName").GetString());
        Assert.False(nestedJson.TryGetProperty("customerName", out _));
        Assert.Empty(UnmatchedMemberScanner.Scan(written, typeof(DeclaredBase), contract));
    }

    [Fact]
    public void A_base_member_override_applies_to_the_selected_shape_without_changing_direct_derived_members()
    {
        var baseContract = JsonConversionContract.For(typeof(DeclaredBase), typeof(Inputs).GetProperty(nameof(Inputs.Base))!);
        var input = Element("""{"kind":"explicit","wire_name":"Ada"}""");
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(DeclaredBase), baseContract));
        var value = Assert.IsType<ExplicitDerived>(baseContract.Read(input, typeof(DeclaredBase)));
        Assert.Equal("Ada", value.CustomerName);
        var written = baseContract.Write(value, typeof(DeclaredBase));
        Assert.Equal("Ada", written.GetProperty("wire_name").GetString());
        Assert.Equal("explicit", written.GetProperty("kind").GetString());

        var derivedContract = JsonConversionContract.For(typeof(ExplicitDerived), typeof(Inputs).GetProperty(nameof(Inputs.Derived))!);
        var direct = (ExplicitDerived)derivedContract.Read(Element("""{"member_name":"Direct"}"""), typeof(ExplicitDerived))!;
        Assert.Equal("Direct", direct.CustomerName);
        Assert.Equal("Direct", derivedContract.Write(direct, typeof(ExplicitDerived)).GetProperty("member_name").GetString());
        Assert.Equal("Ada", baseContract.Write(value, typeof(DeclaredBase)).GetProperty("wire_name").GetString());
    }

    [Fact]
    public void Unannotated_polymorphic_children_keep_the_enclosing_scope_in_objects_and_collections()
    {
        var contract = JsonConversionContract.For(typeof(EnclosingScope));
        var input = Element("""{"order_value":{"kind":"explicit","CUSTOMER_NAME":"Ada"},"order_values":[{"kind":"explicit","CUSTOMER_NAME":"Lin"}]}""");
        Assert.Empty(UnmatchedMemberScanner.Scan(input, typeof(EnclosingScope), contract));
        var value = (EnclosingScope)contract.Read(input, typeof(EnclosingScope))!;
        Assert.Equal("Ada", Assert.IsType<ExplicitDerived>(value.OrderValue).CustomerName);
        Assert.Equal("Lin", Assert.IsType<ExplicitDerived>(Assert.Single(value.OrderValues!)).CustomerName);
        var written = contract.Write(value, typeof(EnclosingScope));
        Assert.Equal("Ada", written.GetProperty("order_value").GetProperty("customer_name").GetString());
        Assert.Empty(UnmatchedMemberScanner.Scan(written, typeof(EnclosingScope), contract));
    }

    [Theory]
    [InlineData("{\"kind\":\"unknown\",\"CUSTOMERNAME\":\"Ada\"}")]
    [InlineData("{\"CUSTOMERNAME\":\"Ada\"}")]
    [InlineData("{\"kind\":\"derived\"}")]
    public void Invalid_inputs_keep_native_discriminator_and_required_member_refusals(string json)
    {
        var contract = JsonConversionContract.For(typeof(InheritedBase));
        var native = Record.Exception(() => JsonSerializer.Deserialize<InheritedBase>(json, contract.Options));
        var converted = Record.Exception(() => contract.Read(Element(json), typeof(InheritedBase)));
        Assert.NotNull(native);
        Assert.NotNull(converted);
        Assert.Equal(native.GetType(), converted.GetType());
    }

    [Fact]
    public void An_opaque_derived_converter_keeps_native_limits_and_reports_coverage_without_false_member_warnings()
    {
        const string json = """{"kind":"opaque","customerName":"Ada"}""";
        var contract = JsonConversionContract.For(typeof(OpaqueBase));
        var coverage = Assert.Single(UnmatchedMemberScanner.Scan(Element(json), typeof(OpaqueBase), contract));
        Assert.Equal("json-unmatched-coverage-unavailable", coverage.Code);
        Assert.Equal(typeof(OpaqueDerived), coverage.TargetType);
        Assert.Equal(typeof(OpaqueDerivedConverter).FullName, coverage.Converter);
        var native = Record.Exception(() => JsonSerializer.Deserialize<OpaqueBase>(json, contract.Options));
        var converted = Record.Exception(() => contract.Read(Element(json), typeof(OpaqueBase)));
        Assert.NotNull(native);
        Assert.NotNull(converted);
        Assert.Equal(native.GetType(), converted.GetType());
    }

    [Fact]
    public void An_ordinary_hierarchy_and_native_nearest_ancestor_fallback_keep_their_dispatch()
    {
        var ordinary = JsonConversionContract.For(typeof(OrdinaryBase));
        var value = Assert.IsType<OrdinaryDerived>(ordinary.Read(Element("""{"kind":"ordinary","customerName":"Ada"}"""), typeof(OrdinaryBase)));
        Assert.Equal("Ada", value.CustomerName);
        Assert.Equal("ordinary", ordinary.Write(value, typeof(OrdinaryBase)).GetProperty("kind").GetString());

        var fallback = JsonConversionContract.For(typeof(FallbackBase));
        var subtype = new UnregisteredGrandchild { CustomerName = "Nearest", Extra = "omitted by native fallback" };
        var native = JsonSerializer.SerializeToElement(subtype, typeof(FallbackBase), fallback.Options);
        var written = fallback.Write(subtype, typeof(FallbackBase));
        Assert.True(JsonCanonicalForm.Equals(native, written));
        Assert.Equal("registered", written.GetProperty("kind").GetString());
        Assert.Equal("Nearest", written.GetProperty("customerName").GetString());
        Assert.False(written.TryGetProperty("extra", out _));
    }

    private static JsonElement Element(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    [JsonConversion(CaseInsensitive = true)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(InheritedDerived), "derived")]
    private abstract class InheritedBase;
    private sealed class InheritedDerived : InheritedBase { public required string CustomerName { get; set; } }

    [JsonConversion(CaseInsensitive = true, RefuseUnmatchedMembers = true)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(StrictDerived), "strict")]
    private abstract class StrictBase;
    private sealed class StrictDerived : StrictBase { public string? CustomerName { get; set; } }
    private sealed class StrictHolder { public StrictBase? Order { get; set; } }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(ExplicitDerived), "explicit")]
    private abstract class DeclaredBase;
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.Unspecified, RefuseUnmatchedMembers = true)]
    private sealed class ExplicitDerived : DeclaredBase
    {
        public string? CustomerName { get; set; }
        public ExplicitDerived? DeclaredChild { get; set; }
        public ExplicitDerived[]? DeclaredChildren { get; set; }
        public Dictionary<string, ExplicitDerived>? DeclaredNamed { get; set; }
    }
    private sealed class Inputs
    {
        [JsonConversion(MemberMappings = ["wire_name=CustomerName"], RefuseUnmatchedMembers = true)] public DeclaredBase? Base { get; set; }
        [JsonConversion(MemberMappings = ["member_name=CustomerName"])] public ExplicitDerived? Derived { get; set; }
    }
    [JsonConversion(NamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, CaseInsensitive = true)]
    private sealed class EnclosingScope { public DeclaredBase? OrderValue { get; set; } public List<DeclaredBase>? OrderValues { get; set; } }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(OrdinaryDerived), "ordinary")]
    private abstract class OrdinaryBase;
    private sealed class OrdinaryDerived : OrdinaryBase { public string? CustomerName { get; set; } }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind"), JsonDerivedType(typeof(OpaqueDerived), "opaque")]
    private abstract class OpaqueBase;
    [JsonConverter(typeof(OpaqueDerivedConverter))]
    private sealed class OpaqueDerived : OpaqueBase;
    private sealed class OpaqueDerivedConverter : JsonConverter<OpaqueDerived>
    {
        public OpaqueDerivedConverter() { }
        public override OpaqueDerived Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new InvalidOperationException("Native STJ discriminator dispatch does not invoke an opaque derived converter.");
        public override void Write(Utf8JsonWriter writer, OpaqueDerived value, JsonSerializerOptions options) => throw new InvalidOperationException("Native STJ discriminator dispatch does not invoke an opaque derived converter.");
    }

    [JsonConversion(CaseInsensitive = true)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
    [JsonDerivedType(typeof(RegisteredDerived), "registered")]
    private abstract class FallbackBase;
    private class RegisteredDerived : FallbackBase { public string? CustomerName { get; set; } }
    private sealed class UnregisteredGrandchild : RegisteredDerived { public string? Extra { get; set; } }
}
