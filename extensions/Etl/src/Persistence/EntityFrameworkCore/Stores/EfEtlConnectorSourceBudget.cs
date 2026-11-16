using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfEtlRuntimeStore
{
    // A response adds only a status, outcome flags, Retry-After, reconciliation time and (for an
    // invalid 200) ResponseReceivedAtUtc. The 512-byte pre-GET reserve covers both UTC instants;
    // replacing InFlight with Waiting releases 256 bytes, and completing Waiting releases the rest.
    // Each new attempt purchases its own history bytes before it can authorize a physical GET.
    private static long HttpSourceOutcomeMetadataReserve(EtlConnectorHttpSourceFetch fetch, EtlHttpPolicy policy) => fetch.Phase switch
    {
        EtlConnectorHttpSourceFetchPhase.InFlight => 512,
        EtlConnectorHttpSourceFetchPhase.Waiting => 256,
        EtlConnectorHttpSourceFetchPhase.Captured or EtlConnectorHttpSourceFetchPhase.Failed => 0,
        _ => throw new InvalidDataException("The source-fetch phase is invalid.")
    };

    private static long HttpSourceCaptureJournalMetadataReserve(EtlConnectorHttpPosition current, EtlHttpPolicy policy)
    {
        var page = SourceBudgetPage(current, policy);
        var capture = new EtlHttpSourceFetchCapture(page, new string('f', 32), long.MaxValue, DateTimeOffset.MaxValue);
        // The capture replaces JSON null; the extra 64 covers phase/update timestamp growth.
        return checked(MetadataBytes(EtlConnectorRecordCodec.Write(capture)) - 4 + SourceBudgetPageExpansion(policy) + 64);
    }

    private static long HttpSourceCaptureProgressMetadataReserve(EtlConnectorHttpPosition current, EtlHttpPolicy policy)
    {
        var page = SourceBudgetPage(current, policy);
        var pageBytes = checked(MetadataBytes(EtlConnectorRecordCodec.Write(page)) + SourceBudgetPageExpansion(policy));
        var attachedBytes = checked(MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(current with { Page = page })) +
            SourceBudgetPageExpansion(policy));
        EnsureSourceDocumentBound(pageBytes);
        EnsureSourceDocumentBound(attachedBytes);
        var nextRequest = current.Request with { Continuation = string.Empty };
        var requestBytes = checked(MetadataBytes(EtlConnectorRecordCodec.Write(nextRequest)) + SourceContinuationExpansion(policy));
        var nextPosition = new EtlConnectorHttpPosition(checked(current.PageSequence + 1), 0, nextRequest, null, false);
        var nextBytes = checked(MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(nextPosition)) + SourceContinuationExpansion(policy));
        EnsureSourceDocumentBound(requestBytes);
        EnsureSourceDocumentBound(nextBytes);
        // Ten bytes cover any positive Int32 cursor digit growth. Charging the entire successor
        // documents (without subtracting the released attached position) is a conservative bound.
        var progress = checked(10 + nextBytes + requestBytes);
        return checked(HttpSourceCaptureJournalMetadataReserve(current, policy) + pageBytes +
            Math.Max(0, attachedBytes - MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(current))) + progress);
    }

    private static long HttpSourceCapturedPageProgressMetadataReserve(EtlConnectorHttpPosition current,
        EtlRetainedHttpPage page, EtlHttpPolicy policy)
    {
        var attached = current with { Page = page };
        return checked(MetadataBytes(EtlConnectorRecordCodec.Write(page)) +
            Math.Max(0, MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(attached)) -
                MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(current))) +
            HttpSourceAttachedProgressMetadataReserve(attached, policy));
    }

    private static long HttpSourceAttachedProgressMetadataReserve(EtlConnectorHttpPosition attached, EtlHttpPolicy policy)
    {
        if (attached.Page is not { } page || attached.Exhausted) return 0;
        EtlConnectorRecordCodec.ValidateHttpPosition(attached, policy);
        var currentBytes = MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(attached));
        var next = AfterHttpPage(attached);
        var nextBytes = MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(next));
        var requestBytes = next.Exhausted ? 0 : MetadataBytes(EtlConnectorRecordCodec.Write(next.Request));
        // The largest intermediate cursor is RecordCount - 1: the final batch removes the page.
        var lastOffset = Math.Max(attached.NextItem, page.RecordCount - 1);
        var lastBytes = MetadataBytes(EtlConnectorRecordCodec.Write<EtlConnectorPosition>(attached with { NextItem = lastOffset }));
        return checked(Math.Max(0, lastBytes - currentBytes) + Math.Max(0, nextBytes + requestBytes - lastBytes));
    }

    private static EtlRetainedHttpPage SourceBudgetPage(EtlConnectorHttpPosition current, EtlHttpPolicy policy) =>
        new(current.PageSequence, EtlRuntimeIdentity.HttpRequestHash(current.Request),
            new(new string('a', 128), new string('f', 64), policy.MaxPageBytes), policy.MaxPageRecords, string.Empty);

    private static long SourceBudgetPageExpansion(EtlHttpPolicy policy) =>
        checked(SourceContinuationExpansion(policy) + SourceStringBound(128) - MetadataBytes(EtlConnectorRecordCodec.Write(new string('a', 128))));

    private static long SourceContinuationExpansion(EtlHttpPolicy policy) =>
        checked(SourceStringBound(policy.MaxContinuationBytes) - MetadataBytes(EtlConnectorRecordCodec.Write(string.Empty)));

    // CLR strings are UTF-16LE Base64 inside a JSON string. For each three code units (eight
    // Base64 characters), '+' is the only Base64 character escaped by the default JSON encoder.
    // Assign indices 0..2, 3..5, 6..7 to the three code units. For UTF-8 costs 1/2/3 their maximum
    // '+' counts are respectively (0,1,3), (1,2,3), (0,0,2). Six bytes per UTF-8 byte covers every
    // combination. Surrogate pairs cost four UTF-8 bytes but their D8..DF high bytes prohibit the
    // high-byte '+' patterns; they remain inside that bound. Sixteen covers partial groups and
    // padding; six covers the quotes and u16: prefix. Invalid isolated surrogates use UTF-8's
    // three-byte replacement for policy measurement while remaining losslessly encoded here.
    private static long SourceStringBound(int maximumUtf8Bytes) => checked(6L * maximumUtf8Bytes + 22);

    private static void EnsureSourceDocumentBound(long bytes)
    {
        if (bytes > EtlConnectorRecordCodec.MaximumDocumentBytes)
            throw new ArgumentException("The source policy cannot guarantee its persisted document bound.");
    }
}
