namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>
/// One resumption sweep's walk over durable backlog (#2188). It lists executions page by page (at most
/// <see cref="MaxPagesPerSweep"/>), checks each only when the sweep reaches it, passes those that are not ready without
/// a slot, and hands out at most one page's worth of ready executions, BacklogBatchSize being the backlog's bound per
/// sweep.
/// </summary>
internal sealed class BacklogWalk
{
    /// <summary>
    /// Bounds the pages one pass reads while passing held or excluded executions, so a large paused set costs a bounded
    /// amount of work per sweep; the walk carries on from where the pass stopped.
    /// </summary>
    public const int MaxPagesPerSweep = 10;

    private readonly PauseCheck _check;
    private readonly string? _startAfter;
    private readonly int _pageSize;
    private readonly Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>>? _listAfter;
    private readonly List<string> _listed = [];
    private readonly List<string> _held = [];
    private int _visited;
    private int _pagesRead;
    private int _readyLeft;
    private bool _listedToBacklogEnd;

    private BacklogWalk(
        PauseCheck check,
        string? startAfter,
        int pageSize,
        Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>>? listAfter)
    {
        _check = check;
        _startAfter = startAfter;
        _pageSize = pageSize;
        _readyLeft = pageSize;
        _listAfter = listAfter;
    }

    /// <summary>The held executions this walk passed.</summary>
    public IReadOnlyCollection<string> Held => _held;

    public static BacklogWalk After(
        PauseCheck check,
        string? startAfter,
        int pageSize,
        Func<string?, CancellationToken, ValueTask<IReadOnlyCollection<string>>> listAfter) =>
        new(check, startAfter, pageSize, listAfter);

    // A provider that cannot resume after a position offers one page, which is all the backlog the sweep sees.
    public static async ValueTask<BacklogWalk> OverFirstPageAsync(
        PauseCheck check,
        IReadOnlyCollection<string> firstPage,
        CancellationToken cancellationToken)
    {
        var walk = new BacklogWalk(check, startAfter: null, firstPage.Count, listAfter: null) { _listedToBacklogEnd = true };
        walk._listed.AddRange(firstPage);
        await check.ReadHeadsAsync(firstPage, cancellationToken);
        return walk;
    }

    // Counts ready executions ahead of the walk, up to limit, without visiting them.
    public async ValueTask<int> CountReadyAheadAsync(int limit, CancellationToken cancellationToken)
    {
        var ready = 0;
        for (var index = _visited; ready < Math.Min(limit, _readyLeft); index++)
        {
            if (index == _listed.Count && !await ListMoreAsync(cancellationToken))
                break;
            if (await _check.ClassifyAsync(_listed[index], cancellationToken) == Readiness.Ready)
                ready++;
        }

        return ready;
    }

    // Visits executions up to and including the next ready one and returns it, passing the rest on the way; null when
    // the walk cannot go further this sweep.
    public async ValueTask<string?> TakeNextReadyAsync(CancellationToken cancellationToken)
    {
        while (_readyLeft > 0 && (_visited < _listed.Count || await ListMoreAsync(cancellationToken)))
        {
            var workflowExecutionId = _listed[_visited++];
            switch (await _check.ClassifyAsync(workflowExecutionId, cancellationToken))
            {
                case Readiness.Ready:
                    _readyLeft--;
                    return workflowExecutionId;
                case Readiness.Held:
                    _held.Add(workflowExecutionId);
                    break;
            }
        }

        return null;
    }

    // Where the next sweep resumes: from the start once this one visited the end of the backlog, otherwise after the last
    // execution it visited, or where it started when it visited none.
    public string? ResumeAfter()
    {
        if (_listedToBacklogEnd && _visited == _listed.Count)
            return null;
        return _visited > 0 ? _listed[_visited - 1] : _startAfter;
    }

    private async ValueTask<bool> ListMoreAsync(CancellationToken cancellationToken)
    {
        if (_listAfter is null || _listedToBacklogEnd || _pagesRead == MaxPagesPerSweep)
            return false;

        var page = await _listAfter(_listed.Count > 0 ? _listed[^1] : _startAfter, cancellationToken);
        _pagesRead++;
        _listedToBacklogEnd = page.Count < _pageSize;
        _listed.AddRange(page);
        await _check.ReadHeadsAsync(page, cancellationToken);
        return page.Count > 0;
    }
}
