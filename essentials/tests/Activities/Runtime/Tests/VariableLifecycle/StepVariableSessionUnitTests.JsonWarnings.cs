using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

public sealed partial class StepVariableSessionUnitTests
{
    private const string RawValidationValue = """{ "known":1, "SecretBranch": { "n":1.00, "nil":null, "large":9007199254740993, "text":"\u0041 <+>" } }""";

    [Fact]
    public void Retention_only_typed_reads_protect_warning_paths_without_changing_the_stored_value()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(ValidationWarningRecord), "ValidationWarningRecord");
        var type = new ValueTypeDescriptor("ValidationWarningRecord");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, retentionPolicy: "retain");
        using var document = JsonDocument.Parse(RawValidationValue);
        var frame = Root((CountKey, ValueEnvelope.Inline(type, document.RootElement, policy)));
        var session = Session(frame, registry: registry, countType: type);
        var warnings = new ExecutionWarningCollector(); // The actual value policy must protect even this public sink.
        session.SetWarnings(warnings);
        Assert.False(session.HasReadProtectedValue);

        var value = session.Get(new VariableReference<ValidationWarningRecord>("count", "workflow", CountKey));

        Assert.Equal(1, value.Known);
        Assert.True(session.HasReadProtectedValue);
        Assert.Empty(session.Intents);
        Assert.Null(session.PendingFault);
        Assert.Equal(policy, frame.Values[CountKey].Policy);
        Assert.Equal(RawValidationValue, frame.Values[CountKey].InlineValue!.Value.GetRawText());
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal("json-unmatched-member", warning.Code);
        Assert.Equal("variables.count", warning.Details!["path"]);
        Assert.DoesNotContain("SecretBranch", JsonSerializer.Serialize(warning, JsonBaseline.Options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("sensitive")]
    [InlineData("redacted")]
    [InlineData("retained")]
    public void Typed_raw_writes_keep_validation_warnings_and_select_protection_from_the_written_value(string protection)
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(ValidationWarningRecord), "ValidationWarningRecord");
        var type = new ValueTypeDescriptor("ValidationWarningRecord");
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline,
            isSensitive: protection == "sensitive", redactionMode: protection == "redacted" ? "mask" : null,
            retentionPolicy: protection == "retained" ? "retain" : null);
        var frame = Root((CountKey, ValueEnvelope.Null(type, policy)));
        var session = Session(frame, registry: registry, countType: type, validator: new WorkflowExecutableInputValidator(registry));
        var warnings = new ExecutionWarningCollector(); // Protection must come from the written envelope, not this sink.
        session.SetWarnings(warnings);
        using var document = JsonDocument.Parse(RawValidationValue);

        session.Set(new VariableReference<JsonElement>("count", "workflow", CountKey), document.RootElement);

        Assert.Null(session.PendingFault);
        Assert.Equal(ValuePresence.ExplicitNull, frame.Values[CountKey].Presence);
        var intent = Assert.Single(session.Intents);
        Assert.Equal(RawValidationValue, intent.Value.InlineValue!.Value.GetRawText());
        Assert.Equal(policy, intent.Value.Policy);
        var warning = Assert.Single(warnings.Warnings);
        Assert.Equal("json-unmatched-member", warning.Code);
        var diagnostic = JsonSerializer.Serialize(warning, JsonBaseline.Options);
        if (protection == "public") Assert.Contains("SecretBranch", diagnostic, StringComparison.Ordinal);
        else Assert.DoesNotContain("SecretBranch", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("9007199254740993", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_typed_raw_writes_still_fault_before_staging_a_write_or_a_success_warning()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(ValidationWarningRecord), "ValidationWarningRecord");
        var type = new ValueTypeDescriptor("ValidationWarningRecord");
        var frame = Root((CountKey, ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline)));
        var session = Session(frame, registry: registry, countType: type, validator: new WorkflowExecutableInputValidator(registry));
        var warnings = new ExecutionWarningCollector(); session.SetWarnings(warnings);
        using var document = JsonDocument.Parse("{\"known\":\"not an integer\",\"SecretBranch\":1}");

        Assert.Throws<InvalidOperationException>(() => session.Set(new VariableReference<JsonElement>("count", "workflow", CountKey), document.RootElement));

        Assert.NotNull(session.PendingFault);
        Assert.Empty(session.Intents);
        Assert.Empty(warnings.Warnings);
        Assert.Equal(ValuePresence.ExplicitNull, frame.Values[CountKey].Presence);
    }

    [Fact]
    public void Generated_unassigned_enum_JSON_uses_the_relaxed_baseline_without_assigning_the_variable()
    {
        var registry = SerializationServices.TypeRegistry();
        registry.RegisterType(typeof(BaselineDefaultEnum), "BaselineDefaultEnum");
        var type = new ValueTypeDescriptor("BaselineDefaultEnum");
        var frame = Root((CountKey, ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline)));
        var session = Session(frame, registry: registry, countType: type);

        Assert.Equal("\"<+>\"", session.Get(new VariableReference<JsonElement>("count", "workflow", CountKey)).GetRawText());
        Assert.Equal(ValuePresence.Absent, frame.Values[CountKey].Presence);
        Assert.Empty(session.Intents);
    }

    public sealed class ValidationWarningRecord { public int Known { get; set; } }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BaselineDefaultEnum { [JsonStringEnumMemberName("<+>")] Default }
}
