using System.Globalization;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

internal sealed partial class EfRuleRunStore
{
    public async ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after,
        int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        if (take is < 1 or > RuleLimits.MaxPageEntries)
            throw new ArgumentOutOfRangeException(nameof(take));

        var scope = RuleRecordCodec.ScopeBytes(identity);
        var runKey = RuleRecordCodec.Hash(scope);
        var retainedIdentity = Convert.ToBase64String(scope);
        RuleProgressCursor.Position? position = null;

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunKey == runKey, cancellationToken);
                // Compare the retained full identity before parsing a cursor or projecting any run data.
                if (row is null || !string.Equals(row.IdentityBase64, retainedIdentity, StringComparison.Ordinal))
                    return new(RuleProgressReadDisposition.NotFound, null);

                var state = RuleRowCodec.Read(row);
                long snapshotRevision;
                if (after is null)
                {
                    position = null;
                    snapshotRevision = row.ModelRevision;
                }
                else
                {
                    if (!RuleProgressCursor.TryDecode(after.Cursor, out var decoded)
                        || decoded.RunKey != row.RunKey || decoded.Fingerprint != row.Fingerprint64
                        || decoded.SnapshotRevision > row.ModelRevision)
                        return new(RuleProgressReadDisposition.InvalidPosition, null);
                    position = decoded;
                    snapshotRevision = decoded.SnapshotRevision;

                    var anchor = await ValidateAnchorAsync(db, row, state, decoded, cancellationToken);
                    if (!anchor.Current)
                        continue;
                    if (!anchor.Valid)
                        return new(RuleProgressReadDisposition.InvalidPosition, null);
                }

                var factQuery = db.Facts.AsNoTracking().Where(fact => fact.RunKey == row.RunKey
                    && fact.FirstVisibleModelRevision <= snapshotRevision);
                var roundQuery = db.Receipts.AsNoTracking().Where(receipt => receipt.RunKey == row.RunKey
                    && receipt.AcceptedModelRevision <= snapshotRevision);
                if (position is { } cursor)
                {
                    factQuery = cursor.Kind == RuleProgressItemKind.Fact
                        ? factQuery.Where(fact => fact.FirstVisibleModelRevision > cursor.VisibleRevision
                            || (fact.FirstVisibleModelRevision == cursor.VisibleRevision && fact.FactIndex > cursor.Rank))
                        : factQuery.Where(fact => fact.FirstVisibleModelRevision > cursor.VisibleRevision);
                    roundQuery = cursor.Kind == RuleProgressItemKind.Fact
                        ? roundQuery.Where(receipt => receipt.AcceptedModelRevision >= cursor.VisibleRevision)
                        : roundQuery.Where(receipt => receipt.AcceptedModelRevision > cursor.VisibleRevision
                            || (receipt.AcceptedModelRevision == cursor.VisibleRevision && receipt.Ordinal > cursor.Rank));
                }

                // Pull only bounded numeric ordering projections. Large immutable receipt bodies are loaded
                // one at a time only for items that will actually be returned on this page.
                var factHeaders = await factQuery.OrderBy(fact => fact.FirstVisibleModelRevision).ThenBy(fact => fact.FactIndex)
                    .Select(fact => new FactHeader(fact.FactIndex, fact.FirstVisibleModelRevision))
                    .Take(take + 1).ToArrayAsync(cancellationToken);
                var roundHeaders = await roundQuery.OrderBy(receipt => receipt.AcceptedModelRevision).ThenBy(receipt => receipt.Ordinal)
                    .Select(receipt => new RoundHeader(receipt.Ordinal, receipt.AcceptedModelRevision))
                    .Take(take + 1).ToArrayAsync(cancellationToken);
                if (!await IsCurrentAsync(db, row, cancellationToken)) continue;

                // FactIndex is assigned from canonical atom order within each visibility revision;
                // round ordinal is the canonical round key. These bounded numeric ranks preserve
                // ordinal ordering without relying on provider string collation.
                var candidates = new List<PageCandidate>(factHeaders.Length + roundHeaders.Length);
                candidates.AddRange(factHeaders.Select(header => new PageCandidate(header.VisibleRevision,
                    RuleProgressItemKind.Fact, header.FactIndex, header, null)));
                candidates.AddRange(roundHeaders.Select(header => new PageCandidate(header.VisibleRevision,
                    RuleProgressItemKind.Round, header.Ordinal, null, header)));
                var ordered = candidates.OrderBy(candidate => candidate.VisibleRevision)
                    .ThenBy(candidate => candidate.Kind).ThenBy(candidate => candidate.Rank).ToArray();
                var hasMore = ordered.Length > take;
                var selected = ordered.Take(take).ToArray();
                var items = new List<RuleProgressItem>(selected.Length);

                foreach (var candidate in selected)
                {
                    if (candidate.Fact is { } factHeader)
                    {
                        var factRow = await db.Facts.AsNoTracking().SingleOrDefaultAsync(fact => fact.RunKey == row.RunKey
                            && fact.FactIndex == factHeader.FactIndex, cancellationToken);
                        if (!await IsCurrentAsync(db, row, cancellationToken)) break;
                        if (factRow is null) throw new InvalidDataException("rules.progress-missing-fact");
                        var fact = ReadFact(row, state, factRow, snapshotRevision);
                        items.Add(new(RuleProgressItemKind.Fact, fact.Atom, factHeader.VisibleRevision, fact, null));
                    }
                    else if (candidate.Round is { } roundHeader)
                    {
                        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(record => record.RunKey == row.RunKey
                            && record.Ordinal == roundHeader.Ordinal, cancellationToken);
                        // Every receipt mutation also advances the run row in the same provider transaction.
                        if (!await IsCurrentAsync(db, row, cancellationToken)) break;
                        if (receipt is null) throw new InvalidDataException("rules.progress-missing-receipt");
                        var stored = RuleRoundRecords.Read(row, receipt);
                        Require(stored.Receipt.Identity.Ordinal == roundHeader.Ordinal
                            && stored.AcceptedModelRevision == roundHeader.VisibleRevision, "rules.progress-round-order");
                        var key = RoundKey(roundHeader.Ordinal);
                        var round = new RuleRoundProgress(stored.Receipt.Identity.Ordinal,
                            stored.Receipt.Identity.InputStateSha256, stored.ReceiptId,
                            stored.Receipt.SemanticReceiptSha256, stored.AcceptedModelRevision,
                            stored.ConsumedModelRevision is { } consumedRevision && consumedRevision <= snapshotRevision
                                ? consumedRevision : null,
                            stored.AcceptedAttempt.AttemptId, stored.AcceptedAttempt.WorkerId,
                            stored.AcceptedAttempt.LeaseFence, stored.Receipt.AddedFacts.Count);
                        items.Add(new(RuleProgressItemKind.Round, key, stored.AcceptedModelRevision, null, round));
                    }
                    else
                        throw new InvalidDataException("rules.progress-candidate-shape");
                }

                if (items.Count != selected.Length || !await IsCurrentAsync(db, row, cancellationToken)) continue;

                RuleProgressPosition? next = null;
                if (hasMore)
                {
                    var last = selected[^1];
                    var lastKey = last.Kind == RuleProgressItemKind.Fact
                        ? items[^1].Key : RoundKey(last.Rank);
                    next = new(RuleProgressCursor.Encode(row.RunKey, row.Fingerprint64, snapshotRevision,
                        last.VisibleRevision, last.Kind, last.Rank, lastKey));
                }
                return new(RuleProgressReadDisposition.Page, new(items, next));
            }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return new(RuleProgressReadDisposition.Unavailable, null);
            }
        }

        return new(RuleProgressReadDisposition.Unavailable, null);
    }

    private static async Task<(bool Current, bool Valid)> ValidateAnchorAsync(RulesDbContext db, RuleRunRecord run,
        RuleRowCodec.State state, RuleProgressCursor.Position position, CancellationToken cancellationToken)
    {
        const bool invalid = false;
        if (position.SnapshotRevision <= 0 || position.VisibleRevision > position.SnapshotRevision)
            return (true, invalid);

        if (position.Kind == RuleProgressItemKind.Fact)
        {
            if (position.Rank >= RuleLimits.MaxVocabularyFacts) return (true, invalid);
            var fact = await db.Facts.AsNoTracking().SingleOrDefaultAsync(record => record.RunKey == run.RunKey
                && record.FactIndex == position.Rank, cancellationToken);
            if (!await IsCurrentAsync(db, run, cancellationToken)) return (false, invalid);
            if (fact is null) return (true, invalid);
            var value = ReadFact(run, state, fact, run.ModelRevision);
            var factValid = fact.FirstVisibleModelRevision <= position.SnapshotRevision
                && fact.FirstVisibleModelRevision == position.VisibleRevision
                && RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(value.Atom)) == position.KeyHash;
            return (true, factValid);
        }

        if (position.Rank >= RuleLimits.MaxRounds) return (true, invalid);
        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(record => record.RunKey == run.RunKey
            && record.Ordinal == position.Rank, cancellationToken);
        if (!await IsCurrentAsync(db, run, cancellationToken)) return (false, invalid);
        if (receipt is null) return (true, invalid);
        // Validate the retained anchor body as well as its compact position. It is one bounded row, never
        // a materialized history; page bodies are also loaded one at a time below.
        var stored = RuleRoundRecords.Read(run, receipt);
        var key = RoundKey(stored.Receipt.Identity.Ordinal);
        var valid = stored.Receipt.Identity.Ordinal == position.Rank
            && stored.AcceptedModelRevision == position.VisibleRevision
            && RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(key)) == position.KeyHash;
        return (true, valid);
    }

    private static RuleFact ReadFact(RuleRunRecord run, RuleRowCodec.State state,
        RuleFactHistoryRecord record, long snapshotRevision)
    {
        Require(record.RunKey == run.RunKey && record.SchemaVersion == RulesEfModule.SchemaVersion
            && record.FactIndex >= 0 && record.FactIndex < RuleLimits.MaxVocabularyFacts
            && record.FirstVisibleModelRevision > 0 && record.FirstVisibleModelRevision <= run.ModelRevision
            && record.FirstVisibleModelRevision <= snapshotRevision, "rules.progress-fact-projection");
        var fact = new RuleFact(record.Atom);
        Require(state.Facts.Any(current => current.Atom == fact.Atom), "rules.progress-fact-not-current");
        return fact;
    }

    private static string RoundKey(int ordinal) => ordinal.ToString("D10", CultureInfo.InvariantCulture);

    private sealed record FactHeader(int FactIndex, long VisibleRevision);
    private sealed record RoundHeader(int Ordinal, long VisibleRevision);
    private sealed record PageCandidate(long VisibleRevision, RuleProgressItemKind Kind, int Rank,
        FactHeader? Fact, RoundHeader? Round);

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
