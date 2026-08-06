using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointRecoveryAuthorityTests
{
    private const int KiB = 1024;

    [Fact]
    public void Scheduler_work_authority_has_the_exact_v1_identity_and_domain_separated_fingerprint()
    {
        var item = AuthorityWorkItem.Sample();

        var authority = RecoveryAuthorityTestContract.Encode(item.ToRuntimeWorkItem());

        Assert.Equal(1, RecoveryAuthorityTestContract.Property<int>(authority, "Version"));
        Assert.Equal("runtime.scheduler-work", RecoveryAuthorityTestContract.Property<string>(authority, "Kind"));
        Assert.Equal(item.WorkflowExecutionId, RecoveryAuthorityTestContract.Property<string>(authority, "WorkflowExecutionId"));
        Assert.Equal(item.WorkItemId, RecoveryAuthorityTestContract.Property<string>(authority, "WorkItemId"));
        Assert.Equal(ExpectedFingerprint(item), RecoveryAuthorityTestContract.Property<string>(authority, "Fingerprint"));
        Assert.Equal(
            "elsa.runtime.scheduler-work-authority:v1",
            RecoveryAuthorityTestContract.RequiredConstant<string>(RecoveryAuthorityTestContract.CodecType, "DomainSeparator"));
    }

    [Theory]
    [InlineData("work-item")]
    [InlineData("workflow")]
    [InlineData("command")]
    [InlineData("command-kind")]
    [InlineData("envelope")]
    [InlineData("idempotency")]
    [InlineData("enqueued")]
    [InlineData("recorded")]
    [InlineData("sequence")]
    [InlineData("execution-scope")]
    [InlineData("attempt-number")]
    [InlineData("attempt-first")]
    [InlineData("attempt-previous")]
    [InlineData("payload")]
    [InlineData("command-metadata")]
    [InlineData("envelope-metadata")]
    public void Fingerprint_covers_every_immutable_scheduler_work_field(string field)
    {
        var baseline = AuthorityWorkItem.Sample();
        var changed = baseline.Change(field);

        Assert.NotEqual(Fingerprint(baseline), Fingerprint(changed));
    }

    [Fact]
    public void Canonical_payload_recursively_sorts_objects_preserves_arrays_and_minimizes_valid_numbers()
    {
        var first = AuthorityWorkItem.Sample() with
        {
            Payload = Parse("""{"outer":{"b":1.0,"a":[3,2,1]},"z":true}""")
        };
        var equivalent = first with
        {
            Payload = Parse("""{"z":true,"outer":{"a":[3,2,1],"b":1e0}}""")
        };
        var reorderedArray = first with
        {
            Payload = Parse("""{"outer":{"a":[1,2,3],"b":1},"z":true}""")
        };

        Assert.Equal(Fingerprint(first), Fingerprint(equivalent));
        Assert.NotEqual(Fingerprint(first), Fingerprint(reorderedArray));
    }

    [Theory]
    [InlineData("1", "1.0", "1e0", "1E+0")]
    [InlineData("0", "-0", "-0.0", "0e10")]
    [InlineData("1000", "1e3", "1.000e+3", "1000.000")]
    public void Canonical_payload_uses_one_minimal_representation_for_equivalent_valid_numbers(
        string first,
        string second,
        string third,
        string fourth)
    {
        var fingerprints = new[] { first, second, third, fourth }
            .Select(number => Fingerprint(AuthorityWorkItem.Sample() with { Payload = Parse($$"""{"value":{{number}}}""") }))
            .ToArray();

        Assert.All(fingerprints, fingerprint => Assert.Equal(fingerprints[0], fingerprint));
    }

    [Fact]
    public void Canonical_strings_are_not_Unicode_normalized()
    {
        var composed = AuthorityWorkItem.Sample() with { Payload = Parse("""{"value":"é"}""") };
        var decomposed = AuthorityWorkItem.Sample() with { Payload = Parse("""{"value":"e\u0301"}""") };

        Assert.NotEqual(Fingerprint(composed), Fingerprint(decomposed));
    }

    [Fact]
    public void Equivalent_instants_use_UTC_ticks_in_the_fingerprint()
    {
        var baseline = AuthorityWorkItem.Sample();
        var shifted = baseline with
        {
            EnqueuedAt = baseline.EnqueuedAt.ToOffset(TimeSpan.FromHours(5)),
            RecordedAt = baseline.RecordedAt.ToOffset(TimeSpan.FromHours(-7))
        };

        Assert.Equal(Fingerprint(baseline), Fingerprint(shifted));
    }

    [Theory]
    [InlineData("work-item")]
    [InlineData("workflow")]
    [InlineData("command")]
    [InlineData("envelope")]
    [InlineData("idempotency")]
    [InlineData("execution-scope")]
    [InlineData("attempt-first")]
    [InlineData("attempt-previous")]
    public void Identity_and_lineage_fields_accept_450_UTF16_units_and_reject_451(string field)
    {
        var accepted = AuthorityWorkItem.Sample().WithBoundedField(field, new string('x', 450));
        var rejected = AuthorityWorkItem.Sample().WithBoundedField(field, new string('x', 451));

        _ = RecoveryAuthorityTestContract.Encode(accepted.ToRuntimeWorkItem());
        AssertCodecRejects(rejected);
    }

    [Theory]
    [InlineData("work-item")]
    [InlineData("workflow")]
    [InlineData("command")]
    [InlineData("envelope")]
    [InlineData("idempotency")]
    [InlineData("execution-scope")]
    [InlineData("attempt-first")]
    [InlineData("attempt-previous")]
    public void Required_and_present_identity_or_lineage_fields_reject_blank_and_whitespace(string field)
    {
        AssertCodecRejects(AuthorityWorkItem.Sample().WithBoundedField(field, string.Empty));
        AssertCodecRejects(AuthorityWorkItem.Sample().WithBoundedField(field, " \t "));
    }

    [Theory]
    [InlineData("command")]
    [InlineData("envelope")]
    public void Both_metadata_maps_enforce_entry_key_and_value_bounds_independently(string map)
    {
        var sixtyFour = Enumerable.Range(0, 64).ToDictionary(index => $"k{index:D2}", _ => "v", StringComparer.Ordinal);
        _ = RecoveryAuthorityTestContract.Encode(WithMetadata(AuthorityWorkItem.Sample(), map, sixtyFour).ToRuntimeWorkItem());
        _ = RecoveryAuthorityTestContract.Encode(WithMetadata(AuthorityWorkItem.Sample(), map,
            new Dictionary<string, string> { [new string('k', 128)] = new string('v', 4096) }).ToRuntimeWorkItem());

        AssertCodecRejects(WithMetadata(AuthorityWorkItem.Sample(), map,
            Enumerable.Range(0, 65).ToDictionary(index => $"k{index:D2}", _ => "v", StringComparer.Ordinal)));
        AssertCodecRejects(WithMetadata(AuthorityWorkItem.Sample(), map,
            new Dictionary<string, string> { [new string('k', 129)] = "v" }));
        AssertCodecRejects(WithMetadata(AuthorityWorkItem.Sample(), map,
            new Dictionary<string, string> { ["k"] = new string('v', 4097) }));
        AssertCodecRejects(WithMetadata(AuthorityWorkItem.Sample(), map,
            new Dictionary<string, string> { [string.Empty] = "v" }));
        AssertCodecRejects(WithMetadata(AuthorityWorkItem.Sample(), map,
            new Dictionary<string, string> { [" \t "] = "v" }));
    }

    [Fact]
    public void Payload_enforces_256KiB_canonical_UTF8_and_depth_64()
    {
        const int emptyStringDocumentBytes = 8; // {"v":""}
        var exactPayload = Parse($$"""{"v":"{{new string('x', 256 * KiB - emptyStringDocumentBytes)}}"}""");
        var oversizedPayload = Parse($$"""{"v":"{{new string('x', 256 * KiB - emptyStringDocumentBytes + 1)}}"}""");
        var depth64 = Parse(NestedObject(64), maxDepth: 128);
        var depth65 = Parse(NestedObject(65), maxDepth: 128);

        _ = RecoveryAuthorityTestContract.Encode((AuthorityWorkItem.Sample() with { Payload = exactPayload }).ToRuntimeWorkItem());
        _ = RecoveryAuthorityTestContract.Encode((AuthorityWorkItem.Sample() with { Payload = depth64 }).ToRuntimeWorkItem());
        AssertCodecRejects(AuthorityWorkItem.Sample() with { Payload = oversizedPayload });
        AssertCodecRejects(AuthorityWorkItem.Sample() with { Payload = depth65 });
    }

    [Fact]
    public void Complete_canonical_input_accepts_exactly_512KiB_and_rejects_one_byte_more()
    {
        var exact = WithCanonicalInputSize(512 * KiB);
        var oversized = WithCanonicalInputSize(512 * KiB + 1);

        Assert.Equal(512 * KiB, CanonicalFingerprintInput(exact).Length);
        Assert.Equal(512 * KiB + 1, CanonicalFingerprintInput(oversized).Length);
        _ = RecoveryAuthorityTestContract.Encode(exact.ToRuntimeWorkItem());
        AssertCodecRejects(oversized);
    }

    private static string Fingerprint(AuthorityWorkItem item) =>
        RecoveryAuthorityTestContract.Property<string>(
            RecoveryAuthorityTestContract.Encode(item.ToRuntimeWorkItem()),
            "Fingerprint");

    private static void AssertCodecRejects(AuthorityWorkItem item)
    {
        _ = RecoveryAuthorityTestContract.CodecType;
        Assert.ThrowsAny<ArgumentException>(() => RecoveryAuthorityTestContract.Encode(item.ToRuntimeWorkItem()));
    }

    private static JsonElement Parse(string json, int maxDepth = 64)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = maxDepth });
        return document.RootElement.Clone();
    }

    private static string NestedObject(int depth) =>
        string.Concat(Enumerable.Repeat("{\"x\":", depth)) + "0" + new string('}', depth);

    private static string ExpectedFingerprint(AuthorityWorkItem item) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(CanonicalFingerprintInput(item)))}";

    private static byte[] CanonicalFingerprintInput(AuthorityWorkItem item)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("workItemId", item.WorkItemId);
            writer.WriteString("workflowExecutionId", item.WorkflowExecutionId);
            writer.WriteString("commandId", item.CommandId);
            writer.WriteNumber("commandKind", (int)item.CommandKind);
            writer.WriteString("envelopeId", item.EnvelopeId);
            writer.WriteString("idempotencyKey", item.IdempotencyKey);
            writer.WriteNumber("enqueuedAtUtcTicks", item.EnqueuedAt.UtcTicks);
            writer.WriteNumber("recordedAtUtcTicks", item.RecordedAt.UtcTicks);
            if (item.Sequence is { } sequence)
                writer.WriteNumber("sequence", sequence);
            else
                writer.WriteNull("sequence");
            if (item.ExecutionScopeId is { } executionScopeId)
                writer.WriteString("executionScopeId", executionScopeId);
            else
                writer.WriteNull("executionScopeId");
            writer.WritePropertyName("attempt");
            WriteAttempt(writer, item.Attempt);
            writer.WritePropertyName("payload");
            WriteCanonical(writer, item.Payload);
            writer.WritePropertyName("commandMetadata");
            WriteMetadata(writer, item.CommandMetadata);
            writer.WritePropertyName("envelopeMetadata");
            WriteMetadata(writer, item.EnvelopeMetadata);
            writer.WriteEndObject();
        }

        var domain = Encoding.UTF8.GetBytes("elsa.runtime.scheduler-work-authority:v1\n");
        var input = new byte[domain.Length + stream.Length];
        domain.CopyTo(input, 0);
        stream.Position = 0;
        _ = stream.Read(input, domain.Length, checked((int)stream.Length));
        return input;
    }

    private static AuthorityWorkItem WithMetadata(
        AuthorityWorkItem item,
        string map,
        IReadOnlyDictionary<string, string> metadata) => map switch
    {
        "command" => item with { CommandMetadata = metadata },
        "envelope" => item with { EnvelopeMetadata = metadata },
        _ => throw new ArgumentOutOfRangeException(nameof(map), map, null)
    };

    private static AuthorityWorkItem WithCanonicalInputSize(int targetBytes)
    {
        var command = Enumerable.Range(0, 64).ToDictionary(index => $"c{index:D2}", _ => string.Empty, StringComparer.Ordinal);
        var envelope = Enumerable.Range(0, 64).ToDictionary(index => $"e{index:D2}", _ => string.Empty, StringComparer.Ordinal);
        var item = AuthorityWorkItem.Sample() with
        {
            Payload = Parse("""{"v":""}"""),
            CommandMetadata = command,
            EnvelopeMetadata = envelope
        };
        var remaining = targetBytes - CanonicalFingerprintInput(item).Length;
        Assert.True(remaining >= 0);

        const int payloadCapacity = 256 * KiB - 8;
        var payloadLength = Math.Min(payloadCapacity, remaining);
        remaining -= payloadLength;
        item = item with { Payload = Parse($$"""{"v":"{{new string('p', payloadLength)}}"}""") };

        foreach (var metadata in new[] { command, envelope })
        foreach (var key in metadata.Keys.ToArray())
        {
            var length = Math.Min(4096, remaining);
            metadata[key] = new string('m', length);
            remaining -= length;
        }

        Assert.Equal(0, remaining);
        return item;
    }

    private static void WriteAttempt(Utf8JsonWriter writer, ActivityExecutionAttemptLineage? attempt)
    {
        if (attempt is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("attemptNumber", attempt.AttemptNumber);
        writer.WriteString("firstAttemptActivityExecutionId", attempt.FirstAttemptActivityExecutionId);
        if (attempt.PreviousAttemptActivityExecutionId is { } previous)
            writer.WriteString("previousAttemptActivityExecutionId", previous);
        else
            writer.WriteNull("previousAttemptActivityExecutionId");
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> metadata)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            writer.WriteString(key, value);
        writer.WriteEndObject();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement? element)
    {
        if (element is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.Value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.Value.EnumerateArray())
                    WriteCanonical(writer, child);
                writer.WriteEndArray();
                return;
            case JsonValueKind.Number:
                if (element.Value.TryGetInt64(out var integer))
                    writer.WriteNumberValue(integer);
                else if (element.Value.TryGetDecimal(out var number))
                    writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                else
                    writer.WriteRawValue(element.Value.GetDouble().ToString("R", CultureInfo.InvariantCulture));
                return;
            default:
                element.Value.WriteTo(writer);
                return;
        }
    }
}

