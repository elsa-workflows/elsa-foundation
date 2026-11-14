namespace Kernel.Store.Conformance;

internal static class ProjectionIdentityChecks
{
    internal static async Task RunAsync(Context c, Lease lease)
    {
        var position = (await c.S.ProjectedAsync(0)).Written;
        async Task Commit(TurnCommit turn)
        {
            await c.Lands(() => c.Commit(0, lease.Epoch, position, [turn]));
            position++;
        }
        async Task View(string id, string canonical, int revision, string why)
        {
            var view = (await c.S.ReadViewsAsync([id])).SingleOrDefault();
            Context.Equal(new ViewRow(canonical, revision, revision), view, why);
            var tail = await Viewer.ReadAsync(c.S, 0, id);
            Context.Equal(new ViewRow(canonical, revision, revision), tail, why + " through viewer");
        }

        foreach (var (old, fresh, third, count) in new[]
        {
            ("0/Invoice", "0/invoice", "0/INVOICE", 205),
            ("0/café", "0/cafe", "0/CAFÉ", 7),
            ("0/space", "0/space ", "0/space  ", 7),
        })
        {
            await Commit(Make.New(old));
            await Commit(Make.Turn(old, Make.Row(old, 2), expected: 1));
            // Observe this provider's accepted equality, rather than imposing .NET case/accent/space rules.
            var alias = (await c.S.LoadAsync([fresh])).Count == 1;
            var thirdAlias = (await c.S.LoadAsync([third])).Count == 1;
            c.Notes.Add($"projection aliases {old}/{fresh}/{third}: {alias}/{thirdAlias}");
            if (alias) Context.Equal(new ViewRow(old, 2, 2), await Viewer.ReadAsync(c.S, 0, fresh),
                "an alias query before catch-up preserves the committed creation's spelling");
            await new Projector(c.S, 0).CatchUpAsync();
            if (alias) await View(fresh, old, 2, "catch-up must not change the complete aliased view");
            await Commit(Make.Delete(old, 2));
            await Commit(Make.New(fresh));
            await Commit(Make.Turn(thirdAlias && alias ? third : fresh, Make.Row(fresh, 2), expected: 1));
            await Commit(Make.Turn(alias ? old : fresh, Make.Row(fresh, 3), expected: 2));
            await Commit(Make.Turn(fresh, Make.Row(fresh, 4), expected: 3));
            var current = await Viewer.ReadAsync(c.S, 0, fresh);
            Context.Equal(new ViewRow(fresh, 4, 4), current, "unprojected alias history after delete");
            if (alias) Context.Equal(current, await Viewer.ReadAsync(c.S, 0, old),
                "a prior creation's spelling resolves to the current creation's committed spelling");
            await new Projector(c.S, 0).CatchUpAsync();
            await View(fresh, fresh, 4, "interleaved aliases reset once and keep every fresh commit in journal order");

            if (alias) await View(old, fresh, 4, "complete aliased result remains equal after recreation and catch-up");
            await Commit(Make.Delete(fresh, 4));
            await Commit(Make.New(old));
            for (var revision = 2; revision <= count; revision++)
            {
                var id = alias ? (revision % 3) switch { 0 => old, 1 => fresh, _ => thirdAlias ? third : old } : old;
                await Commit(Make.Turn(id, Make.Row(old, revision), expected: revision - 1));
            }
            await new Projector(c.S, 0, take: 1).RoundAsync();
            Context.Equal(0, (await c.S.ReadViewsAsync([fresh])).Count, "alias delete at a page boundary");
            var take = count > 200 ? 201 : 3;
            var pages = new Projector(c.S, 0, take);
            await pages.RoundAsync();
            var partial = (await c.S.ReadViewsAsync([old])).Single();
            Context.Equal(new ViewRow(old, take, take), partial, "first page, including the SQL batch boundary");
            await pages.CatchUpAsync();
            await View(old, old, count, "alias count and revision across projection pages and SQL batches");
        }
    }
}
