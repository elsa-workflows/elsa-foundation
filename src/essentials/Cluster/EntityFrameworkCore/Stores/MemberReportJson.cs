using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.EntityFrameworkCore.Stores;

/// <summary>
/// How a member report is stored: named sections, each an explicit document shape, so the stored form never follows a
/// model type's shape by accident.
/// </summary>
/// <remarks>
/// Reading is strict on purpose. A report with a member, a section or an entry field this build does not know was
/// written by a provider version that says something this one cannot interpret, and ignoring the part it cannot read
/// could credit the member with more than it reported. Such a report reads as <see langword="null"/>, which the reader
/// returns as <see cref="MemberReport.Unknown"/>: counted, reading nothing (spec 183, FR-012). A report that lacks an
/// entry field reads the same way: every writer writes every field, null included, so a missing one was not written by
/// this envelope, and reading it as its default would credit the member with a claim it never made.
/// </remarks>
internal static class MemberReportJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static string Write(MemberReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.IsUnknown)
            throw new ArgumentException("An unknown report is what a reader returns; no member publishes one.", nameof(report));

        var readability = report.Readability is { } section
            ? new ReadabilityDocument(section.Entries.Select(EntryDocument.From).ToArray())
            : null;
        return JsonSerializer.Serialize(new ReportDocument(readability), Options);
    }

    /// <summary>The report <paramref name="json"/> holds, or <see langword="null"/> when this build cannot interpret it.</summary>
    public static MemberReport? Read(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ReportDocument>(json, Options);
            if (document is null)
                return null;
            if (document.Readability is not { } readability)
                return new MemberReport();

            return new MemberReport(new ReadabilitySection(readability.Entries.Select(entry => entry.ToEntry())));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ReportDocument(ReadabilityDocument? Readability);

    private sealed record ReadabilityDocument(IReadOnlyList<EntryDocument> Entries);

    /// <summary>
    /// Every field of <see cref="ReadabilityEntry"/>, nullable exactly where it is. A field left out would be dropped on
    /// write and read back as its default: a report that looks whole but says less than the member published. The store
    /// tests round-trip an entry with every field set, so a field <see cref="ReadabilityEntry"/> gains fails them until
    /// it is mapped here.
    /// </summary>
    private sealed record EntryDocument(
        string Family,
        string? EfModule,
        IReadOnlyList<string> ReadableVersions,
        string? DatabaseIdentity,
        string? ObservedFinalizedVersion)
    {
        public static EntryDocument From(ReadabilityEntry entry) =>
            new(entry.Family, entry.EfModule, entry.ReadableVersions, entry.DatabaseIdentity, entry.ObservedFinalizedVersion);

        public ReadabilityEntry ToEntry() => new(Family, EfModule, ReadableVersions, DatabaseIdentity, ObservedFinalizedVersion);
    }
}
