using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Acme.Weaver.ProtocolFixtures;

public sealed class ProtocolFixtureException(string message, Exception? innerException = null) : Exception(message, innerException)
{
}

internal sealed record ProtocolCommand(string CommandId, string ToolName, JsonElement Arguments)
{
    public Dictionary<string, object?> ToArguments() => Arguments.EnumerateObject()
        .ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
}

internal sealed class ProtocolFiles
{
    private static readonly ConcurrentDictionary<string, object> TraceLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string commandFile;
    private readonly string traceFile;
    private readonly string? gateFile;

    public ProtocolFiles(string commandFile, string traceFile, string? gateFile = null)
    {
        if (!Path.IsPathFullyQualified(commandFile) || !Path.IsPathFullyQualified(traceFile))
            throw new ProtocolFixtureException("CommandFile and TraceFile must be explicit absolute private paths.");
        this.commandFile = Path.GetFullPath(commandFile);
        this.traceFile = Path.GetFullPath(traceFile);
        if (string.Equals(this.commandFile, this.traceFile, StringComparison.Ordinal))
            throw new ProtocolFixtureException("CommandFile and TraceFile must be different files.");
        if (!string.IsNullOrWhiteSpace(gateFile))
        {
            if (!Path.IsPathFullyQualified(gateFile))
                throw new ProtocolFixtureException("GateFile must be an explicit absolute private path when configured.");
            this.gateFile = Path.GetFullPath(gateFile);
            if (this.gateFile == this.commandFile || this.gateFile == this.traceFile)
                throw new ProtocolFixtureException("GateFile must differ from CommandFile and TraceFile.");
        }
    }

