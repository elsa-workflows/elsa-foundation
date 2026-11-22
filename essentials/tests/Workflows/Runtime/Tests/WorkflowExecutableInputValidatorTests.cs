using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableInputValidatorTests
{
    [Fact]
    public void Valid_scalar_input_is_normalized_by_ordinal_name()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string)), ("Int32", typeof(int))));
        var contract = Contract(
            new WorkflowDeclaredInput("zeta", new TypeReference("String"), true),
            new WorkflowDeclaredInput("alpha", new TypeReference("Int32"), true));
        var inputs = new[]
        {
            Pair("zeta", Json("\"ready\"")),
            Pair("alpha", Json("42"))
        };

        var result = validator.Validate(contract, inputs);

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
        Assert.Equal(new[] { "alpha", "zeta" }, result.NormalizedInputs.Keys);
        Assert.Equal(42, result.NormalizedInputs["alpha"].GetInt32());
        Assert.Equal("ready", result.NormalizedInputs["zeta"].GetString());
    }

    [Fact]
    public void Blank_input_name_is_rejected_without_retaining_its_value()
    {
        const string rejectedValue = "do-not-retain";
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("known", new TypeReference("String"), false)),
            [Pair(" ", Json($"\"{rejectedValue}\""))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.BlankName, finding.ReasonCode);
        Assert.Null(finding.InputName);
        Assert.DoesNotContain(rejectedValue, finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Unknown_input_name_is_rejected_ordinally_without_retaining_its_value()
    {
        const string rejectedValue = "unknown-secret";
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("Known", new TypeReference("String"), false)),
            [Pair("known", Json($"\"{rejectedValue}\""))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.UnknownName, finding.ReasonCode);
        Assert.Equal("known", finding.InputName);
        Assert.DoesNotContain(rejectedValue, finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Duplicate_input_name_is_rejected_using_ordinal_comparison()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), false)),
            [Pair("count", Json("1")), Pair("count", Json("2"))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.DuplicateName, finding.ReasonCode);
        Assert.Equal("count", finding.InputName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Missing_required_input_without_default_is_rejected()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("required", new TypeReference("String"), true)),
            Array.Empty<KeyValuePair<string, JsonElement>>());

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.MissingRequired, finding.ReasonCode);
        Assert.Equal("required", finding.InputName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Missing_required_input_with_literal_default_is_materialized()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("greeting", new TypeReference("String"), true, Json("\"hello\""))),
            Array.Empty<KeyValuePair<string, JsonElement>>());

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
        Assert.Equal("hello", result.NormalizedInputs["greeting"].GetString());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void Explicit_null_is_accepted_for_nullable_inputs_independently_of_requiredness(bool isRequired, bool isNullable, bool isValid)
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("value", new TypeReference("String"), isRequired, defaultValue: Json("\"fallback\""), isNullable: isNullable)),
            [Pair("value", Json("null"))]);

        Assert.Equal(isValid, result.IsValid);
        if (isValid)
        {
            Assert.Equal(JsonValueKind.Null, result.NormalizedInputs["value"].ValueKind);
            return;
        }

        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.IncompatibleValue, finding.ReasonCode);
        Assert.Equal("value", finding.InputName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Required_nullable_input_without_a_default_accepts_supplied_null()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("value", new TypeReference("String"), true, isNullable: true)),
            [Pair("value", Json("null"))]);

        Assert.True(result.IsValid);
        Assert.Equal(JsonValueKind.Null, result.NormalizedInputs["value"].ValueKind);
    }

    [Theory]
    [InlineData("Int32", typeof(int), false)]
    [InlineData("Int32", typeof(int), true)]
    [InlineData("Boolean", typeof(bool), false)]
    [InlineData("Boolean", typeof(bool), true)]
    public void Nullable_optional_value_types_accept_explicit_and_defaulted_null(string alias, Type type, bool useDefault)
    {
        var validator = new WorkflowExecutableInputValidator(Registry((alias, type)));
        var contract = Contract(new WorkflowDeclaredInput("value", new TypeReference(alias), false, Json("null"), isNullable: true));

        var result = validator.Validate(contract, useDefault ? [] : [Pair("value", Json("null"))]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
        Assert.Equal(JsonValueKind.Null, result.NormalizedInputs["value"].ValueKind);
    }

    [Fact]
    public void Explicit_zero_false_and_empty_string_override_literal_defaults()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int)), ("Boolean", typeof(bool)), ("String", typeof(string))));
        var contract = Contract(
            new WorkflowDeclaredInput("count", new TypeReference("Int32"), true, Json("10")),
            new WorkflowDeclaredInput("enabled", new TypeReference("Boolean"), true, Json("true")),
            new WorkflowDeclaredInput("label", new TypeReference("String"), true, Json("\"default\"")));

        var result = validator.Validate(contract,
        [
            Pair("count", Json("0")),
            Pair("enabled", Json("false")),
            Pair("label", Json("\"\""))
        ]);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.NormalizedInputs["count"].GetInt32());
        Assert.False(result.NormalizedInputs["enabled"].GetBoolean());
        Assert.Equal(string.Empty, result.NormalizedInputs["label"].GetString());
    }

    [Fact]
    public void Default_false_nullability_is_omitted_but_true_nullability_is_serialized()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var defaultContract = new WorkflowDeclaredInput("legacy", new TypeReference("String"), false);
        var nullableContract = new WorkflowDeclaredInput("nullable", new TypeReference("String"), false, defaultValue: null, isNullable: true);
        var defaultJson = JsonSerializer.Serialize(defaultContract, options);
        var nullableJson = JsonSerializer.Serialize(nullableContract, options);

        Assert.DoesNotContain("isNullable", defaultJson, StringComparison.Ordinal);
        Assert.Contains("\"isNullable\":true", nullableJson, StringComparison.Ordinal);
        Assert.False(JsonSerializer.Deserialize<WorkflowDeclaredInput>(defaultJson, options)!.IsNullable);
        Assert.True(JsonSerializer.Deserialize<WorkflowDeclaredInput>(nullableJson, options)!.IsNullable);
    }

    [Fact]
    public void Unknown_declared_type_alias_fails_closed()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("payload", new TypeReference("Extension.Missing"), false)),
            [Pair("payload", Json("{}"))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.UnknownTypeAlias, finding.ReasonCode);
        Assert.Equal("payload", finding.InputName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Missing_input_with_supported_literal_default_is_materialized()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int)), ("String", typeof(string))));
        var contract = Contract(
            new WorkflowDeclaredInput("label", new TypeReference("String"), true),
            new WorkflowDeclaredInput("count", new TypeReference("Int32"), true, Json("7")));

        var result = validator.Validate(contract, [Pair("label", Json("\"present\""))]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
        Assert.Equal(new[] { "count", "label" }, result.NormalizedInputs.Keys);
        Assert.Equal(7, result.NormalizedInputs["count"].GetInt32());
    }

    [Fact]
    public void Incompatible_value_is_rejected_without_appearing_in_findings_or_output()
    {
        const string rejectedValue = "not-an-integer-secret";
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), false)),
            [Pair("count", Json($"\"{rejectedValue}\""))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.IncompatibleValue, finding.ReasonCode);
        Assert.Equal("count", finding.InputName);
        Assert.DoesNotContain(rejectedValue, finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public void Unknown_input_contract_version_fails_closed()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var contract = new WorkflowExecutableInputContract(
            WorkflowExecutableInputContract.CurrentVersion + 1,
            [new WorkflowDeclaredInput("value", new TypeReference("String"), false)]);

        var result = validator.Validate(contract, [Pair("value", Json("\"safe\""))]);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.UnsupportedContractVersion, finding.ReasonCode);
        Assert.Null(finding.InputName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Theory]
    [InlineData(CollectionKind.Array)]
    [InlineData(CollectionKind.List)]
    [InlineData(CollectionKind.HashSet)]
    public void Declared_collection_kind_accepts_a_compatible_json_array(CollectionKind collectionKind)
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("values", new TypeReference("Int32", collectionKind), true)),
            [Pair("values", Json("[1,2,3]"))]);

        Assert.True(result.IsValid);
        Assert.Equal(JsonValueKind.Array, result.NormalizedInputs["values"].ValueKind);
    }

    [Fact]
    public void Declared_collection_kind_rejects_a_scalar_json_value()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("values", new TypeReference("Int32", CollectionKind.List), true)),
            [Pair("values", Json("1"))]);

        Assert.False(result.IsValid);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.IncompatibleValue, Assert.Single(result.Findings).ReasonCode);
    }

    [Fact]
    public void Declared_runtime_like_names_remain_ordinary_normalized_workflow_inputs()
    {
        var validator = new WorkflowExecutableInputValidator(Registry(("String", typeof(string))));
        var contract = Contract(
            new WorkflowDeclaredInput("authority", new TypeReference("String"), true),
            new WorkflowDeclaredInput("tenant", new TypeReference("String"), true));

        var result = validator.Validate(
            contract,
            [Pair("tenant", Json("\"input-tenant\"")), Pair("authority", Json("\"input-authority\""))]);

        Assert.True(result.IsValid);
        Assert.Equal("input-authority", result.NormalizedInputs["authority"].GetString());
        Assert.Equal("input-tenant", result.NormalizedInputs["tenant"].GetString());
    }

    [Fact]
    public void Incompatible_literal_default_fails_safely_when_materialization_is_needed()
    {
        const string rejectedDefault = "default-secret";
        var validator = new WorkflowExecutableInputValidator(Registry(("Int32", typeof(int))));
        var result = validator.Validate(
            Contract(new WorkflowDeclaredInput("count", new TypeReference("Int32"), false, Json($"\"{rejectedDefault}\""))),
            Array.Empty<KeyValuePair<string, JsonElement>>());

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.IncompatibleValue, finding.ReasonCode);
        Assert.DoesNotContain(rejectedDefault, finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.NormalizedInputs);
    }

    private static WorkflowExecutableInputContract Contract(params WorkflowDeclaredInput[] inputs) =>
        new(WorkflowExecutableInputContract.CurrentVersion, inputs);

    private static KeyValuePair<string, JsonElement> Pair(string name, JsonElement value) => new(name, value);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IWellKnownTypeRegistry Registry(params (string Alias, Type Type)[] entries) =>
        new TestWellKnownTypeRegistry(entries);

    private sealed class TestWellKnownTypeRegistry(IEnumerable<(string Alias, Type Type)> entries) : IWellKnownTypeRegistry
    {
        private readonly Dictionary<string, Type> _types = entries.ToDictionary(x => x.Alias, x => x.Type, StringComparer.Ordinal);

        public void RegisterType(Type type, string alias) => _types.Add(alias, type);
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = _types.SingleOrDefault(x => x.Value == type).Key!;
            return alias is not null;
        }

        public bool TryGetType(string alias, out Type type) => _types.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => _types.Values;
        public string GetAliasOrDefault(Type type) => _types.SingleOrDefault(x => x.Value == type).Key ?? type.FullName!;
        public Type GetTypeOrDefault(string alias) => _types.GetValueOrDefault(alias, typeof(object));
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
