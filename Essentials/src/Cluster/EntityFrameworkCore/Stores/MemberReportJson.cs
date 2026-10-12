using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.EntityFrameworkCore.Stores;

/// <summary>
/// How a member report is stored: named sections, readability (spec 183) and runnability (spec 184), each an explicit
/// document shape, so the stored form never follows a model type's shape by accident.
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

        var readability = report.Readability is { } readable
            ? new ReadabilityDocument(readable.Entries.Select(EntryDocument.From).ToArray())
            : null;
        var runnability = report.Runnability is { } runnable
            ? new RunnabilityDocument(runnable.Entries.Select(RunnabilityEntryDocument.From).ToArray())
            : null;
        return JsonSerializer.Serialize(new ReportDocument(readability, runnability), Options);
    }

    /// <summary>The report <paramref name="json"/> holds, or <see langword="null"/> when this build cannot interpret it.</summary>
    public static MemberReport? Read(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<ReportDocument>(json, Options) is not { } document)
                return null;

            return new MemberReport(
                document.Readability is { } readability ? new ReadabilitySection(Elements(readability.Entries).Select(entry => entry.ToEntry())) : null,
                document.Runnability is { } runnability ? new RunnabilitySection(Elements(runnability.Entries).Select(entry => entry.ToEntry())) : null);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The elements of a stored list. A null element was not written by this envelope, so the report is
    /// uninterpretable rather than read in part.</summary>
    private static IEnumerable<T> Elements<T>(IEnumerable<T?> elements) where T : class =>
        elements.Select(element => element ?? throw new JsonException("A stored member report holds a null list element."));

    /// <summary>Both sections, each <see langword="null"/> when the member has no source for it (spec 183, FR-014; spec 184,
    /// FR-008). Each is written, null included, so a document without one was not written by this envelope.</summary>
    private sealed record ReportDocument(ReadabilityDocument? Readability, RunnabilityDocument? Runnability);

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
        string? ObservedFinalizedVersion,
        bool ModuleActive)
    {
        public static EntryDocument From(ReadabilityEntry entry) =>
            new(entry.Family, entry.EfModule, entry.ReadableVersions, entry.DatabaseIdentity, entry.ObservedFinalizedVersion, entry.ModuleActive);

        public ReadabilityEntry ToEntry() => new(Family, EfModule, ReadableVersions, DatabaseIdentity, ObservedFinalizedVersion, ModuleActive);
    }

    private sealed record RunnabilityDocument(IReadOnlyList<RunnabilityEntryDocument> Entries);

    /// <summary>
    /// Every field of <see cref="RunnabilityEntry"/>, nullable exactly where it is, on the same terms as
    /// <see cref="EntryDocument"/>: the store tests round-trip an entry with every field of it and of
    /// <see cref="RunnableConsumer"/> set, so a field either gains fails them until it is mapped here. A runnability entry
    /// that reads back with less than was published would place work on a member that never said it can run it.
    /// </summary>
    private sealed record RunnabilityEntryDocument(
        IReadOnlyList<RunnableConsumerDocument> Consumers,
        IReadOnlyList<string> StorageDrivers,
        IReadOnlyList<string> ActivityTypes,
        string? DatabaseIdentity)
    {
        public static RunnabilityEntryDocument From(RunnabilityEntry entry) =>
            new(entry.Consumers.Select(RunnableConsumerDocument.From).ToArray(), entry.StorageDrivers, entry.ActivityTypes, entry.DatabaseIdentity);

        public RunnabilityEntry ToEntry() =>
            new(Elements(Consumers).Select(consumer => consumer.ToConsumer()), StorageDrivers, ActivityTypes, DatabaseIdentity);
    }

    private sealed record RunnableConsumerDocument(string ConsumerKey, IReadOnlyList<string> SchemaVersions)
    {
        public static RunnableConsumerDocument From(RunnableConsumer consumer) => new(consumer.ConsumerKey, consumer.SchemaVersions);

        public RunnableConsumer ToConsumer() => new(ConsumerKey, SchemaVersions);
    }
}
