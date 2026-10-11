using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// The versioned JSON <see cref="EfToolingHost.RunAsync(Stream,Stream)"/> exchanges with the CLI worker
/// (spec 171 FR-003). Frozen: the worker runs out of process, so anything ambiguous here becomes a bug
/// there. The schema is closed in both directions — an unknown command, an unknown request property, or an
/// unknown enumerated value is a usage error, never a silent no-op.
/// </summary>
/// <remarks>
/// Candidate inspection is independently versioned and is not an extension of these persistence-command DTOs.
/// <para>
/// Frozen means closed, not unchanging: an additive optional field does not move <see cref="Version"/>. A request field is
/// sent only to a host build that declares it, as the worker does for <see cref="EfToolingRequest.CapabilitySelection"/>,
/// <see cref="EfToolingRequest.SkewAllowance"/> and <see cref="EfToolingRequest.SqliteMigrationLockStaleAfter"/>. The rule, and why it does not contradict "do not add optional fields to the
/// closed version-1 DTOs", is the 2026-09-29 note in <c>specs/173-shared-persistence/contracts/tooling.md</c>. The 2026-09-29
/// additions, all optional and all on <c>status</c>: the request's <c>skewAllowance</c>, the response's
/// <c>finalization.cluster</c> (with its <c>availability</c> marker) and each pending version's <c>waitsFor</c>.
/// </para>
/// </remarks>
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

    /// <summary>Places an operator's hold on a schema family's finalization (spec 181, FR-019 and FR-020).</summary>
    public const string Hold = "hold";

    /// <summary>Releases an operator's hold (spec 181, FR-019 and FR-020).</summary>
    public const string Release = "release";

    /// <summary>Reports each schema family's finalization status (spec 181, FR-020 and FR-022).</summary>
    public const string Status = "status";

    public static readonly string[] All = [List, Plan, Script, Apply, Validate, PostMigrate, Hold, Release, Status];

    /// <summary>The commands that read or write a finalization record rather than migrations. None finalizes, forces finalization or lowers a finalized version.</summary>
    public static bool IsFinalization(string command) => command is Hold or Release or Status;
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
    /// What <c>hold</c>, <c>release</c> and <c>status</c> act on (spec 181, FR-020). Required by <c>hold</c> and
    /// <c>release</c>, optional for <c>status</c>, and refused by every other command.
    /// </summary>
    public EfToolingFinalizationRequest? Finalization { get; init; }

    /// <summary>
    /// The skew allowance <c>status</c> judges the cluster members' liveness with, as a <c>TimeSpan</c> in its invariant
    /// <c>c</c> format (<c>00:00:02</c>). Optional and accepted by <c>status</c> only: absent means the membership provider's
    /// own default. A host that runs its members with another allowance names it here, or the tool can list a member as
    /// counted that the hosts already count expired, or the reverse.
    /// </summary>
    public string? SkewAllowance { get; init; }

    /// <summary>
    /// How long <c>apply</c> waits for a SQLite database's EF migration lock before it reports the lock as stale, as a
    /// <c>TimeSpan</c> in its invariant <c>c</c> format (<c>00:10:00</c>): the host's own
    /// <see cref="EfMigrateOptions.SqliteMigrationLockStaleAfter"/>, as the CLI read it from the host's appsettings (#2196).
    /// Optional and accepted by <c>apply</c> only: absent means the key's default. The version-1 request carries no configuration
    /// of its own, so this is how a host's bound reaches it.
    /// </summary>
    public string? SqliteMigrationLockStaleAfter { get; init; }

    /// <summary>
    /// The connection string <c>apply</c>, <c>validate</c> and <c>post-migrate</c> run against. Required for
    /// those three commands, refused otherwise — <c>list</c>, <c>plan</c> and <c>script</c> never open a
    /// database (D7). This is the one field this build never echoes back: not in a response, a refusal
    /// detail, or an exception message.
    /// </summary>
    public string? Connection { get; init; }
}

/// <summary>The schema family, version, reason and operator a finalization command names (spec 181, FR-019).</summary>
public sealed class EfToolingFinalizationRequest
{
    /// <summary>The schema family. Required by <c>hold</c> and <c>release</c>; narrows <c>status</c> to one family.</summary>
    public string? Family { get; init; }

    /// <summary>The one version a hold is limited to, or null for a hold on the whole family.</summary>
    public string? Version { get; init; }

