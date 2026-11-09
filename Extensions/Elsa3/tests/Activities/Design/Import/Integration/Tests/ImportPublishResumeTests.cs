using System.Net;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Activities.Design.Import.Models.ReusableActivityPublicationStatus;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// One host that applied <c>reusable-resume</c> (<c>independent</c>; <c>dependant</c>, whose body calls
/// <c>dependency</c>) while its publish seam refused <c>dependency</c>.
/// </summary>
public sealed class PartlyPublishedImportFixture : IAsyncLifetime
{
    public const string IdempotencyKey = "import-resume";

    public Elsa3ImportHost Host { get; private set; } = null!;
    public ImportAttempt Import { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Host = await Elsa3ImportHost.StartAsync(withPublishSeam: true);
        Import = await ImportRefusingDependencyAsync(Host, IdempotencyKey);
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();

    /// <summary>Applies <c>reusable-resume</c> under <paramref name="idempotencyKey"/> with the seam refusing <c>dependency-v1</c>.</summary>
    public static Task<ImportAttempt> ImportRefusingDependencyAsync(Elsa3ImportHost host, string idempotencyKey) =>
        host.ImportSelectionAsync(idempotencyKey, ["reusable-resume"], beforeApply: items => host.Refusals.Refuse(
            Elsa3ImportHost.Required(items.Single(item => Elsa3ImportHost.Required(item, "sourceVersionId") == "dependency-v1"), "activityDefinitionVersionId")));
}

/// <summary>
/// Spec 246 User Story 3: every selected version reports its publication state, a refusal stops only the versions that
/// use the refused one, a status read and a replay report the state live, and a new apply of the same export publishes
/// only what is missing.
/// </summary>
public sealed class ImportPublishResumeTests(PartlyPublishedImportFixture fixture) : IClassFixture<PartlyPublishedImportFixture>
{
    [Fact]
    public void Refused_version_reports_publisher_code_and_diagnostics_and_independent_versions_still_publish()
    {
        Assert.Equal(HttpStatusCode.Created, fixture.Import.Apply.StatusCode);
        var states = PublicationEntry.Of(fixture.Import.Apply.Body).ToDictionary(state => state.SourceVersionId);

        var refused = states["dependency-v1"];
        Assert.Equal((Refused, PublishRefusals.Code), (refused.Status, refused.ErrorCode));
        Assert.Contains(refused.Json["diagnostics"]!.AsArray(), diagnostic => (string?)diagnostic!["code"] == PublishRefusals.Code);
        Assert.Equal(Published, states["independent-v1"].Status);
    }

    [Fact]
    public void Dependant_of_a_refused_version_reports_NotPublishedDependency()
    {
        var dependant = PublicationEntry.Of(fixture.Import.Apply.Body).Single(state => state.SourceVersionId == "dependant-v1");

        Assert.Equal(NotPublishedDependency, dependant.Status);
        Assert.Equal(fixture.Import.VersionId("dependency-v1"), dependant.BlockedBy);
    }

    [Fact]
    public async Task Status_read_reports_published_state_live()
    {
        var (status, receipt) = await fixture.Host.GetImportStatusAsync(PartlyPublishedImportFixture.IdempotencyKey);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Expected(AlreadyPublished, NotPublished, NotPublished), PublicationEntry.StatusBySource(receipt));
    }

    [Fact]
    public async Task Same_key_replay_returns_AlreadyImported_with_live_publications()
    {
        var replay = await fixture.Host.ReplayAsync(fixture.Import, PartlyPublishedImportFixture.IdempotencyKey);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((int)ReusableActivityImportReceiptStatus.AlreadyImported, (int?)replay.Body["status"]);
        Assert.Equal(Expected(AlreadyPublished, NotPublished, NotPublished), PublicationEntry.StatusBySource(replay.Body));
    }

    [Fact]
    public async Task Reapply_after_fix_publishes_only_the_missing_versions()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withPublishSeam: true);
        var first = await PartlyPublishedImportFixture.ImportRefusingDependencyAsync(host, "resume-first");
        host.Refusals.Clear();

        // The upload is gone after an apply: resuming re-uploads the same export and applies it under a new key.
        var resumed = await host.ImportSelectionAsync("resume-second", ["reusable-resume"]);

        Assert.Equal(HttpStatusCode.Created, resumed.Apply.StatusCode);
        Assert.Equal(Expected(AlreadyPublished, Published, Published), PublicationEntry.StatusBySource(resumed.Apply.Body));
        foreach (var sourceVersionId in new[] { "independent-v1", "dependency-v1", "dependant-v1" })
        {
            var versionId = resumed.VersionId(sourceVersionId);
            var definitionId = Elsa3ImportHost.Required(resumed.Item(sourceVersionId), "activityDefinitionId");
            var publications = await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().ListByDefinitionAsync(definitionId));
            Assert.Single(publications, publication => publication.DefinitionVersionId == versionId);
        }

        // A replay of a fully published plan publishes nothing and reports every version live.
        var replay = await host.ReplayAsync(first, "resume-first");
        Assert.Equal((int)ReusableActivityImportReceiptStatus.AlreadyImported, (int?)replay.Body["status"]);
        Assert.Equal(Expected(AlreadyPublished, AlreadyPublished, AlreadyPublished), PublicationEntry.StatusBySource(replay.Body));
    }

    private static Dictionary<string, ReusableActivityPublicationStatus> Expected(
        ReusableActivityPublicationStatus independent,
        ReusableActivityPublicationStatus dependency,
        ReusableActivityPublicationStatus dependant) =>
        new(StringComparer.Ordinal)
        {
            ["independent-v1"] = independent,
            ["dependency-v1"] = dependency,
            ["dependant-v1"] = dependant
        };
}
