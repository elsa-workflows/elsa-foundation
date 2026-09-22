using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The versioned JSON <see cref="EfToolingHost.RunAsync(Stream,Stream)"/> exchanges with the CLI worker
/// (spec 171 FR-003). Frozen: the worker runs out of process, so anything ambiguous here becomes a bug
/// there. The schema is closed in both directions — an unknown command, an unknown request property, or an
/// unknown enumerated value is a usage error, never a silent no-op.
/// </summary>
public static class EfToolingContract
{
    /// <summary>The only request/response envelope version this build speaks.</summary>
    public const int Version = 1;

    /// <summary>
    /// Case-sensitive camelCase, and an unmapped property is an error: a worker that sends a field this
    /// build does not know — a future slice's, say — must be told, not quietly given an answer to a
    /// different question.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

/// <summary>The commands <see cref="EfToolingHost"/> backs.</summary>
public static class EfToolingCommands
{
    public const string List = "list";
    public const string Plan = "plan";
    public const string Script = "script";
    public const string Apply = "apply";
    public const string Validate = "validate";

    /// <summary>The one command that ever calls <see cref="IEfPostMigrationAction.RunAsync"/> (ADR 0076 D8).</summary>
    public const string PostMigrate = "post-migrate";

    public static readonly string[] All = [List, Plan, Script, Apply, Validate, PostMigrate];
}

/// <summary>
/// Where a package id and version were determined from (FR-049), so a reader of
/// <c>migration-plan.json</c> can tell a package-derived fact from an assembly-metadata-derived one.
/// <see cref="EfToolingHost"/> can observe neither, so the worker states it and this build only checks it.
/// </summary>
public static class EfToolingPackageSource
{
    /// <summary>Read from the host's own <c>.deps.json</c>.</summary>
    public const string HostDepsFile = "host-deps-file";

    /// <summary>Read from the <c>.nupkg</c> the package set resolved to.</summary>
    public const string ResolvedNupkg = "resolved-nupkg";

    public static readonly string[] All = [HostDepsFile, ResolvedNupkg];
}

/// <summary>What the per-feature provider-agreement check (FR-039) reports about a run.</summary>
public static class EfToolingProviderAgreement
{
    public const string Checked = "checked";
    public const string NotChecked = "not-checked";

    public static readonly string[] All = [Checked, NotChecked];

    /// <summary>
    /// What <c>providerAgreement</c> does <i>not</i> cover, stated in every manifest because FR-039 requires
    /// the artifact itself to say it: the check reads files, and a running host also reads its process
    /// environment, so a live host's effective provider can differ from the one verified here.
    /// </summary>
    public const string Note =
        "providerAgreement reflects shells.json plus its shells.<environment>.json overlay only. A running " +
        "host's own environment-variable configuration overrides are invisible to this check, so its " +
        "effective provider can differ from the one verified here.";
}

/// <summary>One request. Every property is nullable so a missing one is refused with a message rather than bound to a default.</summary>
public sealed class EfToolingRequest
{
    /// <summary>Must equal <see cref="EfToolingContract.Version"/>.</summary>
    public int? Version { get; init; }

    /// <summary>One of <see cref="EfToolingCommands.All"/>.</summary>
    public string? Command { get; init; }

    /// <summary>Required for <c>plan</c> and <c>script</c>; optional for <c>list</c>, which defaults to every discovered module.</summary>
    public EfToolingSelection? Selection { get; init; }

    /// <summary>Required for <c>plan</c> and <c>script</c>, authoritative (D4), and refused on <c>list</c>, which needs no engine.</summary>
    public string? Provider { get; init; }

    /// <summary>The schema the artifact targets, resolved through <see cref="EfSchema.Normalize"/>. Refused on <c>list</c>.</summary>
    public string? Schema { get; init; }

    /// <summary>The directory <c>script</c> writes its artifact to. Refused on <c>list</c> and <c>plan</c>, which write nothing.</summary>
    public string? Output { get; init; }

    /// <summary>The host facts the manifest records. Required for <c>script</c>, refused otherwise.</summary>
    public EfToolingHostFacts? Host { get; init; }

    /// <summary>The provider engine's package facts. Required for <c>script</c>, refused otherwise.</summary>
    public EfToolingEngineFacts? Engine { get; init; }

    /// <summary>One entry per module assembly in the selection. Required for <c>script</c>, refused otherwise.</summary>
    public IReadOnlyList<EfToolingPackageFacts>? Packages { get; init; }