    /// <summary>Why the hold is placed. Required by <c>hold</c>, refused otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>The operator identity the history records. Required by <c>hold</c> and <c>release</c>, refused by <c>status</c>.</summary>
    public string? Operator { get; init; }
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

    /// <summary>What <c>hold</c>, <c>release</c> or <c>status</c> found or changed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EfToolingFinalizationPayload? Finalization { get; init; }

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

/// <summary>
/// Each selected module's schema families as <c>hold</c>, <c>release</c> or <c>status</c> leaves them (spec 181,
/// FR-022). <c>hold</c> and <c>release</c> report the one family they changed.
/// </summary>
public sealed class EfToolingFinalizationPayload
{
    public string Provider { get; init; } = "";
    public string? Schema { get; init; }
    public IReadOnlyList<EfToolingFinalizationFamily> Families { get; init; } = [];

    /// <summary>
    /// The cluster's members as the membership table in this database holds them, present on <c>status</c> only. Always
    /// present on a build that reports it, with <see cref="EfToolingCluster.Availability"/> saying whether it was read; a
    /// payload with no <c>cluster</c> at all comes from a build that predates the property, and says nothing about the cluster.
    /// </summary>
    public EfToolingCluster? Cluster { get; init; }
}

public sealed class EfToolingFinalizationFamily
{
    public string Module { get; init; } = "";
    public string Family { get; init; } = "";

    /// <summary>The database's opaque identity, or null when no record has been written yet.</summary>
    public string? DatabaseIdentity { get; init; }

    /// <summary>The finalized version, or null when no record has been written yet.</summary>
    public string? FinalizedVersion { get; init; }

    /// <summary>The versions this host's build reads, oldest first.</summary>
    public IReadOnlyList<string> ReadableVersions { get; init; } = [];

    public EfToolingFinalizationIntent? Intent { get; init; }
    public IReadOnlyList<EfToolingFinalizationHold> Holds { get; init; } = [];

    /// <summary>Each version this host reads above the finalized one, and why it is not finalized.</summary>
    public IReadOnlyList<EfToolingPendingVersion> Pending { get; init; } = [];

    /// <summary>The finish record's completion version (spec 186), or null when none stands.</summary>
    public string? CompletionVersion { get; init; }

    /// <summary>
    /// The backfill run a worker has claimed in the finish record (spec 186, FR-008 and FR-021), on the completion that
    /// stands or, while none stands, on its withdrawal; or null when none is.
    /// </summary>
    public EfToolingBackfillRun? BackfillRun { get; init; }

