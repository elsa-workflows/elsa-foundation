using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Json;

/// <summary>Strict codecs for the portable public envelope and private integrity receipts.</summary>
public static class PortableCompositionJson
{
    public const string EnvelopeKind = "portable-composition";
    public const string InputReceiptKind = "portable-input-receipt";
    public const string CandidateReceiptKind = "portable-candidate-receipt";
    public const string RequiredInputKind = "workbench-json-bundle";
    public const string SchemaVersion = "1";

    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    public static PortableComposition ParseComposition(ReadOnlySpan<byte> utf8Json)
    {
        using var document = ParseStrict(utf8Json);
        var root = RequireObject(document.RootElement);
        RequireFields(root, "schemaVersion", "kind", "composition", "requiredInput", "inputDisposition");

        if (RequiredString(root, "schemaVersion") != SchemaVersion ||
            RequiredString(root, "kind") != EnvelopeKind)
            throw Invalid();

        var composition = RequiredObject(root, "composition").Clone();
        try
        {
            _ = SelectionJsonReader.ParseComposition(composition.GetRawText());
        }
        catch (SelectionDocumentException)
        {
            throw Invalid();
        }

        var input = RequiredObject(root, "requiredInput");
        RequireFields(input, "kind", "id", "revision");
        var requiredInput = new PortableRequiredInput(
            RequiredExactString(input, "kind", RequiredInputKind),
            RequireVersion4Guid(RequiredString(input, "id")),
            RequireVersion4Guid(RequiredString(input, "revision")));

        return new PortableComposition(
            SchemaVersion,
            EnvelopeKind,
            composition,
            requiredInput,
            ParseDisposition(RequiredString(root, "inputDisposition")));
    }

    public static PortableComposition ParseComposition(string json) => ParseComposition(EncodeStrict(json));

    public static PortableInputReceipt ParseInputReceipt(ReadOnlySpan<byte> utf8Json)
    {
        using var document = ParseStrict(utf8Json);
        var root = RequireObject(document.RootElement);
        RequireFields(root, "schemaVersion", "kind", "inputId", "inputRevision", "publicEnvelopeSha256", "context", "files");
        RequireVersionAndKind(root, InputReceiptKind);
        var context = ParseContext(RequiredObject(root, "context"));
        var receipt = new PortableInputReceipt(
            SchemaVersion,
            InputReceiptKind,
            RequireVersion4Guid(RequiredString(root, "inputId")),
            RequireVersion4Guid(RequiredString(root, "inputRevision")),
            RequireSha256(RequiredString(root, "publicEnvelopeSha256")),
            context,
            ParseFiles(RequiredArray(root, "files")));
        ValidateInputReceipt(receipt);
        return receipt;
    }

    public static PortableInputReceipt ParseInputReceipt(string json) => ParseInputReceipt(EncodeStrict(json));

    public static PortableCandidateReceipt ParseCandidateReceipt(ReadOnlySpan<byte> utf8Json)
    {
        using var document = ParseStrict(utf8Json);
        var root = RequireObject(document.RootElement);
        RequireFields(root, "schemaVersion", "kind", "candidateId", "inputId", "inputRevision", "publicEnvelopeSha256", "context", "inputDisposition", "files");
        RequireVersionAndKind(root, CandidateReceiptKind);
        var receipt = new PortableCandidateReceipt(
            SchemaVersion,
            CandidateReceiptKind,
            RequireVersion4Guid(RequiredString(root, "candidateId")),
            RequireVersion4Guid(RequiredString(root, "inputId")),
            RequireVersion4Guid(RequiredString(root, "inputRevision")),
            RequireSha256(RequiredString(root, "publicEnvelopeSha256")),
            ParseContext(RequiredObject(root, "context")),
            ParseDisposition(RequiredString(root, "inputDisposition")),
            ParseFiles(RequiredArray(root, "files")));
        ValidateCandidateReceipt(receipt);
        return receipt;
    }

    public static PortableCandidateReceipt ParseCandidateReceipt(string json) => ParseCandidateReceipt(EncodeStrict(json));

