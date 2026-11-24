using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonBaselineArchitectureTests
{
    [Theory]
    [InlineData("RuntimeInputBindingCompiler.cs")]
    [InlineData("RuntimeActivityInputMaterializer.cs")]
    public void Type_default_writers_require_the_baseline_and_cannot_join_the_legacy_list(string path)
    {
        const string source = "class C { void M(object defaultValue) { JsonSerializer.SerializeToElement(defaultValue, defaultValue!.GetType()); } }";
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(source));
        Assert.Equal("optionless", finding.Setting);
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity(path, finding) };
        Assert.False(JsonBaselineSourceScan.IsAccounted(path, finding, legacy, []));
        Assert.Empty(JsonBaselineSourceScan.Scan(source.Replace("defaultValue!.GetType())", "defaultValue!.GetType(), JsonBaseline.Options)", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("value?.Deserialize<Payload>()")]
    [InlineData("value?.Deserialize<Payload>(null)")]
    [InlineData("value?.Deserialize(typeof(Payload))")]
    public void Conditional_JSON_reads_also_require_explicit_options(string invocation)
    {
        var source = "class C { void M(JsonElement? value) { var result = " + invocation + "; } }";
        Assert.Equal("optionless", Assert.Single(JsonBaselineSourceScan.Scan(source)).Setting);
        Assert.Empty(JsonBaselineSourceScan.Scan("class C { void M(JsonElement? value) { var result = value?.Deserialize<Payload>(options: JsonBaseline.Options); } }"));
    }

    [Theory]
    [InlineData("value.Deserialize(typeof(Payload), null)")]
    [InlineData("value?.Deserialize(typeof(Payload), null)")]
    [InlineData("value.Deserialize(typeof(Payload), default(JsonSerializerOptions))")]
    [InlineData("value?.Deserialize(typeof(Payload), default(JsonSerializerOptions))")]
    public void A_required_runtime_Type_does_not_hide_a_null_JSON_profile(string invocation)
    {
        var source = "class C { void M(JsonElement? value) { var result = " + invocation + "; } }";
        Assert.Equal("optionless", Assert.Single(JsonBaselineSourceScan.Scan(source)).Setting);
    }

    [Theory]
    [InlineData("class FutureDataOwner { void M<T>(JsonElement? value) { value?.Deserialize<T>(); } }")]
    [InlineData("class FutureDataOwner<T> { void M(JsonElement value) { value.Deserialize<T>(); } }")]
    [InlineData("class FutureDataOwner<T> { void M(JsonElement? value) { value?.Deserialize<T>(); } }")]
    [InlineData("class FutureDataOwner { void M(JsonElement? value) { value?.Deserialize(typeof(Payload)); } }")]
    public void Conditional_and_enclosing_generic_data_reads_cannot_join_the_legacy_list(string source)
    {
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(source));
        Assert.True(finding.CarriesData);
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity("FutureDataOwner.cs", finding) };
        Assert.False(JsonBaselineSourceScan.IsAccounted("FutureDataOwner.cs", finding, legacy, []));
    }
    [Theory]
    [InlineData("class C { object x = new JsonSerializerOptions(); }", "construction")]
    [InlineData("class C { JsonSerializerOptions x = new(); }", "construction")]
    [InlineData("class C { JsonSerializerOptions Create() => new(); }", "construction")]
    [InlineData("class C { void M(object value) { JsonSerializerOptions options = JsonBaseline.CreateCopy(); options = new(); JsonSerializer.Serialize(value, options); } }", "construction")]
    [InlineData("class C { JsonSerializerOptions profile; void M() { profile = new(); } }", "construction")]
    [InlineData("class C { JsonSerializerOptions profile; void M(object profile) { this.profile = new(); } }", "construction")]
    [InlineData("class C { JsonSerializerOptions Profile { get; set; } void M() { Profile = new(); } }", "construction")]
    [InlineData("class C { JsonSerializerOptions Profile { get; set; } void M(object Profile) { this.Profile = new(); } }", "construction")]
    [InlineData("class C { void M(JsonSerializerOptions profile) { profile = (new()); } }", "construction")]
    [InlineData("class C { static System.Text.Json.JsonSerializerOptions profile; void M() { C.profile = new(); } }", "construction")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { void M() { Opt profile = JsonBaseline.CreateCopy(); profile = new(); } }", "construction")]
    [InlineData("class Holder { public JsonSerializerOptions Profile { get; set; } } class C { void M(Holder target) { target.Profile = new(); } }", "construction")]
    [InlineData("class Holder { public JsonSerializerOptions SerializerOptions { get; set; } } class C { void M(Holder target) { target.SerializerOptions = new(); } }", "construction")]
    [InlineData("class C { void M(object value) { JsonSerializer.Serialize(value, options: new()); } }", "construction")]
    [InlineData("class C { void M(object value) { System.Text.Json.JsonSerializer.Serialize(value, options: new()); } }", "construction")]
    [InlineData("using JS = System.Text.Json.JsonSerializer; class C { void M(object value) { JS.Serialize(value, options: new()); } }", "construction")]
    [InlineData("using static System.Text.Json.JsonSerializer; class C { void M(object value) { Serialize(value, options: new()); } }", "construction")]
    [InlineData("class C { void Use(JsonSerializerOptions options) {} void M() { Use(new()); } }", "construction")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { void Use(Opt options) {} void M() { this.Use(new()); } }", "construction")]
    [InlineData("class C { void Use(JsonSerializerOptions options, bool flag) {} void M() { Use(flag: true, options: new()); } }", "construction")]
    [InlineData("class C { void M() { void Use(JsonSerializerOptions options) {} Use(new()); } }", "construction")]
    [InlineData("class Factory { public static void Use(JsonSerializerOptions options) {} } class C { void M() { Factory.Use(new()); } }", "construction")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { object x = new Opt(); }", "construction")]
    [InlineData("class C { object x = JsonSerializerOptions.Web; }", "defaults")]
    [InlineData("class C { object x = JsonSerializerOptions.Default; }", "defaults")]
    [InlineData("[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)] class C {}", "PropertyNameCaseInsensitive")]
    [InlineData("[JsonSourceGenerationOptions(Converters = [typeof(LossyConverter)])] class C {}", "converter:LossyConverter")]
    [InlineData("[JsonSourceGenerationOptions(UseStringEnumConverter = true)] class C {}", "converter:JsonStringEnumConverter")]
    [InlineData("class C { object x = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }; }", "UnmappedMemberHandling")]
    [InlineData("class C { void M() { options.MaxDepth = 128; } }", "MaxDepth")]
    [InlineData("class C { void M() { options.AllowTrailingCommas = true; } }", "AllowTrailingCommas")]
    [InlineData("class C { void M() { options.ReadCommentHandling = JsonCommentHandling.Skip; } }", "ReadCommentHandling")]
    [InlineData("class C { void M() { options.ReferenceHandler = ReferenceHandler.IgnoreCycles; } }", "ReferenceHandler")]
    [InlineData("class C { void M() { options.IgnoreReadOnlyProperties = true; } }", "IgnoreReadOnlyProperties")]
    [InlineData("class C { void M() { options.IncludeFields = true; } }", "IncludeFields")]
    [InlineData("class C { void M() { options.WriteIndented = true; } }", "WriteIndented")]
    [InlineData("class C { void M() { options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { options.Converters.Add(new LossyConverter()); } }", "converter:LossyConverter")]
    [InlineData("class C { void M() { options.Converters.Insert(0, new LossyConverter()); } }", "converter:LossyConverter")]
    [InlineData("class C { void M() { options.Converters.Clear(); } }", "converter-mutation:Clear")]
    [InlineData("class C { void M() { options.Converters.RemoveAt(0); } }", "converter-mutation:RemoveAt")]
    [InlineData("class C { void M() { options.Converters.Remove(converter); } }", "converter-mutation:Remove")]
    [InlineData("class C { void M() { options.TypeInfoResolver.Modifiers.Add(ChangeData); } }", "modifier:ChangeData")]
    [InlineData("class C { void M() { resolver.WithAddedModifier(ChangeData); } }", "modifier:ChangeData")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value); } }", "optionless")]
    [InlineData("class C { void M(Utf8JsonWriter writer, object value) { JsonSerializer.Serialize(writer, value); } }", "optionless")]
    [InlineData("class C { void M(object value, Type target) { JsonSerializer.Serialize(value, target); } }", "optionless")]
    [InlineData("class C { void M(object value) { var target = typeof(UserType); JsonSerializer.Serialize(value, target); } }", "optionless")]
    [InlineData("class C { void M(JsonElement value, Type target) { value.Deserialize(target); } }", "optionless")]
    [InlineData("class C { object x = new JsonSerializerOptions(NotJsonBaseline.Options); }", "construction")]
    [InlineData("class C { void M() { var wire = JsonBaseline.CreateCopy(); wire.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { var original = JsonBaseline.CreateCopy(); var wire = original; wire.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { dynamic wire = JsonBaseline.CreateCopy(); wire.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M(JsonSerializerOptions? wire) { wire!.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }", "DictionaryKeyPolicy")]
    [InlineData("class C { void M() { var original = JsonBaseline.CreateCopy(); var list = original.Converters; list.Clear(); } }", "converter-mutation:Clear")]
    [InlineData("class C { void M() { var original = JsonBaseline.CreateCopy(); dynamic list = original.Converters; list.Clear(); } }", "converter-mutation:Clear")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { void M(Opt wire) { var list = wire.Converters; list.Clear(); } }", "converter-mutation:Clear")]
    [InlineData("class C { void M() { var resolver = new DefaultJsonTypeInfoResolver(); var list = resolver.Modifiers; list.Clear(); } }", "modifier-mutation:Clear")]
    [InlineData("class C { void M(DefaultJsonTypeInfoResolver resolver) { IList<Action<JsonTypeInfo>> list = resolver.Modifiers; list.Clear(); } }", "modifier-mutation:Clear")]
    [InlineData("class C { void M() { resolver.Modifiers.Clear(); } }", "modifier-mutation:Clear")]
    [InlineData("class C { void M() { resolver.Modifiers[0] = ChangeData; } }", "modifier-mutation:replace")]
    [InlineData("class C { void M() { options.Converters[0] = converter; } }", "converter-mutation:replace")]
    [InlineData("class C { void M() { options.Encoder = CustomJavaScriptEncoder.UnsafeRelaxedJsonEscaping; } }", "Encoder")]
    [InlineData("class C { void M() { options.ReadCommentHandling = CustomJsonCommentHandling.Disallow; } }", "ReadCommentHandling")]
    [InlineData("class C { object x = new JsonSerializerOptions { Converters = { converter } }; }", "converter:converter")]
    [InlineData("class C { object x = new DefaultJsonTypeInfoResolver { Modifiers = { ChangeData } }; }", "modifier:ChangeData")]
    [InlineData("class C { void M() { builder.HasColumnType(\"jsonb\"); } }", "storage:jsonb")]
    [InlineData("class C { void M() { builder.HasColumnType(\"JSONB\"); } }", "storage:jsonb")]
    [InlineData("class C { void M() { builder.HasColumnType(\"jsonb\", fromDataAnnotation: true); } }", "storage:jsonb")]
    [InlineData("class C { void M() { builder.HasColumnType(fromDataAnnotation: true, typeName: \"jsonb\"); } }", "storage:jsonb")]
    [InlineData("class C { void M() { property.SetColumnType(\"jsonb\"); } }", "storage:jsonb")]
    [InlineData("class C { void M() { var type = \"text\"; type = \"jsonb\"; builder.HasColumnType(type); } }", "storage:jsonb")]
    [InlineData("class C { void M() { var payloadType = provider switch { 1 => \"jsonb\", _ => \"text\" }; builder.HasColumnType(payloadType); } }", "storage:jsonb")]
    [InlineData("class C { void M() { table.Column<string>(type: \"json\", nullable: false); } }", "storage:json")]
    [InlineData("class C { void M(OwnedNavigationBuilder builder) { builder.ToJson(); } }", "storage:owned-json")]
    [InlineData("class C { void M() { builder.ToJson(); } }", "json-method:ToJson")]
    [InlineData("class C { void M() { migration.AlterColumn<string>(name: \"Data\", type: \"jsonb\"); } }", "storage:jsonb")]
    [InlineData("class C { [Column(TypeName = \"json\")] public string Data { get; set; } }", "storage:json")]
    [InlineData("class C { [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = \"JSON\")] public string Data { get; set; } }", "storage:json")]
    [InlineData("using DbColumn = System.ComponentModel.DataAnnotations.Schema.ColumnAttribute; class C { [DbColumn(TypeName = \"json\")] public string Data { get; set; } }", "storage:json")]
    [InlineData("[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)] class C {}", "source-generation-defaults")]
    [InlineData("using Gen = System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute; [Gen(PropertyNameCaseInsensitive = true)] class C {}", "PropertyNameCaseInsensitive")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, OwnerJsonContext.Default.Value); } }", "context-default")]
    [InlineData("class OwnerContext : JsonSerializerContext {} class C { object options = OwnerContext.Default.Options; }", "context-default")]
    [InlineData("using Meta = OwnerContext; class OwnerContext : JsonSerializerContext {} class C { object options = Meta.Default.Options; }", "context-default")]
    [InlineData("class C { void M() { request.ReadFromJsonAsync<Request>(); } }", "optionless")]
    [InlineData("class C { void M() { Results.Json(value); } }", "optionless")]
    [InlineData("class C { void M() { Results.Json(\"Fake.WireContext.Value\"); } }", "optionless")]
    [InlineData("class C { JsonElement? WireContext { get; } void M() { Results.Json(this.WireContext.Value); } }", "optionless")]
    [InlineData("class C { JsonElement? WireContext { get; } void M() { Results.Json(statusCode: 200, data: this.WireContext.Value); } }", "optionless")]
    [InlineData("class C { string WireContext { get; } void M() { Results.Json(data: value, contentType: this.WireContext.Value); } }", "optionless")]
    [InlineData("class C { void M() { var value = \"Fake.WireContext.Value\"; Results.Json(value); } }", "optionless")]
    [InlineData("class C { void M() { TypedResults.Ok(value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, options: null); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, default(JsonSerializerOptions)); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, (JsonSerializerOptions?)null); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.Serialize(value, options: null!); } }", "optionless")]
    [InlineData("class C { void M() { Results.Json(value, options: default(JsonSerializerOptions)!); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializerOptions? profile = null; JsonSerializer.Serialize(value, profile); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>((JsonSerializerOptions?)null); } }", "optionless")]
    [InlineData("class C { void M() { Results.Json(data, options: default(JsonSerializerOptions)); } }", "optionless")]
    [InlineData("class C { void M() { request.ReadFromJsonAsync<Request>((JsonSerializerOptions?)null); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeToElement(value, value.GetType()); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeAsync(stream, value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.DeserializeAsync<T>(stream); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, topLevelValues: true); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, false); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, (true)); } }", "optionless")]
    [InlineData("class C { void M(Utf8JsonWriter? writer, object data) { JsonSerializer.Serialize(writer!, data); } }", "optionless")]
    [InlineData("using Writer = System.Text.Json.Utf8JsonWriter; class C { void M(Writer writer, object data) { JsonSerializer.Serialize(writer, data); } }", "optionless")]
    [InlineData("class UserJsonContext { public JsonElement Data {get;set;} } class C { void M(Utf8JsonWriter writer, JsonElement raw) { JsonSerializer.Serialize(writer, new UserJsonContext { Data = raw }); } }", "optionless")]
    [InlineData("class CustomJsonSerializerOptions { public JsonElement Data {get;set;} } class C { void M(Utf8JsonWriter writer, JsonElement raw) { JsonSerializer.Serialize(writer, new CustomJsonSerializerOptions { Data = raw }); } }", "optionless")]
    [InlineData("class C { void M(Utf8JsonWriter writer, object data) { JsonSerializer.Serialize(inputType: typeof(object), value: data, writer: writer); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>(); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize<T>(options: null); } }", "optionless")]
    [InlineData("using JS = System.Text.Json.JsonSerializer; class C { void M() { JS.Serialize(value); } }", "optionless")]
    [InlineData("using static System.Text.Json.JsonSerializer; class C { void M() { Serialize(value); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeToNode(value); } }", "optionless")]
    [InlineData("class C { void M() { payload.Deserialize(typeof(UserType)); } }", "optionless")]
    [InlineData("class C { void M() { JsonSerializer.SerializeAsync(stream, value, cancellationToken: token); } }", "optionless")]
    public void Every_unregistered_deviation_is_rejected(string source, string setting)
    {
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(source).Where(x => x.Setting == setting));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), []));
    }

    [Fact]
    public void A_reasoned_register_entry_accounts_only_for_its_file_and_specific_setting()
    {
        var finding = Assert.Single(JsonBaselineSourceScan.Scan("class C { object x = new JsonSerializerOptions(); }"));
        var register = JsonBaselineSourceScan.ReadRegister("## Baseline deviations\n| File | Setting | Reason | Approved by |\n|---|---|---|---|\n| `owner.cs` | `" + JsonBaselineSourceScan.RegistrationKey(finding) + "` | Own format projection. | ADR 0091 |\n");
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), register));
        Assert.False(JsonBaselineSourceScan.IsAccounted("foreign.cs", finding, new HashSet<string>(), register));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding with { Setting = "Encoder" }, new HashSet<string>(), register));
    }

    [Fact]
    public void A_same_setting_addition_cannot_borrow_an_existing_deviation_receipt()
    {
        var findings = JsonBaselineSourceScan.Scan("class C { void M() { options.Converters.Add(new LossyConverter()); options.Converters.Add(new LossyConverter()); } }");
        var original = findings[0];
        var register = new[] { new JsonBaselineSourceScan.Registration("owner.cs", new HashSet<string> { JsonBaselineSourceScan.RegistrationKey(original) }, "One owned converter call.", "ADR 0091 decision 7") };
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", original, new HashSet<string>(), register));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", findings[1], new HashSet<string>(), register));
    }

    [Theory]
    [InlineData("class NewOwner { void M(object data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M(object? data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class Metadata {} class NewOwner { void M(JsonElement raw) { dynamic data = new Metadata(); data = raw; JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M<T>(T data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M(Dictionary<string,JsonElement> data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M(IReadOnlyList<object?> data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("using Data = System.Text.Json.JsonElement; class NewOwner { void M(Data? data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M(JsonElement data) { JsonSerializer.Serialize(new { value = data }); } }")]
    [InlineData("class NewOwner { void M() { JsonSerializer.Serialize(new JsonObject { [\"value\"] = 1 }); } }")]
    [InlineData("class NewOwner { JsonElement? WireContext { get; } void M() { Results.Json(this.WireContext.Value); } }")]
    [InlineData("class NewOwner { void M(JsonElement data) { JsonSerializer.Serialize(data); } }")]
    [InlineData("class NewOwner { void M<T>(T data) { JsonSerializer.Serialize<T>(data); } }")]
    [InlineData("class NewOwner { void M(JsonElement data, Type type) { data.Deserialize(type); } }")]
    public void A_new_data_owner_cannot_join_the_own_contract_legacy_list(string source)
    {
        var call = Assert.Single(JsonBaselineSourceScan.Scan(source));
        Assert.False(JsonBaselineSourceScan.IsAccounted("FutureDataOwner.cs", call, new HashSet<string> { JsonBaselineSourceScan.Identity("FutureDataOwner.cs", call) }, []));
    }

    [Fact]
    public void Legacy_accounting_never_admits_workflow_data_and_a_removed_migrated_entry_passes()
    {
        var call = Assert.Single(JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(value); } }"));
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity("owner.cs", call), JsonBaselineSourceScan.Identity("StatefulActivity.cs", call) };
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("foreign.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("StatefulActivity.cs", call, legacy, []));
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", call, new HashSet<string>(), []));
        var migrated = JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(value, JsonBaseline.Options); } }");
        Assert.Empty(migrated.Where(x => !JsonBaselineSourceScan.IsAccounted("owner.cs", x, new HashSet<string>(), [])));
    }

    [Fact]
    public void Baseline_copies_explicit_type_info_protected_defaults_comments_and_strings_are_accepted()
    {
        const string source = "class C { JsonSerializerOptions options = new(JsonBaseline.Options); void M() { options.PropertyNameCaseInsensitive = false; options.NumberHandling = JsonNumberHandling.Strict; JsonSerializer.Serialize(value, options); JsonSerializer.Serialize(value, Context.WireContext.Value); /* new JsonSerializerOptions() */ var text = \"JsonSerializer.Serialize(value)\"; } }";
        Assert.Empty(JsonBaselineSourceScan.Scan(source));
    }

    [Theory]
    [InlineData("class C { void M(JsonSerializerOptions profile) { profile = new(JsonBaseline.Options); } }")]
    [InlineData("class C { JsonSerializerOptions Profile { get; set; } void M() { this.Profile = new(JsonBaseline.Options); } }")]
    [InlineData("using Opt = System.Text.Json.JsonSerializerOptions; class C { Opt profile; void M() { profile = new(JsonBaseline.Options); } }")]
    [InlineData("class C { JsonSerializerOptions M() { object value; value = new(); return JsonBaseline.CreateCopy(); } }")]
    [InlineData("class C { object profile; JsonSerializerOptions M(JsonSerializerOptions profile) { this.profile = new(); return profile; } }")]
    [InlineData("class C { object Profile { get; set; } JsonSerializerOptions M(JsonSerializerOptions Profile) { this.Profile = new(); return Profile; } }")]
    [InlineData("class Holder { public object Profile { get; set; } } class C { JsonSerializerOptions M(Holder target) { target.Profile = new(); return JsonBaseline.CreateCopy(); } }")]
    [InlineData("class Holder { public object SerializerOptions { get; set; } } class C { void M(Holder target) { target.SerializerOptions = new(); } }")]
    [InlineData("class C { JsonSerializerOptions M(object value) { value = new(); return JsonBaseline.CreateCopy(); } }")]
    [InlineData("class C { void M() { JsonDocumentOptions value; value = new(); } }")]
    [InlineData("class C { void M(object value) { JsonSerializer.Serialize(value, options: new(JsonBaseline.Options)); } }")]
    [InlineData("class C { void Use(JsonSerializerOptions options) {} void M() { Use(new(JsonBaseline.Options)); } }")]
    [InlineData("class C { void Use(object options) {} JsonSerializerOptions M() { Use(new()); return JsonBaseline.CreateCopy(); } }")]
    [InlineData("class C { void Use(JsonSerializerOptions options) {} JsonSerializerOptions M() { void Use(object options) {} Use(new()); return JsonBaseline.CreateCopy(); } }")]
    [InlineData("class Other { public static void Use(object options) {} } class C { void Use(JsonSerializerOptions options) {} void M() { Other.Use(new()); } }")]
    [InlineData("class JsonSerializer { public static void Serialize(object value, object options) {} } class C { void M(object value) { JsonSerializer.Serialize(value, options: new()); } }")]
    public void Target_typed_constructions_accept_baseline_copies_and_respect_declared_types(string source) =>
        Assert.Empty(JsonBaselineSourceScan.Scan(source));

    [Theory]
    [InlineData("class H { public H(JsonSerializerOptions options) {} } class C { object M() => new H(new()); }")]
    [InlineData("class H { public H(JsonSerializerOptions options) {} } class C { H M() => new(new()); }")]
    [InlineData("class H { public H(object business, JsonSerializerOptions options) {} } class C { object M() => new H(new(), new()); }")]
    [InlineData("class H { public H(object business, JsonSerializerOptions options) {} } class C { object M() => new H(options: new(), business: new()); }")]
    [InlineData("class H { public H(JsonSerializerOptions options, int count) {} public H(object value, string text) {} } class C { object M() => new H(new(), 1); }")]
    [InlineData("class C { C(JsonSerializerOptions options) {} C() : this(new()) {} }")]
    [InlineData("class B { protected B(JsonSerializerOptions options) {} } class C : B { C() : base(new()) {} }")]
    [InlineData("class B(JsonSerializerOptions options) {} class C() : B(new()) {}")]
    [InlineData("class C { void M() { JsonSerializerOptions Local() => new(); } }")]
    [InlineData("class C { JsonSerializerOptions P { get => new(); } }")]
    [InlineData("class C { void M() { JsonSerializerOptions[] values = [new()]; } }")]
    [InlineData("class C { void M() { var values = new JsonSerializerOptions[] { new() }; } }")]
    [InlineData("class C { void M() { List<JsonSerializerOptions> values = [new()]; } }")]
    [InlineData("class C { void M() { var values = new List<JsonSerializerOptions> { new() }; } }")]
    [InlineData("class C { void M() { Func<JsonSerializerOptions> make = () => new(); } }")]
    [InlineData("class C { Func<JsonSerializerOptions> M() => () => new(); }")]
    [InlineData("class C { void M() { Func<JsonSerializerOptions> make = delegate { return new(); }; } }")]
    [InlineData("class H { public JsonSerializerOptions P { get; set; } } class C { object M() => new H { P = new() }; }")]
    [InlineData("class C { void M(Action<JsonSerializerOptions> use) { use(new()); } }")]
    [InlineData("class C { void M(JsonSerializerOptions[] values) { values[0] = new(); } }")]
    [InlineData("class H { public JsonSerializerOptions this[int index] { get => JsonBaseline.Options; set {} } } class C { void M(H value) { value[0] = new(); } }")]
    [InlineData("class C { static void Use<T>(T value) {} void M() { Use<JsonSerializerOptions>(new()); } }")]
    [InlineData("class C { (JsonSerializerOptions Options, object Business) M() => (new(), new()); }")]
    [InlineData("class C { async System.Threading.Tasks.Task<JsonSerializerOptions> M() { await System.Threading.Tasks.Task.Yield(); return new(); } }")]
    [InlineData("namespace A { class H { public H(object value) {} } } namespace B { class H { public H(JsonSerializerOptions options) {} } class C { object M() => new H(new()); } }")]
    public void Every_valid_target_typed_options_construction_requires_a_baseline_or_receipt(string body)
    {
        var source = TargetTypedSource(body);
        // These controls exercise valid C# target conversions, not a syntax shape that cannot ship.
        _ = FixtureCompiler.Compile("TargetTypedOptionsControl", source, [typeof(Elsa.Serialization.Core.JsonBaseline).Assembly]);
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(source));
        Assert.Equal("construction", finding.Setting);
        Assert.False(JsonBaselineSourceScan.IsAccounted("NewOwner.cs", finding, new HashSet<string>(), []));
    }

    [Theory]
    [InlineData("class H { public H(object business) {} } class C { object M() => new H(new()); }")]
    [InlineData("class H { public H(object business) {} } class C { H M() => new(new()); }")]
    [InlineData("class H { public H(object business, JsonSerializerOptions options) {} } class C { object M() => new H(new(), JsonBaseline.Options); }")]
    [InlineData("class H { public H(object business, JsonSerializerOptions options) {} } class C { object M() => new H(options: JsonBaseline.Options, business: new()); }")]
    [InlineData("class H { public H(JsonSerializerOptions options, int count) {} public H(object value, string text) {} } class C { object M() => new H(new(), \"text\"); }")]
    [InlineData("class H { public H(JsonSerializerOptions options) {} } class C { object M() => new H(new(JsonBaseline.Options)); }")]
    [InlineData("class C { C(object value) {} C() : this(new()) {} }")]
    [InlineData("class B { protected B(object value) {} } class C : B { C() : base(new()) {} }")]
    [InlineData("class B(object value) {} class C() : B(new()) {}")]
    [InlineData("class C { JsonSerializerOptions M() { object Local() => new(); return JsonBaseline.Options; } }")]
    [InlineData("class C { void M() { object[] values = [new()]; } }")]
    [InlineData("class C { void M() { var values = new object[] { new() }; } }")]
    [InlineData("class C { void M() { List<object> values = [new()]; } }")]
    [InlineData("class C { void M() { var values = new List<object> { new() }; } }")]
    [InlineData("class C { void M() { Func<object> make = () => new(); } }")]
    [InlineData("class C { void M() { Func<object> make = delegate { return new(); }; } }")]
    [InlineData("class H { public object P { get; set; } } class C { object M() => new H { P = new() }; }")]
    [InlineData("class C { void M(Action<object> use) { use(new()); } }")]
    [InlineData("class C { void M(object[] values) { values[0] = new(); } }")]
    [InlineData("class H { public object this[int index] { get => new object(); set {} } } class C { void M(H value) { value[0] = new(); } }")]
    [InlineData("class C { static void Use<T>(T value) {} void M() { Use<object>(new()); } }")]
    [InlineData("namespace A { class H { public H(JsonSerializerOptions options) {} } } namespace B { class H { public H(object value) {} } class C { object M() => new H(new()); } }")]
    [InlineData("namespace Business { class JsonSerializerOptions {} class C { JsonSerializerOptions M() => new(); } }")]
    public void Compiler_targets_do_not_borrow_unrelated_types_or_enclosing_options_returns(string body)
    {
        var source = TargetTypedSource(body);
        _ = FixtureCompiler.Compile("TargetTypedBusinessControl", source, [typeof(Elsa.Serialization.Core.JsonBaseline).Assembly]);
        Assert.Empty(JsonBaselineSourceScan.Scan(source));
    }

    [Theory]
    [InlineData("class H { public H(JsonSerializerOptions options) {} } class C { object M() => new H(new()); }")]
    [InlineData("class C { JsonSerializerOptions M() => new(); }")]
    public void Incomplete_fragments_retain_their_exact_declared_options_target_without_ambient_usings(string source) =>
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(source)).Setting);

    [Fact]
    public void A_target_typed_baseline_copy_in_a_constructor_still_checks_its_protected_initializer_settings()
    {
        var source = TargetTypedSource("class H { public H(JsonSerializerOptions options) {} } class C { object M() => new H(new(JsonBaseline.Options) { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase }); }");
        _ = FixtureCompiler.Compile("TargetTypedProtectedInitializerControl", source, [typeof(Elsa.Serialization.Core.JsonBaseline).Assembly]);
        Assert.Equal("DictionaryKeyPolicy", Assert.Single(JsonBaselineSourceScan.Scan(source)).Setting);
        Assert.Empty(JsonBaselineSourceScan.Scan(source.Replace("JsonNamingPolicy.CamelCase", "null", StringComparison.Ordinal)));
    }

    private static string TargetTypedSource(string body) =>
        "using System; using System.Collections.Generic; using System.Text.Json; using Elsa.Serialization.Core; " + body;

    [Fact]
    public void Reordered_explicit_baseline_profile_and_null_body_are_accepted()
    {
        Assert.Empty(JsonBaselineSourceScan.Scan("class C { void M() { JsonSerializer.Serialize(options: JsonBaseline.Options, value: (object?)null); } }"));
    }

    [Fact]
    public void A_domain_depth_property_and_owner_codec_are_not_serializer_settings_or_optionless_calls()
    {
        Assert.Empty(JsonBaselineSourceScan.Scan("class C { void M() { model.MaxDepth = 128; var parsing = new JsonDocumentOptions { MaxDepth = 128 }; parsing.MaxDepth = 256; RuntimeCompleteActivityPayloadMemo.Deserialize(workItem); var value = codec.Deserialize(input); } }"));
    }

    [Fact]
    public void Every_production_root_uses_the_baseline_or_a_specific_recorded_deviation()
    {
        var register = JsonBaselineSourceScan.ReadRegister(File.ReadAllText(Path.Join(RepoPaths.RepoRoot, "docs/serialization.md")));
        var legacyPath = Path.Join(RepoPaths.RepoRoot, "essentials/tests/Architecture/Tests/Baselines/json-optionless-calls.txt");
        Assert.True(File.Exists(legacyPath), "Missing shrinking option-less own-contract inventory.");
        var legacy = File.ReadAllLines(legacyPath).Where(line => line.Length > 0 && !line.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var paths = ModuleRoots.Resolve(RepoPaths.RepoRoot, [.. ModuleRoots.Production, "Studio/src", "extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(path) && ModuleRoots.IsNotTestFile(path)).Distinct().ToArray();
        Assert.NotEmpty(paths);
        var storageRoots = paths.Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot()).ToArray();
        var configuration = typeof(JsonBaselineArchitectureTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var constructionContexts = new JsonBaselineConstructionContexts(storageRoots, ShippingHashAssemblies.Discover(RepoPaths.RepoRoot, configuration), configuration);
        var rootsByPath = storageRoots.ToDictionary(root => root.SyntaxTree.FilePath, StringComparer.Ordinal);
        Assert.All(register, row => Assert.True(File.Exists(Path.Join(RepoPaths.RepoRoot, row.File)), $"Stale register path: {row.File}"));
        var failures = new List<string>();
        var observedLegacy = new HashSet<string>(StringComparer.Ordinal);
        var observedDeviations = new HashSet<(string File, string Setting)>();
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(RepoPaths.RepoRoot, path).Replace('\\', '/');
            foreach (var finding in JsonBaselineSourceScan.Scan(rootsByPath[path], storageRoots, constructionContexts))
            {
                if (finding.Setting == "optionless") observedLegacy.Add(JsonBaselineSourceScan.Identity(relative, finding));
                else observedDeviations.Add((relative, JsonBaselineSourceScan.RegistrationKey(finding)));
                if (!JsonBaselineSourceScan.IsAccounted(relative, finding, legacy, register)) failures.Add($"{relative}:{finding.Line} | {finding.Setting} | {finding.Signature}");
            }
        }
        failures.AddRange(legacy.Except(observedLegacy).Select(identity => "Remove stale migrated legacy receipt: " + identity));
        failures.AddRange(register.SelectMany(row => row.Settings.Select(setting => (row.File, Setting: setting)))
            .Except(observedDeviations).Select(site => "Remove stale deviation receipt: " + site.File + " | " + site.Setting));
        Assert.True(failures.Count == 0, "Unregistered JSON options or new/workflow-data option-less calls:\n" + string.Join('\n', failures));
    }

    [Fact]
    public void Duplicate_calls_and_same_named_types_cannot_share_a_legacy_receipt()
    {
        var source = "namespace A { class C { void M() { JsonSerializer.Serialize(value); JsonSerializer.Serialize(value); } } } namespace B { class C { void M() { JsonSerializer.Serialize(value); } } }";
        var calls = JsonBaselineSourceScan.Scan(source);
        Assert.Equal(3, calls.Count);
        Assert.Equal(3, calls.Select(call => JsonBaselineSourceScan.Identity("owner.cs", call)).Distinct().Count());
        var legacy = new HashSet<string> { JsonBaselineSourceScan.Identity("owner.cs", calls[0]) };
        Assert.True(JsonBaselineSourceScan.IsAccounted("owner.cs", calls[0], legacy, []));
        Assert.All(calls.Skip(1), call => Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", call, legacy, [])));
    }
}