    /// <summary>The withdrawal of the family's completion while none has been recorded since (spec 186, FR-018), or null.</summary>
    public EfToolingCompletionWithdrawal? CompletionWithdrawn { get; init; }
}

/// <summary>A claimed backfill run: the version it upgrades to, the member that claimed it, and until when the claim holds.</summary>
public sealed class EfToolingBackfillRun
{
    public string TargetVersion { get; init; } = "";
    public string Member { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>A withdrawn completion: the version, who withdrew it and when, and why, naming the tables and counts.</summary>
public sealed class EfToolingCompletionWithdrawal
{
    public string Version { get; init; } = "";
    public string WithdrawnBy { get; init; } = "";
    public DateTimeOffset At { get; init; }
    public string? Reason { get; init; }
}

public sealed class EfToolingFinalizationIntent
{
    public string Version { get; init; } = "";
    public string Member { get; init; } = "";
    public DateTimeOffset At { get; init; }
}

public sealed class EfToolingFinalizationHold
{
    /// <summary>The version the hold is limited to, or null for the whole family.</summary>
    public string? Version { get; init; }
    public string Reason { get; init; } = "";
    public string PlacedBy { get; init; } = "";
    public DateTimeOffset PlacedAt { get; init; }
}

public sealed class EfToolingPendingVersion
{
    public string Version { get; init; } = "";

    /// <summary><c>pending</c> or <c>readable-everywhere</c>.</summary>
    public string State { get; init; } = "";

    /// <summary>The reasons of the holds that keep it.</summary>
    public IReadOnlyList<string> HeldBy { get; init; } = [];

    /// <summary>
    /// The counted members that cannot read it yet, each with the versions it reads (spec 181, FR-022): empty when none
    /// blocks it, and null when the fleet was not read (a <c>status</c> run whose closure carries no membership provider,
    /// a membership table not created in this database, and every <c>hold</c> and <c>release</c>).
    /// </summary>
    public IReadOnlyList<EfToolingWaitingOn>? WaitsFor { get; init; }
}

/// <summary>A counted member that cannot read a pending version, and the versions of the family it does read.</summary>
public sealed class EfToolingWaitingOn
{
    public string HostId { get; init; } = "";
    public string Incarnation { get; init; } = "";

    /// <summary>False when the member's report is in a form this build cannot interpret, which counts as reading nothing.</summary>
    public bool ReportReadable { get; init; } = true;

    public IReadOnlyList<string> Reads { get; init; } = [];
}

/// <summary>How <c>status</c> came by the cluster: <see cref="EfToolingCluster.Availability"/>.</summary>
public static class EfToolingClusterAvailability
{
    /// <summary>The membership table was read, and <see cref="EfToolingCluster.Members"/> lists what it holds.</summary>
    public const string Read = "read";

    /// <summary>The host's closure carries no cluster membership provider, so no cluster is known beyond this host.</summary>
    public const string NoMembershipProvider = "no-membership-provider";

    /// <summary>The closure carries a provider, but its module has no context for the requested provider engine, so its table could not be read.</summary>
    public const string NoProviderContext = "no-provider-context";

    /// <summary>The module has migrations not applied in this database, so its table does not exist here.</summary>
    public const string NotMigrated = "not-migrated";

    /// <summary>The table could not be read; <see cref="EfToolingCluster.Note"/> says why.</summary>
    public const string Unreadable = "unreadable";
}

/// <summary>
/// The membership table as one read of it shows it. <see cref="Availability"/> says whether it could be read, and
/// <see cref="Note"/> why not: an empty <see cref="Members"/> is a cluster with nobody in it only when it was read.
/// </summary>
public sealed class EfToolingCluster
{
    /// <summary>One of <see cref="EfToolingClusterAvailability"/>.</summary>
    public string Availability { get; init; } = EfToolingClusterAvailability.Read;

    /// <summary>The module that holds the members; empty when the closure carries no provider for one.</summary>
    public string Module { get; init; } = "";

    /// <summary>The instant, on this tool's clock, that liveness was judged at; null when the table was not read.</summary>
    public DateTimeOffset? JudgedAt { get; init; }

    /// <summary>The skew allowance liveness was judged with: the request's, or the membership default when it named none; null when the table was not read.</summary>
    public string? SkewAllowance { get; init; }

    /// <summary>Why the table could not be read, or null when it was or when there is no provider to read it with.</summary>
    public string? Note { get; init; }

    public IReadOnlyList<EfToolingClusterMember> Members { get; init; } = [];
}

public sealed class EfToolingClusterMember
{
    /// <summary>The status of a member whose entry this build cannot interpret: the entry's own word is not one it reads.</summary>
    public const string UnknownStatus = "unknown";

    public string HostId { get; init; } = "";
    public string Incarnation { get; init; } = "";

    /// <summary>The member's status by name (joining, active, draining or left), or <see cref="UnknownStatus"/> when its entry cannot be interpreted.</summary>
    public string Status { get; init; } = "";

    /// <summary>
    /// Whether the reader judges the member live: it has not left, and its heartbeat plus expiry and skew has not passed. A
    /// live member is counted for each family it reports on, and for every family when its entry cannot be interpreted.
    /// </summary>
    public bool Live { get; init; }

    /// <summary>Whether a later incarnation of the host id has joined.</summary>
    public bool Displaced { get; init; }

    public DateTimeOffset LastHeartbeatAt { get; init; }

    /// <summary>False when the member's entry is in a form this build cannot interpret, which counts as reading nothing.</summary>
    public bool ReportReadable { get; init; } = true;

    /// <summary>
    /// What the member reports it reads of each family <c>status</c> lists, for that family's database: one entry for each
    /// family it reports on, and none for a family it reports nothing about.
    /// </summary>
    public IReadOnlyList<EfToolingClusterReads> Reads { get; init; } = [];
}

/// <summary>What one member reports it reads of one schema family, in the database that family is listed for.</summary>
public sealed class EfToolingClusterReads
{
    public string Family { get; init; } = "";

    public IReadOnlyList<string> Versions { get; init; } = [];
}

public sealed class EfToolingErrorPayload
{
    /// <summary>A stable machine-readable code; the message is for an operator, this is for a caller.</summary>
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>One line per offender, for the refusals that must name every one rather than the first.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];
}