    /// <summary>
    /// The host's enabled shell features, as the CLI read them from <c>shells.json</c> plus its
    /// <c>--environment</c> overlay (FR-035). Present — even empty — exactly when that configuration was
    /// found, which is what <c>providerAgreement: checked</c> means; absent when none was found at all.
    /// Required by selection kind <c>from-host</c>, which has nothing to select from without it. Carries
    /// each feature's <c>Provider</c> setting and nothing else: no other shell setting travels here, so no
    /// connection string can (D7).
    /// </summary>
    public IReadOnlyList<EfToolingShellFeature>? Shells { get; init; }

    /// <summary>
    /// The engine option(s) the host's own <c>appsettings.json</c> selects under
    /// <see cref="EfProviderAgreement.CapabilityKey"/>, as the CLI worker read them (spec 172 FR-004, ADR
    /// 0076 D4). Present only when that key is set, and refused on <c>list</c>, which asks for no provider
    /// and therefore has nothing to compare. Absent means the host selects no engine that way, not that the
    /// selection agrees.
    /// </summary>
    /// <remarks>
    /// The option names travel, not the key's other children: <c>Version</c> and <c>Feed</c> say which
    /// package to acquire, which is Nuplane's business and never this check's.
    /// </remarks>
    public IReadOnlyList<string>? CapabilitySelection { get; init; }

    /// <summary>
    /// The connection string <c>apply</c>, <c>validate</c> and <c>post-migrate</c> run against. Required for
    /// those three commands, refused otherwise — <c>list</c>, <c>plan</c> and <c>script</c> never open a
    /// database (D7). This is the one field this build never echoes back: not in a response, a refusal
    /// detail, or an exception message.
    /// </summary>
    public string? Connection { get; init; }
}

/// <summary>Which modules a command runs against. Discriminated rather than inferred from a null list.</summary>
public sealed class EfToolingSelection
{
    /// <summary><c>all</c>, <c>modules</c> or <c>from-host</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The canonical <c>--modules</c> names, matched case-insensitively. Required for <c>modules</c>, refused for the other two kinds.</summary>
    public IReadOnlyList<string>? Modules { get; init; }

    public const string AllKind = "all";
    public const string ModulesKind = "modules";

    /// <summary>Every module the host's enabled shell features map to through <c>[UsesEfModule]</c> (FR-028).</summary>
    public const string FromHostKind = "from-host";
}

/// <summary>
/// One feature a shell enables, reduced to what the provider-agreement check needs: which shell enabled it,
/// the CShells feature name it was enabled under, and its configured <c>Provider</c> setting.
/// </summary>
/// <remarks>
/// A feature the shell disables never reaches this list — an unenabled feature is ignored (FR-036) — and no
/// setting other than <c>Provider</c> is carried, deliberately: shell configuration holds connection
/// strings, and the narrowest possible projection of it is the one that cannot leak one into a request, a
/// response, or a refusal.
/// </remarks>
public sealed class EfToolingShellFeature
{
    /// <summary>The shell that enables this feature.</summary>
    public string? Shell { get; init; }

    /// <summary>The CShells feature name, matched case-insensitively against <c>[ShellFeature]</c>.</summary>
    public string? Feature { get; init; }

    /// <summary>The configured <c>Provider</c> value, or <c>null</c> when the shell sets none (FR-037).</summary>
    public string? Provider { get; init; }
}

/// <summary>The <c>host</c> block of <c>migration-plan.json</c> (FR-047).</summary>
public sealed class EfToolingHostFacts
{
    public string? Name { get; init; }

    /// <summary>One of <see cref="EfToolingProviderAgreement.All"/>. <c>checked</c> exactly when the host's shells configuration was found and compared; <c>not-checked</c> only when none was found at all.</summary>
    public string? ProviderAgreement { get; init; }

    public string? Shell { get; init; }

    /// <summary>The <c>--environment</c> value the provider-agreement check used. The CLI owns its <c>Production</c> default (FR-038), so it is stated here rather than defaulted twice.</summary>
    public string? Environment { get; init; }
}

/// <summary>The provider engine's package facts, as the manifest's <c>engine</c> block records them.</summary>
public sealed class EfToolingEngineFacts
{
    /// <summary>Must be the package <see cref="EfRelationalProviderBinding.ProviderPackageId"/> names for the requested provider.</summary>
    public string? Package { get; init; }

    public string? Version { get; init; }

    /// <summary>One of <see cref="EfToolingPackageSource.All"/>.</summary>
    public string? Source { get; init; }
}

/// <summary>One module assembly's package facts, as the manifest's per-module <c>package</c> block records them.</summary>
public sealed class EfToolingPackageFacts
{
    /// <summary>The simple name of the assembly this entry describes.</summary>
    public string? Assembly { get; init; }

    public string? Id { get; init; }

    public string? Version { get; init; }

    /// <summary>One of <see cref="EfToolingPackageSource.All"/>.</summary>
    public string? Source { get; init; }
}

/// <summary>One response. Exactly one payload is present, and it is the one <see cref="Command"/> names.</summary>
public sealed class EfToolingResponse
{
    public int Version { get; init; } = EfToolingContract.Version;