internal static class RecoveryAuthorityTestContract
{
    private const string Namespace = "Elsa.Workflows.Runtime.Core";

    public static Type AuthorityType => RequiredType($"{Namespace}.Models.RuntimeCheckpointRecoveryAuthority");
    public static Type CodecType => RequiredType($"{Namespace}.Models.RuntimeCheckpointRecoveryAuthorityCodec");
    public static Type AccessorContractType => RequiredType($"{Namespace}.Contracts.IRuntimeCheckpointRecoveryAuthorityAccessor");
    public static Type AccessorType => typeof(RuntimeCheckpointCommitter).Assembly.GetType(
        $"{Namespace}.Services.AmbientCheckpointRecoveryAuthorityAccessor",
        throwOnError: false) ?? throw new InvalidOperationException(
        "Required Path A accessor type 'Elsa.Workflows.Runtime.Core.Services.AmbientCheckpointRecoveryAuthorityAccessor' is missing.");

    public static object Encode(RuntimeSchedulerWorkItem item)
    {
        var method = CodecType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(candidate =>
                candidate.ReturnType == AuthorityType &&
                candidate.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(RuntimeSchedulerWorkItem));
        Assert.True(method is not null,
            $"{CodecType.FullName} must expose one public scheduler-work codec method taking RuntimeSchedulerWorkItem and returning RuntimeCheckpointRecoveryAuthority.");
        var target = method!.IsStatic ? null : Activator.CreateInstance(CodecType);
        try
        {
            return method.Invoke(target, [item])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static T Property<T>(object value, string name)
    {
        var property = value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(property is not null, $"{value.GetType().FullName} must expose public {name}.");
        return Assert.IsType<T>(property!.GetValue(value));
    }

    public static T RequiredConstant<T>(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.True(field is not null && field.IsLiteral, $"{type.FullName}.{name} must be a public constant.");
        return Assert.IsType<T>(field!.GetRawConstantValue());
    }

    public static Type RequiredType(string fullName)
    {
        var type = typeof(RuntimeSchedulerWorkItem).Assembly.GetType(fullName, throwOnError: false);
        Assert.True(type is not null, $"Required Path A contract type '{fullName}' is missing.");
        return type!;
    }
}

internal sealed record AuthorityWorkItem(
    string WorkItemId,
    string WorkflowExecutionId,
    string CommandId,
    WorkflowExecutionCommandKind CommandKind,
    string EnvelopeId,
    string IdempotencyKey,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset RecordedAt,
    long? Sequence,
    JsonElement? Payload,
    IReadOnlyDictionary<string, string> CommandMetadata,
    IReadOnlyDictionary<string, string> EnvelopeMetadata,
    string? ExecutionScopeId,
    ActivityExecutionAttemptLineage? Attempt)
{
    public static AuthorityWorkItem Sample() => new(
        "work-α",
        "workflow-1",
        "command-1",
        WorkflowExecutionCommandKind.ScheduleActivity,
        "envelope-1",
        "idempotency-1",
        new DateTimeOffset(2026, 8, 6, 12, 30, 1, TimeSpan.Zero).AddTicks(23),
        new DateTimeOffset(2026, 8, 6, 12, 30, 2, TimeSpan.Zero).AddTicks(47),
        17,
        ParseSample("""{"b":[2,1],"a":{"z":false,"n":1}}"""),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["z"] = "last", ["a"] = "first" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["β"] = "two", ["a"] = "one" },
        "scope-1",
        new ActivityExecutionAttemptLineage(3, "activity-first", "activity-previous"));

