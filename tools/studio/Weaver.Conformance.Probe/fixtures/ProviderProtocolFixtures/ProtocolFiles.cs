using System.Collections.Concurrent;
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

    public ProtocolFiles(string commandFile, string traceFile)
    {
        if (!Path.IsPathFullyQualified(commandFile) || !Path.IsPathFullyQualified(traceFile))
            throw new ProtocolFixtureException("CommandFile and TraceFile must be explicit absolute private paths.");
        this.commandFile = Path.GetFullPath(commandFile);
        this.traceFile = Path.GetFullPath(traceFile);
        if (string.Equals(this.commandFile, this.traceFile, StringComparison.Ordinal))
            throw new ProtocolFixtureException("CommandFile and TraceFile must be different files.");
    }

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
