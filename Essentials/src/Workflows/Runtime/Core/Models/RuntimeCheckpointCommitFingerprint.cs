using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Computes the canonical replay identity of a provider-facing checkpoint bundle.</summary>
public static class RuntimeCheckpointCommitFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ExactBookmarkOptions = ExactOptions();

    private static JsonSerializerOptions ExactOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ExactOpaqueStringWriter());
        return options;
    }

    // Only new conditional replay identities use this encoding. Historical checkpoint bytes remain untouched.
    private sealed class ExactOpaqueStringWriter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(Encode(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(Encode(value));
        private static string Encode(string value)
        {
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
            return Convert.ToBase64String(bytes);
        }
    }


    /// <summary>
    /// Identifies an immutable persisted intent for a delivery-state extension. This is separate from
    /// checkpoint replay identity and grants no authority without checking the exact persisted source.
    /// Opaque string fields retain their UTF-16 code units; in-process conduits are deliberately excluded.
    /// </summary>
    public static string ComputeIntent(RuntimePostCommitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("elsa.post-commit.intent.v1");
        Append(intent.IntentId);
        Append(intent.WorkflowExecutionId);
        Append(intent.Kind);
        Append(intent.RecordedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        Append(intent.ActivityExecutionId);
        Append(intent.IdempotencyKey);
        Append(intent.DependsOnWaitRegistrationId);
        Append(intent.WaitFailurePolicy is { } policy ? ((int)policy).ToString(CultureInfo.InvariantCulture) : null);
        Append(intent.NotBefore?.UtcTicks.ToString(CultureInfo.InvariantCulture));
        if (intent.Payload is { } payload)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteIntentCanonical(payload, writer);
            AppendBytes(stream.ToArray());
        }
        else AppendBytes(null);
        Append(intent.Metadata.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var pair in intent.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Append(pair.Key);
            Append(pair.Value);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        void Append(string? value)
        {
            if (value is null) { AppendBytes(null); return; }
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
            AppendBytes(bytes);
        }

        void AppendBytes(byte[]? bytes)
        {
            Span<byte> header = stackalloc byte[5];
            header[0] = bytes is null ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteInt32BigEndian(header[1..], bytes?.Length ?? 0);
            hash.AppendData(header);
            if (bytes is not null) hash.AppendData(bytes);
        }
    }

    /// <summary>Canonical identity of a model input, independent of transport timestamps and queue envelopes.</summary>
    public static string ComputeModelInput(RuntimeModelExecutionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var payload = JsonSerializer.SerializeToElement(new { Format = "elsa.model.input.v1", Command = command }, ExactBookmarkOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteIntentCanonical(payload, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    // Intent equivalence uses JsonElement.DeepEquals, including exact numeric equivalence. Keep this
    // normalization separate from the historical checkpoint writer, whose existing bytes are durable identities.
    private static void WriteIntentCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteIntentCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray()) WriteIntentCanonical(value, writer);
                writer.WriteEndArray();
                return;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalIntentNumber(element.GetRawText()), skipInputValidation: true);
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static string CanonicalIntentNumber(string token)
    {
        var exponentIndex = token.AsSpan().IndexOfAny('e', 'E');
        var mantissa = exponentIndex < 0 ? token : token[..exponentIndex];
        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];
        var point = mantissa.IndexOf('.');
        var fractionDigits = point < 0 ? 0 : mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var coefficient = digits.TrimEnd('0');
        var exponent = exponentIndex < 0 ? BigInteger.Zero :
            BigInteger.Parse(token.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        exponent += digits.Length - coefficient.Length - fractionDigits;
        return (negative ? "-" : "") + coefficient + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    public static string Compute(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Preserve the exact pre-#676 JSON shape for commits without dispatch state so durable replay markers
        // written by older binaries keep the same fingerprint after upgrade.
        // Consumed scheduler work items only appear on the atomic-ack path, which never existed for older markers.
        // Emit a superset shape that also includes them so the pre-existing branches below stay byte-identical (and old
        // durable replay markers keep the same fingerprint) whenever no work item is consumed.
        object stateChanges = !commit.StateChanges.BackgroundActivityWork.IsEmpty
            ? new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                WorkflowDispatches = Order(commit.StateChanges.WorkflowDispatches),
                WorkflowDispatchCancellations = commit.StateChanges.WorkflowDispatchCancellations
                    .OrderBy(request => request.DispatchId, StringComparer.Ordinal)
                    .ToArray(),
                ActivityScopeCleanups = commit.StateChanges.ActivityScopeCleanups
                    .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                    .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal)
                    .ToArray(),
                ConsumedSchedulerWorkItems = commit.StateChanges.ConsumedSchedulerWorkItems
                    .OrderBy(item => item.WorkItemId, StringComparer.Ordinal)
                    .ToArray(),
                commit.StateChanges.AlterationJobTerminalChange,
                BackgroundActivityWork = new
                {
                    Admissions = commit.StateChanges.BackgroundActivityWork.Admissions
                        .OrderBy(request => request.WorkId, StringComparer.Ordinal).ToArray(),
                    Acceptances = commit.StateChanges.BackgroundActivityWork.Acceptances
                        .OrderBy(request => request.WorkId, StringComparer.Ordinal).ToArray(),
                    Cancellations = commit.StateChanges.BackgroundActivityWork.Cancellations
                        .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                        .ThenBy(request => JsonSerializer.Serialize(request.ActivityExecutionIds), StringComparer.Ordinal)
                        .ToArray()
                },
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : commit.StateChanges.ConsumedSchedulerWorkItems.Count > 0 || commit.StateChanges.AlterationJobTerminalChange is not null
            ? new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                WorkflowDispatches = Order(commit.StateChanges.WorkflowDispatches),
                WorkflowDispatchCancellations = commit.StateChanges.WorkflowDispatchCancellations
                    .OrderBy(request => request.DispatchId, StringComparer.Ordinal)
                    .ToArray(),
                ActivityScopeCleanups = commit.StateChanges.ActivityScopeCleanups
                    .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                    .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal)
                    .ToArray(),
                ConsumedSchedulerWorkItems = commit.StateChanges.ConsumedSchedulerWorkItems
                    .OrderBy(item => item.WorkItemId, StringComparer.Ordinal)
                    .ToArray(),
                commit.StateChanges.AlterationJobTerminalChange,
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : commit.StateChanges.WorkflowDispatchCancellations.Count > 0
            ? new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                WorkflowDispatches = Order(commit.StateChanges.WorkflowDispatches),
                WorkflowDispatchCancellations = commit.StateChanges.WorkflowDispatchCancellations
                    .OrderBy(request => request.DispatchId, StringComparer.Ordinal)
                    .ToArray(),
                ActivityScopeCleanups = commit.StateChanges.ActivityScopeCleanups
                    .OrderBy(request => request.WorkflowExecutionId, StringComparer.Ordinal)
                    .ThenBy(request => request.ExecutionScopeId, StringComparer.Ordinal)
                    .ToArray(),
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : commit.StateChanges.WorkflowDispatches.Count == 0
            ? new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            }
            : new
            {
                commit.StateChanges.WorkflowExecution,
                commit.StateChanges.Scheduler,
                ActivityExecutions = Order(commit.StateChanges.ActivityExecutions),
                ActivityExecutionInspections = Order(commit.StateChanges.ActivityExecutionInspections),
                Bookmarks = Order(commit.StateChanges.Bookmarks),
                DurableValues = Order(commit.StateChanges.DurableValues),
                Incidents = Order(commit.StateChanges.Incidents),
                Operational = Order(commit.StateChanges.Operational),
                WorkflowDispatches = Order(commit.StateChanges.WorkflowDispatches),
                PostCommitOutbox = Order(commit.StateChanges.PostCommitOutbox)
            };

        var payload = new
        {
            commit.CommitId,
            commit.Checkpoint,
            StateChanges = stateChanges,
            PostCommitIntents = commit.PostCommitIntents.OrderBy(x => x.IntentId, StringComparer.Ordinal).ToArray(),
            commit.Metadata
        };

        var element = commit.ExpectedBookmarkResume is null
            ? JsonSerializer.SerializeToElement(payload, SerializerOptions)
            : JsonSerializer.SerializeToElement(new { ExactBookmarkResumeVersion = 1, Payload = payload, commit.ExpectedBookmarkResume }, ExactBookmarkOptions);
        if (commit.ModelState is not null)
            element = JsonSerializer.SerializeToElement(new { ModelStateVersion = 1, Payload = element, commit.ModelState }, ExactBookmarkOptions);
        // ExpectedModelExecution, like ExpectedFence, is an attempt's concurrency condition. A retry can
        // validate a newer snapshot with the same live work. Its immutable semantic address is retained
        // in WorkflowExecutionState.ModelWorkOrigin, which already participates in the decision identity.
        // ExpectedModelParentWait is likewise only a refreshed physical condition. The model-owned state
        // retains its immutable native invocation/result address and participates in this fingerprint.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static RuntimeStateChange<TState>[] Order<TState>(IReadOnlyCollection<RuntimeStateChange<TState>> changes) =>
        changes
            .OrderBy(x => x.StateId, StringComparer.Ordinal)
            .ThenBy(x => x.Operation)
            .ToArray();

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }
}
