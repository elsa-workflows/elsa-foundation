using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>In-memory preparation proof; historical durable replay fingerprints remain unchanged.</summary>
internal static class RuntimePreparedFlushIdentity
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static string Compute(RuntimeCheckpointCommit commit)
    {
        // Bind raw CLR strings before ordinary JSON can replace malformed UTF-16. Refreshable
        // concurrency conditions belong to a physical attempt, outside this prepared logical payload.
        var logical = commit with { ExpectedFence = null, ExpectedModelExecution = null, ExpectedModelParentWait = null };
        var exact = JsonSerializer.Serialize(logical, Options);
        return RuntimeHistoryIdentity.CreateBinding([RuntimeCheckpointCommitFingerprint.Compute(commit), exact]);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ExactStringBindingWriter());
        return options;
    }

    private sealed class ExactStringBindingWriter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(RuntimeHistoryIdentity.CreateBinding([value]));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(RuntimeHistoryIdentity.CreateBinding([value]));
    }
}
