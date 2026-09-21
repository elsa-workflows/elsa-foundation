using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// One module's place in a <c>script</c> run, shared by <c>plan</c> (which stops here) and <c>script</c>
/// (which adds a file to it).
/// </summary>
internal sealed record EfModulePlanEntry(
    int Order,
    EfModuleDescriptor Descriptor,
    string Context,
    IReadOnlyList<string> MigrationIds,
    IReadOnlyList<IEfPostMigrationAction> PostMigration)
{
    public string Module => Descriptor.Name;

    public string Assembly => Descriptor.Assembly.GetName().Name!;

    public string HistoryTable => Descriptor.HistoryTableName;

    public IReadOnlyList<string> DependsOn => EfModuleOrder.Dependencies(Descriptor);

    /// <summary>Null rather than an invented id when a module has no migrations for this provider yet.</summary>
    public string? To => MigrationIds.Count == 0 ? null : MigrationIds[^1];
}

/// <summary>One module's generated file, held in memory until every module has one.</summary>
internal sealed record EfModuleArtifact(EfModulePlanEntry Entry, string File, byte[] Content, EfToolingPackageFacts Package)
{
    public string Sha256 => EfMigrationPlan.Sha256(Content);
}

/// <summary>The top-level facts a manifest records that are not per module.</summary>
internal sealed record EfMigrationPlanFacts(
    string Provider,
    EfToolingEngineFacts Engine,
    string EfCoreVersion,
    string? Schema,
    EfToolingHostFacts Host);

/// <summary>
/// UTF-8 without a byte-order mark, LF endings, one trailing newline — the exact normalization
/// <c>script</c> applies to every file it writes. EF builds its script with <c>Environment.NewLine</c>, so
/// an unnormalized run on Windows would commit CRLF and a run on Linux LF for the same model — which is
/// exactly the difference <c>script-check</c> would then report as a hand edit. Public so the determinism
/// this normalizer enforces can be verified directly, the way the rest of the <c>Tooling</c> namespace is
/// an entry point other processes call into.
/// </summary>
public static class EfToolingLineEndings
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Normalizes <paramref name="content"/> to UTF-8 without a byte-order mark and LF-only line endings.</summary>
    public static byte[] Utf8Lf(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!normalized.EndsWith('\n'))
            normalized += "\n";
        return Utf8NoBom.GetBytes(normalized);
    }
}

/// <summary>
/// Defense in depth against the one value this build must never surface (D7): whatever an underlying
/// exception's message says — a driver's own error can legitimately echo part of a connection string —
/// every occurrence of <paramref name="connection"/> itself is replaced before the text reaches a refusal,
/// a response, or anywhere else an operator or a log could read it. The exact-match replacement alone only
/// catches a verbatim echo; a driver that re-serialises, re-cases or re-quotes the connection string before
/// including it in a message would slip through, so this also scrubs any <c>Password=</c>/<c>Pwd=</c>
/// key/value pair, case-insensitively and bounded by the usual <c>;</c> separator, regardless of how the
/// rest of the text around it was reformatted. Anything less regular than that — a reformatting this
/// pattern does not recognise — is not something a fixed pattern can chase, and is not the guarantee this
/// defends: the guarantee that matters is that the value never reaches <c>argv</c> in the first place,
/// which is proved elsewhere. Public so this scrubbing can be verified directly, the way the rest of the
/// <c>Tooling</c> namespace is an entry point other processes call into.
/// </summary>
public static class EfToolingRedaction
{
    private static readonly Regex CredentialPair = new(
        @"\b(?<key>password|pwd)\s*=\s*[^;]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Scrubs every occurrence of <paramref name="connection"/> and any <c>Password=</c>/<c>Pwd=</c> pair from <paramref name="text"/>.</summary>
    public static string Redact(string text, string connection)
    {
        if (!string.IsNullOrEmpty(connection))
            text = text.Replace(connection, "<connection-redacted>", StringComparison.Ordinal);

        return CredentialPair.Replace(text, match => $"{match.Groups["key"].Value}=<redacted>");
    }
}

/// <summary>
/// The deterministic artifact <c>script</c> writes: flat <c>NN-&lt;slug&gt;.sql</c> files plus one
/// <c>migration-plan.json</c> (D6, FR-040, FR-047–FR-049). Determinism is the deliverable here, not a
/// nicety — LF endings, UTF-8 without a byte-order mark, a fixed JSON key order, and no timestamp,
/// absolute path, tool version or connection string anywhere — so every byte an operator commits is
/// produced through this one type.
/// </summary>
internal static class EfMigrationPlan
{
    public const string FileName = "migration-plan.json";
    public const int SchemaVersion = 1;
    public const string Ordering = "dependsOn-then-name";

    /// <summary>The canonical name lower-cased with dots replaced by hyphens.</summary>
    public static string Slug(string module) => module.Replace('.', '-').ToLowerInvariant();

