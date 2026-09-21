using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.AuthorityContract;

/// <summary>One out-of-band column edit applied after the writer has stored a consistent row.</summary>
/// <param name="Column">The column to overwrite, by its mapped name.</param>
/// <param name="Value">The replacement value.</param>
public sealed record ActivityAuthorityColumnEdit(string Column, object Value);

/// <summary>
/// One row, the edits that follow it, and the single marker value every provider must produce for it.
/// </summary>
/// <param name="Name">Identifies the case in an assertion message.</param>
/// <param name="Kind">The authority kind the writer stores.</param>
/// <param name="AuthorityKey">The authority key the writer stores.</param>
/// <param name="SourceId">The source id the writer stores, or <c>null</c>.</param>
/// <param name="Expected">The <c>ContentAuthorityIsValid</c> value every provider must report.</param>
/// <param name="Edits">Out-of-band edits applied with raw SQL, as a writer outside the module would make them.</param>
public sealed record ActivityAuthorityContractCase(
    string Name,
    ActivityContentAuthorityKind Kind,
    string AuthorityKey,
    string? SourceId,
    bool Expected,
    IReadOnlyList<ActivityAuthorityColumnEdit> Edits);

/// <summary>
/// The cross-provider contract for the <c>ContentAuthorityIsValid</c> computed column. Each provider proves the
/// marker's meaning with its own SQL, because no single dialect expresses it, so the guarantee that matters is that
/// they all produce the same value for the same row. This table is that guarantee, written once and run by every
/// provider's suite: SQLite in the module's fast tests, and SQL Server, PostgreSQL and MySQL in the ProviderTests leg.
/// </summary>
/// <remarks>
/// The contract is on the marker's value, not on which clause produced it. A provider may reject a row for a
/// different reason than another does (SQL Server reaches several properties through its persisted digest rather
/// than a direct comparison), and that is exactly why comparing the values is the check worth having.
/// </remarks>
public static class ActivityAuthorityValidityContract
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Raw = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthority);
    private const string Canonical = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityCanonicalJson);
    private const string KeyToken = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKeyJson);
    private const string SourceToken = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceIdJson);
    private const string Key = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKey);
    private const string Source = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceId);
    private const string Hash = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIntegrityHash);
    private const string Kind = nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityKind);

    /// <summary>Every row shape and tamper the providers must agree on.</summary>
    public static IReadOnlyList<ActivityAuthorityContractCase> Cases { get; } =
    [
        new("design-authority-round-trips", ActivityContentAuthorityKind.Design, "design", null, true, []),
        new("provider-authority-round-trips", ActivityContentAuthorityKind.ProviderSource, "provider", "source-1", true, []),
        // Characters that every provider's JSON encoder escapes differently, held valid on all of them.
        new("escaped-material-round-trips", ActivityContentAuthorityKind.ProviderSource, "\u00e9<&+\u2028\\\"", "source-\u00e9<&+\u2028\\\"", true, []),

        // The token no longer re-encodes the decoded key. This is the one case whose only failing clause is the
        // token check on every provider, so it is the case a bite-proof mutation of that clause flips.
        new("authority-key-token-replaced", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(KeyToken, "\"tampered\"")]),
        // A scalar-only edit, which agrees with nothing else in the row.
        new("decoded-authority-key-replaced", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Key, "scalar-drift")]),
        new("integrity-hash-replaced", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Hash, "tampered")]),
        // A kind outside the declared enumeration, with the raw JSON left agreeing with nothing.
        new("kind-outside-domain", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Kind, 42)]),
        // $.authorityKey is a number rather than a string, with a decoy nested key that must not be read instead.
        new("authority-key-is-not-a-string", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Raw, NumericKeyJson), new(Canonical, NumericKeyJson), new(KeyToken, "123"), new(SourceToken, "null")]),
        // The token claims an absent source while the raw JSON and the decoded column both carry one.
        new("source-id-token-claims-null", ActivityContentAuthorityKind.ProviderSource, "provider", "source-1", false,
            [new(SourceToken, "null")]),
        // A design authority may not carry a source id, however consistently the row is dressed up.
        new("design-authority-carrying-a-source-id", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Raw, SmuggledSourceJson), new(Canonical, SmuggledSourceJson), new(SourceToken, "\"smuggled\""), new(Source, "smuggled")]),
        // An authority key of nothing but Unicode whitespace, which no provider may accept.
        new("authority-key-is-all-whitespace", ActivityContentAuthorityKind.Design, "design", null, false,
            [new(Raw, WhitespaceKeyJson), new(Canonical, WhitespaceKeyJson)])
    ];

    private static string NumericKeyJson => "{\"kind\":0,\"authorityKey\":123,\"sourceId\":null,\"decoy\":{\"authorityKey\":\"design\"}}";

    private static string SmuggledSourceJson => "{\"kind\":0,\"authorityKey\":\"design\",\"sourceId\":\"smuggled\"}";

    private static string WhitespaceKeyJson => JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, "\t\n\u00a0"), Json);

    /// <summary>
    /// Runs every case against one provider's live database and asserts the marker matches the contract.
    /// <paramref name="suffix"/> keeps rows distinct when a suite reuses one database across tests.
    /// </summary>
    public static async Task RunAsync(ActivitiesDesignDbContext context, string providerName, string suffix)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(ActivityDefinitionManagementProjectionRevision))!;
        var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        var resourceColumn = sql.DelimitIdentifier(nameof(ActivityDefinitionManagementProjectionRevision.ResourceId));

        foreach (var contractCase in Cases)
        {
            var id = $"authority-contract-{contractCase.Name}-{suffix}";
            context.ActivityDefinitionManagementProjections.Add(Projection(id, contractCase));
            await context.SaveChangesAsync();

            if (contractCase.Edits.Count > 0)
            {
                var assignments = contractCase.Edits.Select((edit, index) => $"{sql.DelimitIdentifier(edit.Column)} = {{{index}}}");
                var statement = $"UPDATE {table} SET {string.Join(", ", assignments)} WHERE {resourceColumn} = {{{contractCase.Edits.Count}}}";
                await context.Database.ExecuteSqlRawAsync(statement, [.. contractCase.Edits.Select(edit => edit.Value), id]);
            }

            var marker = await context.ActivityDefinitionManagementProjections.AsNoTracking()
                .Where(row => row.ResourceId == id)
                .Select(row => row.ContentAuthorityIsValid)
                .SingleAsync();
            Assert.True(marker == contractCase.Expected,
                $"{providerName} reported ContentAuthorityIsValid={marker} for contract case '{contractCase.Name}', expected {contractCase.Expected}.");
        }
    }

    private static ActivityDefinitionManagementProjectionRevision Projection(string id, ActivityAuthorityContractCase contractCase) =>
        new()
        {
            Id = id, ResourceId = id, DefinitionId = id, TenantId = "tenant-a",
            ValidFromSequence = 1, ValidToSequenceExclusive = long.MaxValue,
            ValidFromKey = 1L.ToString("D20"), ValidToKey = long.MaxValue.ToString("D20"),
            VisibilityKey = "tenant-a", SortKey = id, SearchText = id,
            ActivityTypeKey = "Acme.AuthorityContract", Category = "Tests",
            ContentAuthority = new(contractCase.Kind, contractCase.AuthorityKey, contractCase.SourceId),
            ContentAuthorityKind = contractCase.Kind, UpdatedAt = DateTimeOffset.UtcNow
        };
}
