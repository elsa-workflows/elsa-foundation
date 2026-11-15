using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 246 User Story 4: the conversion warnings of the apply that commits are kept on its receipt, so a replay and a
/// status read return them, and they never reach the published activity.
/// </summary>
public sealed class ImportWarningsTests
{
    private const string IdempotencyKey = "import-warning";

    [Fact]
    public async Task Status_and_replay_return_the_warnings_of_the_committing_apply()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync(IdempotencyKey, ["reusable-with-behaviour-warning"]);
        Assert.Equal(HttpStatusCode.Created, import.Apply.StatusCode);
        var warnings = Warnings(import.Apply.Body);
        Assert.Contains(warnings, warning => (string?)warning!["code"] == Elsa3ActivityDiagnosticCodes.OutcomeUnreached);

        var replay = await host.ReplayAsync(import, IdempotencyKey);
        var (status, receipt) = await host.GetImportStatusAsync(IdempotencyKey);

        Assert.Equal((int)ReusableActivityImportReceiptStatus.AlreadyImported, (int?)replay.Body["status"]);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(warnings.ToJsonString(), Warnings(replay.Body).ToJsonString());
        Assert.Equal(warnings.ToJsonString(), Warnings(receipt).ToJsonString());
    }

    [Fact]
    public async Task Publication_contract_and_facets_carry_no_conversion_warning()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync(IdempotencyKey, ["reusable-with-behaviour-warning"]);
        var warning = Warnings(import.Apply.Body).Single(warning => (string?)warning!["code"] == Elsa3ActivityDiagnosticCodes.OutcomeUnreached)!;
        var versionId = import.VersionId("escalation-v1");
        Assert.Equal(ReusableActivityPublicationStatus.Published, Assert.Single(PublicationEntry.Of(import.Apply.Body)).Status);

        var publication = await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionVersionPublicationStore>().FindAsync(versionId));
        var version = await host.ReadAsync(services => services.GetRequiredService<IActivityDefinitionLookup>().FindVersion(versionId));

        // The unreached outcome is dropped from the contract, as the warning says; the warning itself is not carried.
        Assert.DoesNotContain("Escalated", publication!.Contract.Outcomes.Select(outcome => outcome.Name));
        var published = JsonSerializer.Serialize(new { publication.Contract, version!.DesignFacets });
        Assert.DoesNotContain(Elsa3ActivityDiagnosticCodes.OutcomeUnreached, published, StringComparison.Ordinal);
        Assert.DoesNotContain((string)warning["message"]!, published, StringComparison.Ordinal);
    }

    private static JsonArray Warnings(JsonNode receipt) => receipt["warnings"]?.AsArray() ?? [];
}