    public static string ScriptFileName(int order, string module) =>
        string.Create(CultureInfo.InvariantCulture, $"{order:D2}-{Slug(module)}.sql");

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>Renders the manifest in the fixed key order FR-047 and FR-048 name, in the same encoding as the SQL beside it.</summary>
    public static byte[] Render(EfMigrationPlanFacts facts, IReadOnlyList<EfModuleArtifact> modules)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, IndentCharacter = ' ', IndentSize = 2, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("provider", facts.Provider);
            writer.WriteStartObject("engine");
            writer.WriteString("package", facts.Engine.Package);
            writer.WriteString("version", facts.Engine.Version);
            writer.WriteString("source", facts.Engine.Source);
            writer.WriteEndObject();
            writer.WriteString("efCoreVersion", facts.EfCoreVersion);
            writer.WriteString("schema", facts.Schema);
            writer.WriteBoolean("idempotent", true);
            writer.WriteString("ordering", Ordering);
            writer.WriteStartObject("host");
            writer.WriteString("name", facts.Host.Name);
            writer.WriteString("providerAgreement", facts.Host.ProviderAgreement);
            writer.WriteString("providerAgreementNote", EfToolingProviderAgreement.Note);
            writer.WriteString("shell", facts.Host.Shell);
            writer.WriteString("environment", facts.Host.Environment);
            writer.WriteEndObject();
            writer.WriteStartArray("modules");
            foreach (var module in modules)
                WriteModule(writer, module, facts.Provider);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return [.. buffer.WrittenSpan, (byte)'\n'];
    }

    /// <summary>
    /// Writes the artifact, once every file exists in memory. Whether a stale <c>.sql</c> file already sits
    /// in the output directory is <c>script-check</c>'s call to make (FR-045), not this command's: nothing
    /// already in the directory is ever deleted or refused here.
    /// </summary>
    public static void Write(string output, IReadOnlyList<EfModuleArtifact> modules, byte[] manifest)
    {
        foreach (var module in modules)
            ValidateBareFileName(module.Entry.Module, module.File);

        try
        {
            Directory.CreateDirectory(output);
            foreach (var module in modules)
                File.WriteAllBytes(Path.Combine(output, module.File), module.Content);
            File.WriteAllBytes(Path.Combine(output, FileName), manifest);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw EfToolingRefusal.Resolution("output-write-failed", $"The artifact could not be written: {failure.Message}");
        }
    }

    /// <summary>
    /// Refuses rather than lets a rooted or directory-separator-carrying name reach
    /// <see cref="File.WriteAllBytes(string,byte[])"/>, where it would either escape <paramref name="name"/>'s
    /// intended output directory or fail deep inside the write with an opaque <c>internal-error</c>. First-party
    /// module names can never trigger this — <see cref="ScriptFileName"/> always prefixes an ordinal and
    /// <see cref="Slug"/> maps every <c>.</c> to <c>-</c> — but spec 171 User Story 6 admits third-party
    /// modules, and nothing stops a third-party <c>[EfModule]</c> name from containing a directory separator.
    /// </summary>
    private static void ValidateBareFileName(string module, string name)
    {
        if (Path.IsPathRooted(name) || Path.GetFileName(name) != name)
            throw EfToolingRefusal.Resolution(
                "module-file-name-invalid",
                $"Module '{module}' would write to '{name}', which is not a bare file name.");
    }

    private static void WriteModule(Utf8JsonWriter writer, EfModuleArtifact module, string provider)
    {
        var entry = module.Entry;
        writer.WriteStartObject();
        writer.WriteNumber("order", entry.Order);
        writer.WriteString("module", entry.Module);
        writer.WriteString("file", module.File);
        writer.WriteString("sha256", module.Sha256);
        writer.WriteStartObject("package");
        writer.WriteString("id", module.Package.Id);
        writer.WriteString("version", module.Package.Version);
        writer.WriteString("source", module.Package.Source);
        writer.WriteEndObject();
        writer.WriteString("assembly", entry.Assembly);
        writer.WriteString("context", entry.Context);
        writer.WriteString("historyTable", entry.HistoryTable);
        writer.WriteStartObject("migrations");
        writer.WriteString("from", "0");
        writer.WriteString("to", entry.To);
        writer.WriteNumber("count", entry.MigrationIds.Count);
        writer.WriteStartArray("ids");
        foreach (var id in entry.MigrationIds)
            writer.WriteStringValue(id);
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteStartArray("dependsOn");
        foreach (var dependency in entry.DependsOn)
            writer.WriteStringValue(dependency);
        writer.WriteEndArray();
        // FR-048's {id, kind, requiredWhen, audit, run}. An empty array here means the module genuinely
        // declares nothing — a module that declares an action this build cannot describe is refused before
        // generation rather than recorded as having nothing left to run.
        writer.WriteStartArray("postMigration");
        foreach (var action in entry.PostMigration)
        {
            writer.WriteStartObject();
            writer.WriteString("id", action.Id);
            writer.WriteString("kind", action.Kind);
            writer.WriteString("requiredWhen", action.RequiredWhen);
            writer.WriteString("audit", action.Audit);
            writer.WriteString("run", EfPostMigrationActions.CommandFor(entry.Module, provider));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