    public static byte[] SerializeComposition(PortableComposition composition)
    {
        ValidateComposition(composition);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("kind", EnvelopeKind);
            writer.WritePropertyName("composition");
            composition.Composition.WriteTo(writer);
            writer.WritePropertyName("requiredInput");
            writer.WriteStartObject();
            writer.WriteString("kind", RequiredInputKind);
            writer.WriteString("id", composition.RequiredInput.Id);
            writer.WriteString("revision", composition.RequiredInput.Revision);
            writer.WriteEndObject();
            writer.WriteString("inputDisposition", FormatDisposition(composition.InputDisposition));
            writer.WriteEndObject();
        });
    }

    public static byte[] SerializeInputReceipt(PortableInputReceipt receipt)
    {
        ValidateInputReceipt(receipt);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("kind", InputReceiptKind);
            writer.WriteString("inputId", receipt.InputId);
            writer.WriteString("inputRevision", receipt.InputRevision);
            writer.WriteString("publicEnvelopeSha256", receipt.PublicEnvelopeSha256);
            WriteContext(writer, receipt.Context);
            WriteFiles(writer, receipt.Files);
            writer.WriteEndObject();
        });
    }

    public static byte[] SerializeCandidateReceipt(PortableCandidateReceipt receipt)
    {
        ValidateCandidateReceipt(receipt);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("kind", CandidateReceiptKind);
            writer.WriteString("candidateId", receipt.CandidateId);
            writer.WriteString("inputId", receipt.InputId);
            writer.WriteString("inputRevision", receipt.InputRevision);
            writer.WriteString("publicEnvelopeSha256", receipt.PublicEnvelopeSha256);
            WriteContext(writer, receipt.Context);
            writer.WriteString("inputDisposition", FormatDisposition(receipt.InputDisposition));
            WriteFiles(writer, receipt.Files);
            writer.WriteEndObject();
        });
    }

    internal static bool IsSupportedFileName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('\\') || name != Path.GetFileName(name))
            return false;
        if (name.Equals("shells.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        var prefix = name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase)
            ? "shells."
            : name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
                ? "appsettings."
                : null;
        if (prefix is null)
            return false;

        var environment = name[prefix.Length..^5];
        return environment.Length is > 0 and <= 128 &&
               environment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    internal static void ValidateInventory(ImmutableArray<PortableFileDigest> files)
    {
        if (files.IsDefaultOrEmpty)
            throw Invalid();

        var insensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        foreach (var file in files)
        {
            if (file is null || !IsSupportedFileName(file.Name) ||
                !insensitiveNames.Add(file.Name) || RequireSha256(file.Sha256) != file.Sha256 ||
                (previous is not null && StringComparer.Ordinal.Compare(previous, file.Name) >= 0))
                throw Invalid();
            previous = file.Name;
        }
    }

    internal static void ValidateInputReceipt(PortableInputReceipt receipt)
    {
        if (receipt is null || receipt.SchemaVersion != SchemaVersion || receipt.Kind != InputReceiptKind)
            throw Invalid();
        _ = RequireVersion4Guid(receipt.InputId);
        _ = RequireVersion4Guid(receipt.InputRevision);
        _ = RequireSha256(receipt.PublicEnvelopeSha256);
        ValidateContext(receipt.Context);
        ValidateInventory(receipt.Files);
    }

    internal static void ValidateCandidateReceipt(PortableCandidateReceipt receipt)
    {
        if (receipt is null || receipt.SchemaVersion != SchemaVersion || receipt.Kind != CandidateReceiptKind)
            throw Invalid();
        _ = RequireVersion4Guid(receipt.CandidateId);
        _ = RequireVersion4Guid(receipt.InputId);
        _ = RequireVersion4Guid(receipt.InputRevision);
        _ = RequireSha256(receipt.PublicEnvelopeSha256);
        ValidateContext(receipt.Context);
        _ = FormatDisposition(receipt.InputDisposition);
        ValidateInventory(receipt.Files);
    }

    internal static void ValidateComposition(PortableComposition composition)
    {
        if (composition is null || composition.SchemaVersion != SchemaVersion || composition.Kind != EnvelopeKind ||
            composition.Composition.ValueKind != JsonValueKind.Object || composition.RequiredInput is null ||
            composition.RequiredInput.Kind != RequiredInputKind)
            throw Invalid();
        _ = RequireVersion4Guid(composition.RequiredInput.Id);
        _ = RequireVersion4Guid(composition.RequiredInput.Revision);
        _ = FormatDisposition(composition.InputDisposition);
        try
        {
            _ = SelectionJsonReader.ParseComposition(composition.Composition.GetRawText());
        }
        catch (SelectionDocumentException)
        {
            throw Invalid();
        }
    }

    internal static string RequireVersion4Guid(string? value)
    {
        if (value is null || value.Length != 36 || value[14] != '4' ||
            value[19] is not ('8' or '9' or 'a' or 'b') ||
            !Guid.TryParseExact(value, "D", out var guid) ||
            !string.Equals(guid.ToString("D", CultureInfo.InvariantCulture), value, StringComparison.Ordinal))
            throw Invalid();
        return value!;
    }

    internal static string RequireSha256(string? value)
    {
        if (!SelectionValueRules.IsDigest(value))
            throw Invalid();
        return value!;
    }

    private static PortableInputContext ParseContext(JsonElement element)
    {
        RequireFields(element, "shell", "environment");
        var context = new PortableInputContext(RequiredString(element, "shell"), RequiredString(element, "environment"));
        ValidateContext(context);
        return context;
    }

    private static void ValidateContext(PortableInputContext context)
    {
        if (context is null || !SelectionValueRules.IsSafeReference(context.Shell) ||
            string.IsNullOrWhiteSpace(context.Environment) || context.Environment.Length > 128 ||
            !context.Environment.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            throw Invalid();
    }

    private static ImmutableArray<PortableFileDigest> ParseFiles(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw Invalid();
        var files = element.EnumerateArray().Select(value =>
        {
            var file = RequireObject(value);
            RequireFields(file, "name", "sha256");
            return new PortableFileDigest(RequiredString(file, "name"), RequireSha256(RequiredString(file, "sha256")));
        }).ToImmutableArray();
        ValidateInventory(files);
        return files;
    }

    private static void RequireVersionAndKind(JsonElement root, string kind)
    {
        if (RequiredString(root, "schemaVersion") != SchemaVersion || RequiredString(root, "kind") != kind)
            throw Invalid();
    }

    private static string RequiredExactString(JsonElement element, string name, string expected)
    {
        var value = RequiredString(element, name);
        return value == expected ? value : throw Invalid();
    }

    private static PortableInputDisposition ParseDisposition(string value) => value switch
    {
        "origin" => PortableInputDisposition.Origin,
        "replacement" => PortableInputDisposition.Replacement,
        _ => throw Invalid()
    };

    private static string FormatDisposition(PortableInputDisposition disposition) => disposition switch
    {
        PortableInputDisposition.Origin => "origin",
        PortableInputDisposition.Replacement => "replacement",
        _ => throw Invalid()
    };

    private static void WriteContext(Utf8JsonWriter writer, PortableInputContext context)
    {
        writer.WritePropertyName("context");
        writer.WriteStartObject();
        writer.WriteString("shell", context.Shell);
        writer.WriteString("environment", context.Environment);
        writer.WriteEndObject();
    }

    private static void WriteFiles(Utf8JsonWriter writer, ImmutableArray<PortableFileDigest> files)
    {
        writer.WritePropertyName("files");
        writer.WriteStartArray();
        foreach (var file in files)
        {
            writer.WriteStartObject();
            writer.WriteString("name", file.Name);
            writer.WriteString("sha256", file.Sha256);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static JsonDocument ParseStrict(ReadOnlySpan<byte> utf8Json)
    {
        string json;
        try
        {
            json = s_strictUtf8.GetString(utf8Json);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid();
        }

        try
        {
            return SelectionJsonReader.ParseStrict(json);
        }
        catch (SelectionDocumentException)
        {
            throw Invalid();
        }
    }

    private static byte[] EncodeStrict(string json)
    {
        if (json is null)
            throw Invalid();
        try
        {
            return s_strictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static JsonElement RequireObject(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value : throw Invalid();

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? RequireObject(value) : throw Invalid();

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw Invalid();

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw Invalid();
        var result = value.GetString();
        return !string.IsNullOrEmpty(result) ? result : throw Invalid();
    }

    private static void RequireFields(JsonElement element, params string[] allowed)
    {
        var names = allowed.ToHashSet(StringComparer.Ordinal);
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length != allowed.Length || properties.Any(property => !names.Contains(property.Name)))
            throw Invalid();
    }

    private static CompositionImportException Invalid() =>
        new("portable-input-invalid", "The portable artifact is invalid.");
}
