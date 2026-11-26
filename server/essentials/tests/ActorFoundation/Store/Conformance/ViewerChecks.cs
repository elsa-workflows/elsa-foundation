namespace Kernel.Store.Conformance;

internal static class ViewerChecks
{
    private sealed class InterceptPages(IKernelStore inner,
        Func<int, long, int, CancellationToken, ValueTask<IReadOnlyList<JournalRow>>> read) : RecordingStore(inner)
    {
        public override ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take,
            CancellationToken cancellationToken = default) => read(partition, after, take, cancellationToken);
    }

    private sealed class ProjectionWrites(IKernelStore inner, bool loseFirstAck = false) : RecordingStore(inner), IKernelStore
    {
        public int Bounds, Pages, Writes;

        ValueTask<Projection> IKernelStore.ProjectedAsync(int partition, CancellationToken token)
        {
            Bounds++;
            return base.ProjectedAsync(partition, token);
        }

        public override ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take,
            CancellationToken token = default)
        {
            Pages++;
            return base.ReadJournalAsync(partition, after, take, token);
        }

        async ValueTask<bool> IKernelStore.ProjectAsync(int part, long from, long to, IReadOnlyList<JournalRow> rows,
            CancellationToken token)
        {
            Writes++;
            var landed = await base.ProjectAsync(part, from, to, rows, token);
            if (landed && loseFirstAck && Writes == 1) throw new IOException("projection acknowledgement lost");
            return landed;
        }
    }

    private sealed class CutTail(IKernelStore inner, Func<Task> delete) : RecordingStore(inner)
    {
        public int TailQueries { get; private set; }

        public override async ValueTask<IReadOnlyList<JournalRow>> ReadJournalOfAsync(int partition, string instance,
            long after, CancellationToken cancellationToken = default)
        {
            TailQueries++;
            await delete();
            return await base.ReadJournalOfAsync(partition, instance, after, cancellationToken);
        }

        public override ValueTask<IReadOnlyList<JournalRow>> ReadJournalAsync(int partition, long after, int take,
            CancellationToken cancellationToken = default) => throw new Exception("a viewer scanned partition pages");
    }

    internal static async Task DeleteDuringReadAsync(Context c, Lease lease)
    {
        // a: revision 3 committed before Viewer.ReadAsync starts, projection still at revision 2.
        // Deletion after reading the view removes all three positive rows and appends its own marker.
        var cut = new CutTail(c.S, () => c.Lands(() => c.Commit(0, lease.Epoch, 5, [Make.Delete("0/a", 3)])));
        var wrapped = new RecordingStore(cut);
        var result = await Viewer.ReadAsync(wrapped, 0, "0/a");
        Context.That(result is null, "viewer returned a revision that predated its read after a concurrent delete");
        Context.Equal(1, cut.TailQueries, "one forwarded consistent per-instance query; no partition-page scan");
    }

    public class NoReads : System.Reflection.DispatchProxy
    {
        public int Reads { get; private set; }
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            Reads++;
            throw new InvalidOperationException("viewer reached the store before validating its partition");
        }
    }

    internal static async Task PartitionAsync(Context c)
    {
        var unread = System.Reflection.DispatchProxy.Create<IKernelStore, NoReads>();
        try
        {
            await Viewer.ReadAsync(unread, 1, "0/a");
            throw new InvalidOperationException("viewer accepted a different valid partition");
        }
        catch (ArgumentException) { }
        Context.Equal(0, ((NoReads)unread).Reads, "mismatched partition must be rejected before any store call");
        // InstanceRef accepts these numeric spellings and any name after the first slash.
        foreach (var id in new[] { "00/a/b: ?", "+0/a/b: ?", "0/" })
            Context.That(await Viewer.ReadAsync(c.S, 0, id) is null, $"an existing valid address was narrowed: {id}");
    }

    internal static async Task DeleteDuringCatchUpAsync(Context c)
    {
        var lease = await c.Take(0);
        long position = 0;
        async Task Write(params TurnCommit[] turns)
        {
            await c.Lands(() => c.Commit(0, lease.Epoch, position, turns));
            position += turns.Length;
        }

        foreach (var size in new[] { 1, 2, 3, 100 })
            foreach (var (delete, fillers) in new[] { (false, size), (true, size), (true, 0) })
            {
                var id = $"0/catchup-{size}-{delete}-{fillers}";
                await Write(Make.New(id));
                await new Projector(c.S, 0).CatchUpAsync();
                await Write(Make.Turn(id, Make.Row(id, 2, status: InstanceStatus.Finished), expected: 1));
                if (fillers > 0) await Write(Enumerable.Range(0, fillers).Select(i => Make.New($"{id}/other{i}")).ToArray());

                int pages = 0;
                async ValueTask<IReadOnlyList<JournalRow>> Read(int partition, long after, int take, CancellationToken ct)
                {
                    if (++pages == 1 && delete) await Write(Make.Delete(id, 2));
                    return await c.S.ReadJournalAsync(partition, after, take, ct);
                }
                var counted = new ProjectionWrites(new InterceptPages(c.S, Read));
                var projector = new Projector(counted, 0, size);
                await projector.CatchUpAsync();
                var actual = (await c.S.ReadViewsAsync([id])).SingleOrDefault();
                ViewRow? expected = delete ? null : new(id, 2, 2);
                Context.Equal(expected, actual, $"catch-up page {size}, concurrent delete {delete}, unrelated rows {fillers}");
                Context.Equal(expected, await Viewer.ReadAsync(c.S, 0, id), "independent tail agrees after catch-up");
                var writes = delete && fillers == 0 ? 1 : 2;
                Context.Equal((2, 2, writes), (counted.Bounds, counted.Pages, counted.Writes),
                    "one bound and one page per round; a confirmation with no pending rows does not write");
                if (size == 100) c.Notes.Add($"take 100, delete {delete}, fillers {fillers}: {counted.Bounds} bounds, {counted.Pages} pages, {counted.Writes} guarded writes");
            }

        // Both revisions predate the call. The second delete only occurs after the first gap was observed.
        // Append on every page, including the confirmation, to distinguish two fixed bounds from a moving target.
        const string a = "0/first-gap", b = "0/second-gap", later = "0/after-request";
        await Write(Make.New(a), Make.New(b));
        await new Projector(c.S, 0).CatchUpAsync();
        await Write(Make.Turn(a, Make.Row(a, 2), expected: 1), Make.New("0/intervening"),
            Make.Turn(b, Make.Row(b, 2), expected: 1), Make.New("0/end-of-prefix"));
        int rounds = 0;
        long confirmation = 0;
        async ValueTask<IReadOnlyList<JournalRow>> TwoDeletes(int partition, long after, int size, CancellationToken ct)
        {
            if (++rounds > 6) throw new InvalidOperationException("catch-up chased continuous appends");
            if (rounds == 1) await Write(Make.Delete(a, 2), Make.New(later));
            if (rounds == 2) await Write(Make.Delete(b, 2));
            if (rounds == 3) { confirmation = position; await Write(Make.Delete(later, 1)); }
            await Write(Make.New($"0/append-{rounds}"));
            return await c.S.ReadJournalAsync(partition, after, size, ct);
        }
        await new Projector(new InterceptPages(c.S, TwoDeletes), 0, 1).CatchUpAsync();
        var projected = await c.S.ProjectedAsync(0);
        Context.That(projected.Position == confirmation && projected.Written > confirmation,
            "catch-up must cover both pre-request revisions despite later deletions and continuous appends");
        Context.Equal(0, (await c.S.ReadViewsAsync([a, b])).Count, "both deletion markers applied in this invocation");
        c.Notes.Add($"two gaps and continuous appends: {rounds} rounds, completed at {projected.Position} of {projected.Written}");

        // Our first page contains the contiguous positive revision. A different projector removes it from the
        // materialized path by winning the cursor with the first unrelated row. Only its cursor advance is visible.
        const string raced = "0/competing-projector";
        await Write(Make.New(raced));
        await new Projector(c.S, 0).CatchUpAsync();
        await Write(Make.Turn(raced, Make.Row(raced, 2), expected: 1), Make.New("0/raced-filler"));
        bool compete = true;
        async ValueTask<IReadOnlyList<JournalRow>> Race(int partition, long after, int size, CancellationToken ct)
        {
            var page = await c.S.ReadJournalAsync(partition, after, size, ct);
            if (compete)
            {
                compete = false;
                await Write(Make.Delete(raced, 2));
                await new Projector(c.S, 0, 1).RoundAsync();
            }
            return page;
        }
        await new Projector(new InterceptPages(c.S, Race), 0, 1).CatchUpAsync();
        Context.Equal(0, (await c.S.ReadViewsAsync([raced])).Count, "lost contiguous page cannot vouch for winner's skipped revisions");

        const string unknown = "0/lost-projection-ack";
        await Write(Make.New(unknown));
        await new Projector(c.S, 0).CatchUpAsync();
        await Write(Make.Turn(unknown, Make.Row(unknown, 2), expected: 1), Make.New("0/ack-filler"));
        bool deleteOnRead = true;
        async ValueTask<IReadOnlyList<JournalRow>> LoseAck(int partition, long after, int size, CancellationToken ct)
        {
            if (deleteOnRead) { deleteOnRead = false; await Write(Make.Delete(unknown, 2)); }
            return await c.S.ReadJournalAsync(partition, after, size, ct);
        }
        var retry = new Projector(new ProjectionWrites(new InterceptPages(c.S, LoseAck), loseFirstAck: true), 0, 1);
        try { await retry.CatchUpAsync(); throw new InvalidOperationException("lost acknowledgement was accepted"); }
        catch (IOException e) when (e.Message == "projection acknowledgement lost") { }
        await retry.CatchUpAsync();
        Context.Equal(0, (await c.S.ReadViewsAsync([unknown])).Count, "retry must cross the retained marker after an unknown write");

    }

    private static async Task CancellationAsync(bool gated)
    {
        var memory = new InMemoryStore(1, () => 0);
        for (var i = 1; i <= 7; i++) memory.Journal.Add((0, i), new(0, i, "0/a", i, "[]"));
        memory.Partitions[0].Position = 7;
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = 0;
        ValueTask<IReadOnlyList<JournalRow>> Read(int partition, long after, int size, CancellationToken ct)
        {
            pages++;
            if (pages == 1) cancel.Cancel(); // cancellation inside an otherwise synchronous seven-page store
            if (gated && pages == 2) return Blocked();
            return memory.ReadJournalAsync(partition, after, size, ct);

            async ValueTask<IReadOnlyList<JournalRow>> Blocked()
            {
                entered.SetResult();
                await release.Task;
                return await memory.ReadJournalAsync(partition, after, size, ct);
            }
        }
        var projector = new Projector(new InterceptPages(memory, Read), 0, take: 1);
        var cancelled = projector.CatchUpAsync(cancel.Token);
        Task first = Task.CompletedTask, second = Task.CompletedTask;
        try
        {
            if (gated)
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                first = projector.CatchUpAsync();
                second = projector.CatchUpAsync();
                Context.That(ReferenceEquals(first, second) && !first.IsCompleted,
                    "uncancelled callers did not coalesce behind the blocked shared work");
            }
            try
            {
                await cancelled.WaitAsync(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("cancellation during a synchronous first page was lost");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            if (gated) Context.That(!release.Task.IsCompleted && !first.IsCompleted,
                "cancelled caller did not finish before the shared work was released");
        }
        finally { release.TrySetResult(); }
        // Cancellation may now return while the shared synchronous pages are still running. An uncancelled
        // request supplies an explicit completion witness; it requests one empty follow-up after those pages.
        if (!gated) first = projector.CatchUpAsync();
        await Task.WhenAll(first, second);
        Context.Equal(new Projection(7, 7), await memory.ProjectedAsync(0), "shared projection continued after caller cancellation");
        Context.Equal(8, projector.Rounds, "shared pages plus one coalesced follow-up round");
    }

    private static async Task SynchronousBlockedCancellationAsync()
    {
        var memory = new InMemoryStore(1, () => 0);
        for (var i = 1; i <= 7; i++) memory.Journal.Add((0, i), new(0, i, "0/a", i, "[]"));
        memory.Partitions[0].Position = 7;
        using var cancel = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = 0;
        ValueTask<IReadOnlyList<JournalRow>> Read(int partition, long after, int size, CancellationToken ct)
        {
            var page = Interlocked.Increment(ref pages);
            if (page == 1) cancel.Cancel();
            if (page == 2)
            {
                entered.TrySetResult();
                release.Wait(); // Actual synchronous provider work: no incomplete ValueTask can yield the caller.
            }
            return memory.ReadJournalAsync(partition, after, size, ct);
        }
        var projector = new Projector(new InterceptPages(memory, Read), 0, take: 1);
        // A dedicated initiating thread keeps a defective inline runner from blocking the test's cleanup.
        var initiating = Task.Factory.StartNew(() => projector.CatchUpAsync(cancel.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task first = Task.CompletedTask, second = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var cancelled = await initiating.WaitAsync(TimeSpan.FromSeconds(30));
            try
            {
                await cancelled.WaitAsync(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("initiating caller lost cancellation behind synchronous work");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            first = projector.CatchUpAsync();
            second = projector.CatchUpAsync();
            Context.That(ReferenceEquals(first, second) && !first.IsCompleted && !release.IsSet,
                "cancelled initiating caller must finish while synchronous shared work and followers remain blocked");
        }
        finally { release.Set(); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        Context.Equal(new Projection(7, 7), await memory.ProjectedAsync(0), "synchronous shared pages finished after release");
        Context.Equal(8, projector.Rounds, "seven shared pages and one coalesced follow-up round");
    }

    internal static async Task CatchUpAsync(Context c)
    {
        await CancellationAsync(gated: false);
        await CancellationAsync(gated: true);
        await SynchronousBlockedCancellationAsync();
        TaskCompletionSource? gate = null;
        Exception? failure = null;
        async ValueTask<IReadOnlyList<JournalRow>> Read(int partition, long after, int size, CancellationToken ct)
        {
            if (gate is { } pending)
            {
                gate = null;
                await pending.Task.WaitAsync(ct);
            }
            if (failure is { } error) { failure = null; throw error; }
            return await c.S.ReadJournalAsync(partition, after, size, ct);
        }
        var projector = new Projector(new InterceptPages(c.S, Read), 0, take: 100);
        TaskCompletionSource BlockNext() => gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = BlockNext();
        var round = projector.RoundAsync();
        var first = projector.CatchUpAsync();
        var second = projector.CatchUpAsync();
        using var cancel = new CancellationTokenSource();
        var cancelled = projector.CatchUpAsync(cancel.Token);
        cancel.Cancel();
        try { await cancelled; throw new InvalidOperationException("cancelled caller completed successfully"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Context.That(ReferenceEquals(first, second) && !first.IsCompleted, "waiting callers did not share a pending catch-up");
        release.SetResult();
        await Task.WhenAll(round, first, second);
        Context.Equal(3, projector.Rounds, "one timer round plus two catch-up pages");
        Context.Equal(new Projection(250, 250), await c.S.ProjectedAsync(0), "projection after coalesced catch-up");

        // A round already under way cannot satisfy the next two callers. Only their new round fails.
        projector = new Projector(new InterceptPages(c.S, Read), 0, take: 100);
        release = BlockNext();
        round = projector.RoundAsync();
        first = projector.CatchUpAsync();
        second = projector.CatchUpAsync();
        // Fail on the requested round after the currently blocked timer round returns.
        failure = new IOException("injected catch-up read failure");
        release.SetException(new IOException("injected timer read failure"));
        await round;
        foreach (var waiting in new[] { first, second })
        {
            try { await waiting; throw new InvalidOperationException("catch-up read failure was lost"); }
            catch (IOException error) when (error.Message == "injected catch-up read failure") { }
        }
        await projector.CatchUpAsync();
        Context.Equal(3, projector.Rounds, "failed catch-up followed by a successful fresh round");
        failure = new OperationCanceledException("injected shared read cancellation");
        try { await projector.CatchUpAsync(); throw new InvalidOperationException("shared cancellation was lost"); }
        catch (OperationCanceledException error) when (error.Message == "injected shared read cancellation") { }
        await projector.CatchUpAsync();
        Context.Equal(5, projector.Rounds, "cancelled shared read followed by a successful fresh round");
    }
}