    public async Task BeforeCallAsync(string provider, ProtocolCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (gateFile is null) return;
        var gate = ReadGate();
        cancellationToken.ThrowIfCancellationRequested();
        if (gate.CommandId != command.CommandId) return;
        if (gate.State == "release") return;
        Trace("protocol.gate.waiting", new { provider, command.CommandId, gate.Epoch, gate.TimeoutMilliseconds });
        var started = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = TimeSpan.FromMilliseconds(gate.TimeoutMilliseconds) - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    Trace("protocol.gate.timedOut", new { provider, command.CommandId, gate.Epoch });
                    throw new ProtocolFixtureException("The bounded private pre-call gate expired without release.");
                }
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(20) ? remaining : TimeSpan.FromMilliseconds(20), cancellationToken);
                var current = ReadGate();
                if (current.CommandId != gate.CommandId || current.Epoch != gate.Epoch)
                {
                    Trace("protocol.gate.refused", new { provider, command.CommandId, gate.Epoch, reason = "identityChanged" });
                    throw new ProtocolFixtureException("The waiting pre-call gate command or epoch changed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(gate.TimeoutMilliseconds))
                {
                    Trace("protocol.gate.timedOut", new { provider, command.CommandId, gate.Epoch });
                    throw new ProtocolFixtureException("The bounded private pre-call gate expired without release.");
                }
                if (current.State != "release") continue;
                Trace("protocol.gate.released", new { provider, command.CommandId, gate.Epoch });
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Trace("protocol.gate.cancelled", new { provider, command.CommandId, gate.Epoch });
            throw;
        }
    }

    private PreCallGate ReadGate()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(gateFile!), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ProtocolFixtureException("The private pre-call gate must be an object.");
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length is < 3 or > 4 || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                names.Any(name => name is not ("commandId" or "epoch" or "state" or "timeoutMilliseconds")) ||
                !root.TryGetProperty("commandId", out var command) || !root.TryGetProperty("epoch", out var epoch) ||
                !root.TryGetProperty("state", out var state))
                throw new ProtocolFixtureException("The private pre-call gate requires commandId, epoch, state and an optional bounded timeoutMilliseconds.");
            var id = command.GetString(); var generation = epoch.GetString(); var action = state.GetString();
            var timeout = root.TryGetProperty("timeoutMilliseconds", out var configured) ? configured.GetInt32() : 60_000;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(generation) || action is not ("hold" or "release") || timeout is < 1 or > 60_000)
                throw new ProtocolFixtureException("The private pre-call gate needs nonempty identities, hold/release state and a timeout within 1..60000 milliseconds.");
            return new(id, generation, action, timeout);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            throw new ProtocolFixtureException("The private pre-call gate could not be read.", error);
        }
    }

    private sealed record PreCallGate(string CommandId, string Epoch, string State, int TimeoutMilliseconds);

    public ProtocolCommand ReadCommand()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(commandFile), new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProtocolFixtureException("Protocol command must be a JSON object.");
            var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (properties.Length != 3 || properties.Distinct(StringComparer.Ordinal).Count() != 3 ||
                properties.Any(name => name is not ("commandId" or "toolName" or "arguments")))
                throw new ProtocolFixtureException("Protocol command requires exactly commandId, toolName and arguments.");
            var commandId = root.GetProperty("commandId").GetString();
            var toolName = root.GetProperty("toolName").GetString();
            var arguments = root.GetProperty("arguments");
            if (string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(toolName) || arguments.ValueKind != JsonValueKind.Object)
                throw new ProtocolFixtureException("Protocol command needs nonempty identities and object arguments.");
            var argumentNames = arguments.EnumerateObject().Select(property => property.Name).ToArray();
            if (argumentNames.Distinct(StringComparer.Ordinal).Count() != argumentNames.Length)
                throw new ProtocolFixtureException("Protocol arguments cannot contain duplicate property names.");
            return new ProtocolCommand(commandId, toolName, arguments.Clone());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new ProtocolFixtureException("The private protocol command could not be read.", exception);
        }
    }

    public void Trace(string kind, object value)
    {
        // The driver creates the private evidence directory; this fixture never invents a destination.
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        Redact(node);
        var record = JsonSerializer.Serialize(new { evidenceKind = "deterministic-provider-protocol", kind, at = DateTimeOffset.UtcNow, data = node }, JsonOptions);
        lock (TraceLocks.GetOrAdd(traceFile, _ => new object()))
        {
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.Read };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var stream = new FileStream(traceFile, options);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(traceFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(record);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ProtocolFixtureException("The private protocol trace could not be written.", exception);
            }
        }
    }

    public static string OutcomeText(object? result)
    {
        // Hashing correlates opaque transport content without inventing a domain success summary.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
        return "Tool outcome received (opaque SHA-256 " + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ").";
    }

    private static bool CredentialName(string name) => name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("password", StringComparison.OrdinalIgnoreCase) || name.Equals("apiKey", StringComparison.OrdinalIgnoreCase)
        || name.Equals("gitHubToken", StringComparison.OrdinalIgnoreCase) || name.Equals("runtimeConnectionToken", StringComparison.OrdinalIgnoreCase)
        || name.Equals("accessToken", StringComparison.OrdinalIgnoreCase) || name.Equals("refreshToken", StringComparison.OrdinalIgnoreCase)
        || name.Equals("cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase);

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject item)
            foreach (var key in item.Select(property => property.Key).ToArray())
            {
                if (CredentialName(key)) item[key] = "[redacted]";
                else if (item[key] is JsonValue value && value.TryGetValue<string>(out var text)) item[key] = RedactText(text);
                else Redact(item[key]);
            }
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = RedactText(text);
                else Redact(array[index]);
            }
    }

    private static string RedactText(string text)
    {
        text = Regex.Replace(text, @"(?i)\bBearer\s+[^\s""\\]+", "Bearer [redacted]", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{12,}", "[redacted]", RegexOptions.CultureInvariant);
        return Regex.Replace(text, "(?i)(\"(?:authorization|password|apiKey|gitHubToken|runtimeConnectionToken|accessToken|refreshToken|cookie|set-cookie)\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"", "$1\"[redacted]\"", RegexOptions.CultureInvariant);
    }
}