    public RuntimeSchedulerWorkItem ToRuntimeWorkItem() => new(
        WorkItemId,
        WorkflowExecutionId,
        CommandId,
        CommandKind,
        EnvelopeId,
        IdempotencyKey,
        EnqueuedAt,
        RecordedAt,
        Sequence,
        Payload,
        CommandMetadata,
        EnvelopeMetadata,
        ExecutionScopeId,
        Attempt);

    public AuthorityWorkItem Change(string field) => field switch
    {
        "work-item" => this with { WorkItemId = "work-2" },
        "workflow" => this with { WorkflowExecutionId = "workflow-2" },
        "command" => this with { CommandId = "command-2" },
        "command-kind" => this with { CommandKind = WorkflowExecutionCommandKind.StartActivity },
        "envelope" => this with { EnvelopeId = "envelope-2" },
        "idempotency" => this with { IdempotencyKey = "idempotency-2" },
        "enqueued" => this with { EnqueuedAt = EnqueuedAt.AddTicks(1) },
        "recorded" => this with { RecordedAt = RecordedAt.AddTicks(1) },
        "sequence" => this with { Sequence = null },
        "execution-scope" => this with { ExecutionScopeId = "scope-2" },
        "attempt-number" => this with { Attempt = Attempt! with { AttemptNumber = 4 } },
        "attempt-first" => this with { Attempt = Attempt! with { FirstAttemptActivityExecutionId = "activity-other" } },
        "attempt-previous" => this with { Attempt = Attempt! with { PreviousAttemptActivityExecutionId = null } },
        "payload" => this with { Payload = ParseSample("""{"a":{"n":2,"z":false},"b":[2,1]}""") },
        "command-metadata" => this with { CommandMetadata = new Dictionary<string, string> { ["a"] = "changed", ["z"] = "last" } },
        "envelope-metadata" => this with { EnvelopeMetadata = new Dictionary<string, string> { ["a"] = "changed", ["β"] = "two" } },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    public AuthorityWorkItem WithBoundedField(string field, string value) => field switch
    {
        "work-item" => this with { WorkItemId = value },
        "workflow" => this with { WorkflowExecutionId = value },
        "command" => this with { CommandId = value },
        "envelope" => this with { EnvelopeId = value },
        "idempotency" => this with { IdempotencyKey = value },
        "execution-scope" => this with { ExecutionScopeId = value },
        "attempt-first" => this with { Attempt = Attempt! with { FirstAttemptActivityExecutionId = value } },
        "attempt-previous" => this with { Attempt = Attempt! with { PreviousAttemptActivityExecutionId = value } },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static JsonElement ParseSample(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
