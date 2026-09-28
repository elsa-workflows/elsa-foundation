using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Versioning.Calculator;

namespace Elsa.Versioning.Publisher;

/// <summary>What one run of the Packages workflow does with the packages it packs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PublishMode>))]
public enum PublishMode
{
    /// <summary>A build from any branch but <c>main</c>: packed for CI artifacts, never pushed (spec 150 FR-008).</summary>
    [JsonStringEnumMemberName("branch")] Branch,

    /// <summary>
    /// A build from <c>main</c> before the record exists: every package packed at its bootstrap version, and nothing
    /// pushed, because the first publish waits for the owner's bootstrap.
    /// </summary>
    [JsonStringEnumMemberName("dry-run")] DryRun,

    /// <summary>A build from <c>main</c> once the record exists: the affected set is pushed and recorded (FR-006).</summary>
    [JsonStringEnumMemberName("publish")] Publish,

    /// <summary>
    /// The one-off, manually dispatched first publish: every package is pushed, and <c>publish-state</c> is created as
    /// an orphan branch recording them (FR-014).
    /// </summary>
    [JsonStringEnumMemberName("bootstrap")] Bootstrap
}

public static class PublishModeNames
{
    /// <summary>The mode's name as the plan and the workflow's <c>mode</c> output spell it, such as <c>dry-run</c>.</summary>
    public static string Name(this PublishMode mode) => JsonSerializer.Serialize(mode).Trim('"');
}

/// <summary>A publish, or the bootstrap, refused to push anything, or stopped part way; the publisher exits with 1.</summary>
public sealed class PublishRefusedException(string message) : Exception(message);

/// <summary>
/// The decision a run's pack job makes before packing, carried to its publish job with the packages: the mode, the
/// commit and ref it was made for, the <c>publish-state</c> revision the versions were computed against, and any
/// force-advance. The publish job recomputes from it and refuses when anything it names has moved.
/// </summary>
public sealed record PublishPlan(PublishMode Mode, string Ref, string Commit, string? StateCommit, ForcedAdvance? Forced)
{
    public const int SchemaVersion = 1;

    /// <summary>The only branch whose builds may push (spec 150 FR-008, FR-021).</summary>
    public const string MainRef = "refs/heads/" + PrereleaseLabel.MainBranch;

    private const string BranchRefPrefix = "refs/heads/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>The branch the ref names, without <c>refs/heads/</c>.</summary>
    public string Branch => Ref[BranchRefPrefix.Length..];

    /// <summary>
    /// The mode a run takes. A branch build only packs. On <c>main</c>, whether <c>publish-state</c> exists decides:
    /// with it, a run publishes the affected set; without it, a run is a dry run unless it is the bootstrap.
    /// </summary>
    /// <param name="reference">The ref being built, as <c>GITHUB_REF</c> gives it.</param>
    /// <param name="bootstrap">True for the manually dispatched bootstrap.</param>
    /// <param name="stateExists">True when <c>publish-state</c> exists on the remote.</param>
    /// <exception cref="ArgumentException">The ref names no branch.</exception>
    /// <exception cref="PublishRefusedException">The bootstrap was asked for off <c>main</c>, or after the record exists.</exception>
    public static PublishMode Decide(string reference, bool bootstrap, bool stateExists)
    {
        if (!reference.StartsWith(BranchRefPrefix, StringComparison.Ordinal) || reference.Length == BranchRefPrefix.Length)
            throw new ArgumentException($"'{reference}' names no branch; packages are packed from a branch ref such as {MainRef}.");

        if (reference != MainRef)
            return bootstrap
                ? throw new PublishRefusedException(
                    $"The bootstrap runs from main only. {reference} is a branch build, which packs for CI artifacts and never pushes (spec 150 FR-008).")
                : PublishMode.Branch;

        if (stateExists)
            return bootstrap
                ? throw new PublishRefusedException(
                    $"{GitPublishState.DefaultBranch} already exists, so the record was bootstrapped already. The bootstrap is one-off (spec 150 FR-014): " +
                    "run the workflow from main without it to publish the affected set.")
                : PublishMode.Publish;

        return bootstrap ? PublishMode.Bootstrap : PublishMode.DryRun;
    }

    /// <summary>The plan's one serialization, like the calculator's: snake-case JSON, LF line endings, a trailing newline.</summary>
    public string Serialize() => new StringBuilder(JsonSerializer.Serialize(
        new PlanDto(
            SchemaVersion,
            Mode,
            Ref,
            Commit,
            StateCommit,
            Forced is null ? null : new ForcedDto(Forced.PackageIds, Forced.Reason)),
        JsonOptions)).Append('\n').ToString();

    public static PublishPlan Load(string path)
    {
        PlanDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<PlanDto>(File.ReadAllBytes(path), JsonOptions) ?? throw new InvalidOperationException($"{path} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{path} is not a readable publish plan: {exception.Message}", exception);
        }

        if (dto.SchemaVersion != SchemaVersion)
            throw new InvalidOperationException($"{path} has schema version {dto.SchemaVersion}; this publisher reads version {SchemaVersion}.");

        return new PublishPlan(
            dto.Mode,
            dto.Ref ?? throw new InvalidOperationException($"{path} names no ref."),
            dto.Commit ?? throw new InvalidOperationException($"{path} names no commit."),
            dto.StateCommit,
            dto.ForceAdvance is null ? null : new ForcedAdvance(dto.ForceAdvance.PackageIds ?? [], dto.ForceAdvance.Reason ?? string.Empty));
    }

    private sealed record PlanDto(int SchemaVersion, PublishMode Mode, string? Ref, string? Commit, string? StateCommit, ForcedDto? ForceAdvance);

    private sealed record ForcedDto(IReadOnlyList<string>? PackageIds, string? Reason);
}
