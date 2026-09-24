using System.Text.Json;

namespace Elsa.Cli;

/// <summary>One module's entry in a committed <c>migration-plan.json</c>, as far as <c>script-check</c> reads it.</summary>
public sealed record PlanModule(string Module, string File, IReadOnlyList<string> Ids);

/// <summary>Only the selectors needed to regenerate a version-2 plan; the full evidence is validated on read.</summary>
public sealed record PlanConfigurationContext(string Source, string? Resource);

/// <summary>
/// The committed <c>migration-plan.json</c>, read as <c>script-check</c>'s own source of truth for what
/// should be regenerated (FR-044): the provider, the schema, and the modules the artifact claims to
/// describe — never a selection typed on the command line, which would let a check pass by checking
/// something else.
/// </summary>
public sealed record MigrationPlan(
    string Provider,
    string? Schema,
    string HostName,
    string? HostShell,
    string HostEnvironment,
    IReadOnlyList<PlanModule> Modules,
    int SchemaVersion = 1,
    PlanConfigurationContext? ConfigurationContext = null)
{
    public const string FileName = "migration-plan.json";

    /// <summary>The legacy manifest schema version, retained for exact artifact compatibility.</summary>
    public const int SupportedSchemaVersion = 1;
    public const int ContextSchemaVersion = 2;

    public static MigrationPlan Read(string directory)
    {
        var path = Path.Join(directory, FileName);
        if (!File.Exists(path))
        {
            throw CliRefusal.Resolution(
                "plan-missing",
                $"'{directory}' has no {FileName}, so there is nothing stating what it should contain. Run `dotnet elsa persistence script` to produce one.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var plan = document.RootElement;
            var version = plan.GetProperty("schemaVersion").GetInt32();
            if (version is not (SupportedSchemaVersion or ContextSchemaVersion))
            {
                throw CliRefusal.Resolution(
                    "plan-unsupported",
                    $"The committed migration plan declares schemaVersion {version}; this build reads {SupportedSchemaVersion} or {ContextSchemaVersion}.");
            }

            var context = version == ContextSchemaVersion ? ReadContextPlan(plan) : null;

            var host = plan.GetProperty("host");
            return new(
                plan.GetProperty("provider").GetString()!,
                plan.GetProperty("schema").GetString(),
                host.GetProperty("name").GetString() ?? "",
                host.GetProperty("shell").GetString(),
                host.GetProperty("environment").GetString() ?? "",
                [
                    .. plan.GetProperty("modules").EnumerateArray().Select(module => new PlanModule(
                        module.GetProperty("module").GetString()!,
                        module.GetProperty("file").GetString()!,
                        [.. module.GetProperty("migrations").GetProperty("ids").EnumerateArray().Select(id => id.GetString()!)]))
                ], version, context);
        }
        catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException or
                                         FormatException or ArgumentException or IOException)
        {
            throw CliRefusal.Resolution("plan-unreadable", "The committed migration plan is malformed or unreadable.");
        }
    }

    private static PlanConfigurationContext ReadContextPlan(JsonElement plan)
    {
        Fields(plan, "schemaVersion", "provider", "engine", "efCoreVersion", "schema", "idempotent",
            "ordering", "host", "configurationContext", "modules");
        if (Required(plan, "provider") is not ("Sqlite" or "SqlServer" or "PostgreSql" or "MySql") ||
            string.IsNullOrWhiteSpace(Required(plan, "efCoreVersion")) ||
            !NullableString(plan, "schema") ||
            plan.GetProperty("idempotent").ValueKind != JsonValueKind.True ||
            Required(plan, "ordering") != "dependsOn-then-name")
            throw Invalid();

        var engine = plan.GetProperty("engine");
        Fields(engine, "package", "version", "source");
        if (string.IsNullOrWhiteSpace(Required(engine, "package")) ||
            string.IsNullOrWhiteSpace(Required(engine, "version")) ||
            Required(engine, "source") is not ("host-deps-file" or "resolved-nupkg"))
            throw Invalid();

        var host = plan.GetProperty("host");
        Fields(host, "name", "providerAgreement", "shell", "environment");
        if (string.IsNullOrWhiteSpace(Required(host, "name")) ||
            Required(host, "providerAgreement") != "checked" ||
            string.IsNullOrWhiteSpace(Required(host, "shell")) ||
            string.IsNullOrWhiteSpace(Required(host, "environment")))
            throw Invalid();

        var context = plan.GetProperty("configurationContext");
        Fields(context, "source", "environment", "shell", "resource", "resolution",
            "targetVerification", "runtimeParity", "participants", "unresolved");
        var source = Required(context, "source");
        var resource = context.GetProperty("resource");
        var resolution = Required(context, "resolution");
        if (source is not ("workbench-json-v1" or "workbench-json-environment-v1") ||
            Required(context, "environment") != Required(host, "environment") ||
            Required(context, "shell") != Required(host, "shell") ||
            !NullableString(context, "resource") ||
            resolution is not ("resource" or "legacy") ||
            (resolution == "resource" ? string.IsNullOrWhiteSpace(resource.GetString()) : resource.ValueKind != JsonValueKind.Null) ||
            Required(context, "targetVerification") != "not-performed" ||
            Required(context, "runtimeParity") != "unobserved")
            throw Invalid();

        foreach (var participant in Array(context, "participants"))
        {
            Fields(participant, "feature", "module", "resource", "provider", "connectionReference", "selection");
            if (string.IsNullOrWhiteSpace(Required(participant, "feature")) ||
                string.IsNullOrWhiteSpace(Required(participant, "module")) ||
                !NullableString(participant, "resource") || !NullableString(participant, "provider") ||
                !NullableString(participant, "connectionReference") ||
                Required(participant, "selection") is not ("Legacy" or "ShellBinding" or "ShellDefault" or "RootDefault"))
                throw Invalid();
        }
        if (Array(context, "unresolved").Any(code => code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString())))
            throw Invalid();

        var modules = Array(plan, "modules").ToArray();
        if (modules.Length == 0)
            throw Invalid();
        for (var index = 0; index < modules.Length; index++)
            ValidateModule(modules[index], index + 1);
        if (modules.Select(module => Required(module, "module")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != modules.Length)
            throw Invalid();
        return new(source, resource.ValueKind == JsonValueKind.Null ? null : resource.GetString());
    }

    private static void ValidateModule(JsonElement module, int order)
    {
        Fields(module, "order", "module", "file", "sha256", "package", "assembly", "context",
            "historyTable", "migrations", "dependsOn", "postMigration");
        var file = Required(module, "file");
        var name = Required(module, "module");
        if (module.GetProperty("order").GetInt32() != order ||
            string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(file) || file.Any(char.IsControl) ||
            file != Path.GetFileName(file) || !file.EndsWith(".sql", StringComparison.Ordinal) ||
            !Sha256(Required(module, "sha256")) ||
            string.IsNullOrWhiteSpace(Required(module, "assembly")) ||
            string.IsNullOrWhiteSpace(Required(module, "context")) ||
            string.IsNullOrWhiteSpace(Required(module, "historyTable")))
            throw Invalid();
        var package = module.GetProperty("package");
        Fields(package, "id", "version", "source");
        if (string.IsNullOrWhiteSpace(Required(package, "id")) ||
            string.IsNullOrWhiteSpace(Required(package, "version")) ||
            Required(package, "source") is not ("host-deps-file" or "resolved-nupkg"))
            throw Invalid();
        var migrations = module.GetProperty("migrations");
        Fields(migrations, "from", "to", "count", "ids");
        var ids = Array(migrations, "ids").ToArray();
        if (Required(migrations, "from") != "0" || !NullableString(migrations, "to") ||
            migrations.GetProperty("count").GetInt32() != ids.Length ||
            ids.Any(id => id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) ||
            Array(module, "dependsOn").Any(id => id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())))
            throw Invalid();
        foreach (var action in Array(module, "postMigration"))
        {
            Fields(action, "id", "kind", "requiredWhen", "audit", "run");
            if (new[] { "id", "kind", "requiredWhen", "audit", "run" }
                .Any(name => string.IsNullOrWhiteSpace(Required(action, name))))
                throw Invalid();
        }
    }

    private static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal)) is false)
            throw Invalid();
    }

    private static string Required(JsonElement value, string name)
    {
        var field = value.GetProperty(name);
        return field.ValueKind == JsonValueKind.String ? field.GetString()! : throw Invalid();
    }

    private static bool NullableString(JsonElement value, string name) =>
        value.GetProperty(name).ValueKind is JsonValueKind.String or JsonValueKind.Null;

    private static IEnumerable<JsonElement> Array(JsonElement value, string name)
    {
        var field = value.GetProperty(name);
        return field.ValueKind == JsonValueKind.Array ? field.EnumerateArray() : throw Invalid();
    }

    private static bool Sha256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static JsonException Invalid() => new("The version-2 migration plan has an invalid closed shape.");
}