    /// <summary><c>ok</c> or <c>error</c>.</summary>
    public string Status { get; init; } = "ok";

    /// <summary>The same code <see cref="EfToolingHost.RunAsync(Stream,Stream)"/> returns.</summary>
    public int ExitCode { get; init; }

    /// <summary>The command that ran, or null when the request could not be parsed far enough to name one.</summary>
    public string? Command { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingListPayload? List { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingPlanPayload? Plan { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingScriptPayload? Script { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingApplyPayload? Apply { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingValidatePayload? Validate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingPostMigratePayload? PostMigrate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingErrorPayload? Error { get; init; }
}

public sealed class EfToolingListPayload
{
    public IReadOnlyList<EfToolingModuleListing> Modules { get; init; } = [];
}

public sealed class EfToolingModuleListing
{
    public string Module { get; init; } = "";
    public string Assembly { get; init; } = "";
    public string Context { get; init; } = "";
    public string HistoryTable { get; init; } = "";
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>The providers this module declares a derived context for, ordinal-ordered.</summary>
    public IReadOnlyList<string> Providers { get; init; } = [];
}

public sealed class EfToolingPlanPayload
{
    public string Provider { get; init; } = "";
    public string? Schema { get; init; }
    public IReadOnlyList<EfToolingPlanEntry> Modules { get; init; } = [];
}

public sealed class EfToolingPlanEntry
{
    public int Order { get; init; }
    public string Module { get; init; } = "";
    public string Assembly { get; init; } = "";
    public string Context { get; init; } = "";
    public string HistoryTable { get; init; } = "";

    /// <summary>Offline, so always <c>0</c> to head (there is no database to read a real <c>from</c> off).</summary>
    public string From { get; init; } = "0";

    public string? To { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<string> Ids { get; init; } = [];
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}

public sealed class EfToolingScriptPayload
{
    /// <summary>The manifest's file name, always <c>migration-plan.json</c>.</summary>
    public string Manifest { get; init; } = "";

    public string ManifestSha256 { get; init; } = "";

    public IReadOnlyList<EfToolingScriptFile> Files { get; init; } = [];
}

public sealed class EfToolingScriptFile
{
    public int Order { get; init; }
    public string Module { get; init; } = "";
    public string File { get; init; } = "";
    public string Sha256 { get; init; } = "";
}

/// <summary>What <c>apply</c> applied against the selected database, per module (FR-053).</summary>
public sealed class EfToolingApplyPayload
{
    public string Provider { get; init; } = "";
    public string? Schema { get; init; }
    public IReadOnlyList<EfToolingApplyEntry> Modules { get; init; } = [];
}

public sealed class EfToolingApplyEntry
{
    public int Order { get; init; }
    public string Module { get; init; } = "";
    public string Context { get; init; } = "";
    public string HistoryTable { get; init; } = "";

    /// <summary>The migrations that were pending before this run and are now applied. Empty means already up to date.</summary>
    public IReadOnlyList<string> Applied { get; init; } = [];
}

/// <summary>What <c>validate</c> found against the selected database, per module (FR-052). Present only on success — a pending migration is refused instead.</summary>
public sealed class EfToolingValidatePayload
{
    public string Provider { get; init; } = "";
    public string? Schema { get; init; }
    public IReadOnlyList<EfToolingValidateEntry> Modules { get; init; } = [];
}

public sealed class EfToolingValidateEntry
{
    public int Order { get; init; }
    public string Module { get; init; } = "";
    public string Context { get; init; } = "";
    public string HistoryTable { get; init; } = "";
}

/// <summary>What <c>post-migrate</c> did, per module (ADR 0076 D8). The one command that runs an action at all.</summary>
public sealed class EfToolingPostMigratePayload
{
    public string Provider { get; init; } = "";
    public string? Schema { get; init; }
    public IReadOnlyList<EfToolingPostMigrateEntry> Modules { get; init; } = [];
}

public sealed class EfToolingPostMigrateEntry
{
    public int Order { get; init; }
    public string Module { get; init; } = "";
    public string Context { get; init; } = "";

    /// <summary>Every action this module declares, whether or not it had work to do.</summary>
    public IReadOnlyList<string> Declared { get; init; } = [];

    /// <summary>The actions that audited as required and were therefore run. Empty means there was nothing to do.</summary>
    public IReadOnlyList<string> Ran { get; init; } = [];
}

public sealed class EfToolingErrorPayload
{
    /// <summary>A stable machine-readable code; the message is for an operator, this is for a caller.</summary>
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>One line per offender, for the refusals that must name every one rather than the first.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}
