using System.Collections;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingDurableValuePageMemoTests
{
    private const string WorkflowExecutionId = "execution";

    [Fact]
    public async Task Repeated_reads_return_detached_complete_rows_and_survive_source_json_disposal()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var codec = new EqualCodecIdentity();
        var rowMetadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["row"] = "before" };
        var externalMetadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["external"] = "value" };
        using var schemaDocument = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}");
        using var inlineDocument = JsonDocument.Parse("{\"name\":\"Alice\"}");
        var inlineRow = new DurableValueState(
            "durable-value",
            WorkflowExecutionId,
            "value",
            new RuntimeValueTypeDescriptor("object", "person", schemaDocument.RootElement),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            inlineDocument.RootElement,
            null,
            "activity-execution",
            DateTimeOffset.UnixEpoch,
            rowMetadata);
        var externalRow = new DurableValueState(
            "external-durable-value",
            WorkflowExecutionId,
            "external-value",
            new RuntimeValueTypeDescriptor("object", "person", schemaDocument.RootElement),
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            null,
            new DurableValueExternalReference("store", "locator", externalMetadata),
            "activity-execution",
            DateTimeOffset.UnixEpoch,
            rowMetadata);
        var sourcePage = Page(query, [inlineRow, externalRow], "cursor-next");
        var loads = 0;

        var first = await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(sourcePage);
        });
        rowMetadata["row"] = "after";
        Assert.NotSame(sourcePage.Items, first.Items);
        Assert.NotSame(inlineRow, first.Items[0]);
        Assert.NotSame(inlineRow.Type, first.Items[0].Type);
        Assert.NotSame(externalRow.ExternalReference, first.Items[1].ExternalReference);
        Assert.NotSame(externalRow.ExternalReference!.Metadata, first.Items[1].ExternalReference!.Metadata);

        // Replacing an item in the caller-visible page cannot alter the private admitted snapshot.
        Assert.IsType<DurableValueState[]>(first.Items)[0] = Row("caller-mutation");
        schemaDocument.Dispose();
        inlineDocument.Dispose();

        var hit = await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            throw new Xunit.Sdk.XunitException("A complete exact-key page should be reused.");
        });

        var cached = hit.Items[0];
        var cachedExternal = hit.Items[1];
        Assert.Equal("durable-value", cached.DurableValueId);
        Assert.Equal("before", cached.Metadata["row"]);
        Assert.Equal("value", cachedExternal.ExternalReference!.Metadata["external"]);
        Assert.Equal("{\"name\":\"Alice\"}", cached.InlineValue!.Value.GetRawText());
        Assert.Equal("object", cached.Type.Kind);
        Assert.Equal(inlineRow.WorkflowExecutionId, cached.WorkflowExecutionId);
        Assert.Equal(inlineRow.ValueId, cached.ValueId);
        Assert.Equal(inlineRow.Type.Id, cached.Type.Id);
        Assert.Equal(inlineRow.Lifecycle, cached.Lifecycle);
        Assert.Equal(inlineRow.Storage, cached.Storage);
        Assert.Equal(inlineRow.SourceActivityExecutionId, cached.SourceActivityExecutionId);
        Assert.Equal(inlineRow.CapturedAt, cached.CapturedAt);
        Assert.Equal(externalRow.ExternalReference.StorageProfile, cachedExternal.ExternalReference.StorageProfile);
        Assert.Equal(externalRow.ExternalReference.Locator, cachedExternal.ExternalReference.Locator);
        Assert.Contains("properties", cached.Type.Schema!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("cursor-next", hit.NextContinuationToken);
        Assert.Equal(1, loads);
        Assert.NotSame(first.Items, hit.Items);
    }

    [Fact]
    public async Task Every_request_and_access_component_and_codec_reference_is_part_of_the_key()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new EqualCodecIdentity();
        var query = Query();
        var context = Context();
        var loads = 0;

        async Task Read(DurableValueStatePageQuery request, PersistenceAccessContext access, object continuationCodec)
        {
            await ReadAsync(memo, request, access, continuationCodec, () =>
            {
                loads++;
                return ValueTask.FromResult(Page(request, []));
            });
        }

        await Read(query, context, codec);
        await Read(query, context, codec);
        Assert.Equal(1, loads);

        await Read(query, PersistenceAccessContext.Scoped(new PersistenceScope("other")), codec);
        await Read(query, PersistenceAccessContext.PrivilegedScoped(
            new PersistenceScope("tenant"), new PersistenceAccessPurpose("maintenance-a")), codec);
        await Read(query, PersistenceAccessContext.PrivilegedScoped(
            new PersistenceScope("tenant"), new PersistenceAccessPurpose("maintenance-b")), codec);
        await Read(query, PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("maintenance-a")), codec);
        await Read(Query(workflowExecutionId: "other-execution"), context, codec);
        await Read(Query(limit: 2), context, codec);
        await Read(Query(continuationToken: "cursor-a"), context, codec);
        await Read(query, context, new EqualCodecIdentity());

        Assert.Equal(9, loads);
    }

    [Fact]
    public async Task Successful_empty_pages_are_reused_but_failed_and_cancelled_loads_are_not()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        var emptyQuery = Query();
        var calls = 0;

        var empty = await ReadAsync(memo, emptyQuery, Context(), codec, () =>
        {
            calls++;
            return ValueTask.FromResult(Page(emptyQuery, []));
        });
        var emptyHit = await ReadAsync(memo, emptyQuery, Context(), codec, () =>
        {
            calls++;
            return ValueTask.FromResult(Page(emptyQuery, []));
        });

        Assert.Empty(empty.Items);
        Assert.Empty(emptyHit.Items);
        Assert.Equal(1, calls);

        var failedQuery = Query(workflowExecutionId: "failed");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReadAsync(memo, failedQuery, Context(), codec, () =>
            {
                calls++;
                return ValueTask.FromException<RuntimeStorePage<DurableValueState>>(new InvalidOperationException("provider failure"));
            }));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledQuery = Query(workflowExecutionId: "cancelled");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ReadAsync(memo, cancelledQuery, Context(), codec, () =>
            {
                calls++;
                return ValueTask.FromCanceled<RuntimeStorePage<DurableValueState>>(cancellation.Token);
            }));

        await ReadAsync(memo, failedQuery, Context(), codec, () =>
        {
            calls++;
            return ValueTask.FromResult(Page(failedQuery, []));
        });
        await ReadAsync(memo, failedQuery, Context(), codec, () =>
        {
            calls++;
            throw new Xunit.Sdk.XunitException("The successful retry should now be cached.");
        });

        await ReadAsync(memo, cancelledQuery, Context(), codec, () =>
        {
            calls++;
            return ValueTask.FromResult(Page(cancelledQuery, []));
        });
        await ReadAsync(memo, cancelledQuery, Context(), codec, () =>
        {
            calls++;
            throw new Xunit.Sdk.XunitException("The successful retry should now be cached.");
        });

        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task Exact_page_and_row_limits_are_admitted_and_the_next_result_falls_back_whole()
    {
        var pageMemo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        var pageLoads = 0;
        var firstPageQuery = Query(continuationToken: "page-0");

        for (var index = 0; index < 32; index++)
        {
            var query = Query(continuationToken: $"page-{index}");
            await ReadAsync(pageMemo, query, Context(), codec, () =>
            {
                pageLoads++;
                return ValueTask.FromResult(Page(query, []));
            });
        }

        await ReadAsync(pageMemo, firstPageQuery, Context(), codec, () =>
        {
            pageLoads++;
            return ValueTask.FromResult(Page(firstPageQuery, []));
        });
        Assert.Equal(32, pageLoads);

        var overflowQuery = Query(continuationToken: "page-32");
        var overflowPage = Page(overflowQuery, [Row("complete-overflow")]);
        var fallback = await ReadAsync(pageMemo, overflowQuery, Context(), codec, () =>
        {
            pageLoads++;
            return ValueTask.FromResult(overflowPage);
        });
        Assert.Same(overflowPage, fallback);

        await ReadAsync(pageMemo, firstPageQuery, Context(), codec, () =>
        {
            pageLoads++;
            return ValueTask.FromResult(Page(firstPageQuery, []));
        });
        Assert.Equal(34, pageLoads);

        using (var successfulBoundary = pageMemo.BeginWrite())
            successfulBoundary.Succeed();

        await ReadAsync(pageMemo, firstPageQuery, Context(), codec, () =>
        {
            pageLoads++;
            return ValueTask.FromResult(Page(firstPageQuery, []));
        });
        await ReadAsync(pageMemo, firstPageQuery, Context(), codec, () =>
        {
            pageLoads++;
            return ValueTask.FromResult(Page(firstPageQuery, []));
        });
        Assert.Equal(35, pageLoads);

        var rowMemo = new RuntimeCoalescingDurableValuePageMemo();
        var rowLoads = 0;
        var rowQueries = new[]
        {
            Query(limit: 500, continuationToken: "rows-0"),
            Query(limit: 500, continuationToken: "rows-1"),
            Query(limit: 24, continuationToken: "rows-2"),
        };
        var rowCounts = new[] { 500, 500, 24 };
        for (var index = 0; index < rowQueries.Length; index++)
        {
            var query = rowQueries[index];
            var rows = Enumerable.Range(0, rowCounts[index]).Select(i => Row($"{index}:{i}")).ToArray();
            var page = Page(query, rows);
            var result = await ReadAsync(rowMemo, query, Context(), codec, () =>
            {
                rowLoads++;
                return ValueTask.FromResult(page);
            });
            Assert.Equal(rowCounts[index], result.Items.Count);
        }

        await ReadAsync(rowMemo, rowQueries[0], Context(), codec, () =>
        {
            rowLoads++;
            return ValueTask.FromResult(Page(rowQueries[0], [Row("must-not-load-at-row-cap")]));
        });
        Assert.Equal(3, rowLoads);

        var rowOverflowQuery = Query(limit: 1, continuationToken: "rows-overflow");
        var rowOverflowPage = Page(rowOverflowQuery, [Row("row-1025")]);
        Assert.Same(rowOverflowPage, await ReadAsync(rowMemo, rowOverflowQuery, Context(), codec, () =>
        {
            rowLoads++;
            return ValueTask.FromResult(rowOverflowPage);
        }));
        await ReadAsync(rowMemo, rowQueries[0], Context(), codec, () =>
        {
            rowLoads++;
            return ValueTask.FromResult(Page(rowQueries[0], [Row("again")]));
        });
        Assert.Equal(5, rowLoads);
    }

    [Fact]
    public async Task Exact_content_limit_is_admitted_and_one_more_byte_falls_back_without_truncation()
    {
        const int MaximumContentBytes = 4 * 1024 * 1024;
        // Fixed key/page/row overhead plus the known UTF-8 strings in the one-row test value total 515 bytes.
        const int FixedContentBytes = 515;
        var exactQuery = Query();
        var exactValueIdLength = MaximumContentBytes - FixedContentBytes;
        var exactPage = Page(exactQuery, [Row("v" + new string('x', exactValueIdLength - 1))]);
        var exactMemo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        var exactLoads = 0;

        await ReadAsync(exactMemo, exactQuery, Context(), codec, () =>
        {
            exactLoads++;
            return ValueTask.FromResult(exactPage);
        });
        await ReadAsync(exactMemo, exactQuery, Context(), codec, () =>
        {
            exactLoads++;
            return ValueTask.FromResult(Page(exactQuery, [Row("should-not-load")]));
        });
        Assert.Equal(1, exactLoads);

        var overMemo = new RuntimeCoalescingDurableValuePageMemo();
        var overQuery = Query();
        var overPage = Page(overQuery, [Row("v" + new string('x', exactValueIdLength))]);
        var overCodec = new object();
        var overLoads = 0;
        var overResult = await ReadAsync(overMemo, overQuery, Context(), overCodec, () =>
        {
            overLoads++;
            return ValueTask.FromResult(overPage);
        });
        Assert.Same(overPage, overResult);
        Assert.Single(overResult.Items);
        await ReadAsync(overMemo, overQuery, Context(), overCodec, () =>
        {
            overLoads++;
            return ValueTask.FromResult(Page(overQuery, [Row("complete-retry-result")]));
        });
        Assert.Equal(2, overLoads);
    }

    [Fact]
    public async Task Cumulative_content_cap_clears_prior_entries_and_returns_the_whole_overflow_page()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        var firstQuery = Query(continuationToken: "bytes-a");
        var secondQuery = Query(continuationToken: "bytes-b");
        var firstPage = Page(firstQuery, [Row("a" + new string('a', 3 * 1024 * 1024 - 1))]);
        var metadataEnumerations = 0;
        var metadata = new DynamicMetadata(_ =>
        {
            metadataEnumerations++;
            return "estimated-once";
        });
        var secondPage = Page(secondQuery, [Row("b" + new string('b', 3 * 1024 * 1024 - 1), metadata)]);
        var loads = 0;

        await ReadAsync(memo, firstQuery, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(firstPage);
        });
        Assert.Same(secondPage, await ReadAsync(memo, secondQuery, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(secondPage);
        }));
        Assert.Equal(1, metadataEnumerations); // Reject the cumulative budget before allocating a detached copy.

        await ReadAsync(memo, firstQuery, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(firstQuery, [Row("complete-provider-result-after-cap")]));
        });
        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task Provider_owned_mutation_between_estimate_and_clone_cannot_admit_an_oversized_snapshot()
    {
        var query = Query();
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        var changingMetadata = new DynamicMetadata(index => index == 1 ? "small" : new string('x', 4 * 1024 * 1024));
        var row = new DurableValueState(
            "mutable-provider-row",
            WorkflowExecutionId,
            "value",
            new RuntimeValueTypeDescriptor("string", null, null),
            DurableValueLifecycle.None,
            DurableValueStorage.None,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            changingMetadata);
        var page = Page(query, [row]);
        var loads = 0;

        Assert.Same(page, await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(page);
        }));

        // The second metadata enumeration occurs during the detached clone and now carries over-cap content.
        var fallback = Page(query, [Row("complete-provider-fallback")]);
        Assert.Same(fallback, await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(fallback);
        }));
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Estimate_and_clone_failures_fall_back_complete_and_do_not_poison_a_later_page()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var codec = new object();
        var loads = 0;
        using var jsonDocument = JsonDocument.Parse("{\"value\":\"expired\"}");
        var disposedRow = new DurableValueState(
            "disposed-json",
            WorkflowExecutionId,
            "value",
            new RuntimeValueTypeDescriptor("object", null, null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            jsonDocument.RootElement,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var disposedPage = Page(query, [disposedRow]);
        jsonDocument.Dispose();

        Assert.Same(disposedPage, await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(disposedPage);
        }));

        var metadata = new DynamicMetadata(index => index == 1
            ? "estimated"
            : throw new InvalidOperationException("The provider metadata changed during snapshot copy."));
        var cloneFailurePage = Page(query, [new DurableValueState(
            "clone-failure",
            WorkflowExecutionId,
            "value",
            new RuntimeValueTypeDescriptor("string", null, null),
            DurableValueLifecycle.None,
            DurableValueStorage.None,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            metadata)]);
        Assert.Same(cloneFailurePage, await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(cloneFailurePage);
        }));

        var healthyPage = Page(query, [Row("healthy-after-fallback")]);
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(healthyPage);
        });
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            throw new Xunit.Sdk.XunitException("The healthy page should be admitted after either fallback.");
        });
        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task Duplicate_concurrent_miss_at_page_limit_does_not_disable_an_admitted_generation()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var codec = new object();
        for (var index = 0; index < 31; index++)
        {
            var query = Query(continuationToken: $"occupied-{index}");
            await ReadAsync(memo, query, Context(), codec, () => ValueTask.FromResult(Page(query, [])));
        }

        var duplicateQuery = Query(continuationToken: "last-slot");
        var firstProviderRead = new TaskCompletionSource<RuntimeStorePage<DurableValueState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProviderRead = new TaskCompletionSource<RuntimeStorePage<DurableValueState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCalls = 0;
        var first = ReadAsync(memo, duplicateQuery, Context(), codec, () =>
        {
            providerCalls++;
            return new ValueTask<RuntimeStorePage<DurableValueState>>(firstProviderRead.Task);
        });
        var second = ReadAsync(memo, duplicateQuery, Context(), codec, () =>
        {
            providerCalls++;
            return new ValueTask<RuntimeStorePage<DurableValueState>>(secondProviderRead.Task);
        });

        var firstPage = Page(duplicateQuery, []);
        firstProviderRead.SetResult(firstPage);
        await first;
        var secondPage = Page(duplicateQuery, [Row("v" + new string('x', 4 * 1024 * 1024))]);
        secondProviderRead.SetResult(secondPage);
        Assert.Same(secondPage, await second);
        Assert.Equal(2, providerCalls);

        var existingQuery = Query(continuationToken: "occupied-0");
        await ReadAsync(memo, existingQuery, Context(), codec, () =>
            throw new Xunit.Sdk.XunitException("A duplicate completion must not clear the 32-page generation."));
    }

    [Fact]
    public async Task Active_and_failed_writes_fence_hits_and_late_fills_and_failure_is_permanent()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var context = Context();
        var codec = new object();
        var loads = 0;

        await ReadAsync(memo, query, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, [Row("before-write")]));
        });

        var activeWrite = memo.BeginWrite();
        var duringWritePage = Page(query, [Row("during-write")]);
        Assert.Same(duringWritePage, await ReadAsync(memo, query, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(duringWritePage);
        }));
        activeWrite.Succeed();
        activeWrite.Dispose();

        var pending = new TaskCompletionSource<RuntimeStorePage<DurableValueState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateLoad = ReadAsync(memo, Query(workflowExecutionId: "late"), context, codec, () =>
        {
            loads++;
            return new ValueTask<RuntimeStorePage<DurableValueState>>(pending.Task);
        });
        using (var successfulWrite = memo.BeginWrite())
            successfulWrite.Succeed();
        var lateQuery = Query(workflowExecutionId: "late");
        var latePage = Page(lateQuery, [Row("late-result")]);
        pending.SetResult(latePage);
        Assert.Same(latePage, await lateLoad);

        await ReadAsync(memo, lateQuery, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(lateQuery, [Row("fresh-result")]));
        });
        await ReadAsync(memo, lateQuery, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(lateQuery, [Row("should-hit") ]));
        });
        Assert.Equal(4, loads);

        // Disposal without success denotes a failed or cancelled write; a later success cannot reactivate this owner.
        memo.BeginWrite().Dispose();
        using (var laterSuccessfulWrite = memo.BeginWrite())
            laterSuccessfulWrite.Succeed();

        await ReadAsync(memo, lateQuery, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(lateQuery, [Row("after-failure-1")]));
        });
        await ReadAsync(memo, lateQuery, context, codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(lateQuery, [Row("after-failure-2")]));
        });
        Assert.Equal(6, loads);
    }

    [Fact]
    public async Task Overlapping_write_leases_keep_reads_bypassed_until_all_succeed_and_any_failure_is_permanent()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var codec = new object();
        var loads = 0;
        var firstWrite = memo.BeginWrite();
        var secondWrite = memo.BeginWrite();

        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        firstWrite.Succeed();
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        secondWrite.Succeed();
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        Assert.Equal(3, loads);

        var failingMemo = new RuntimeCoalescingDurableValuePageMemo();
        var successfulConcurrentWrite = failingMemo.BeginWrite();
        var failedConcurrentWrite = failingMemo.BeginWrite();
        successfulConcurrentWrite.Succeed();
        failedConcurrentWrite.Dispose();
        using (var laterWrite = failingMemo.BeginWrite())
            laterWrite.Succeed();

        var failureLoads = 0;
        await ReadAsync(failingMemo, query, Context(), codec, () =>
        {
            failureLoads++;
            return ValueTask.FromResult(Page(query, []));
        });
        await ReadAsync(failingMemo, query, Context(), codec, () =>
        {
            failureLoads++;
            return ValueTask.FromResult(Page(query, []));
        });
        Assert.Equal(2, failureLoads);
    }

    [Fact]
    public async Task Disable_permanently_rejects_a_provider_fill_already_in_flight()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var codec = new object();
        var pending = new TaskCompletionSource<RuntimeStorePage<DurableValueState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        var read = ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return new ValueTask<RuntimeStorePage<DurableValueState>>(pending.Task);
        });

        memo.DisablePermanently();
        var providerPage = Page(query, [Row("complete-late-owner-result")]);
        pending.SetResult(providerPage);
        Assert.Same(providerPage, await read);

        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, [Row("complete-after-disable-result")]));
        });
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Permanent_disable_cannot_be_reactivated_by_a_successful_write_boundary()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query();
        var codec = new object();
        var loads = 0;
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        memo.DisablePermanently();
        using (var write = memo.BeginWrite())
            write.Succeed();

        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        await ReadAsync(memo, query, Context(), codec, () =>
        {
            loads++;
            return ValueTask.FromResult(Page(query, []));
        });
        Assert.Equal(3, loads);
    }

    [Fact]
    public async Task Provider_list_that_grows_past_its_original_query_limit_is_returned_whole_without_admission()
    {
        var memo = new RuntimeCoalescingDurableValuePageMemo();
        var query = Query(limit: 1);
        var codec = new object();
        var rows = new List<DurableValueState> { Row("original") };
        var page = Page(query, rows);
        rows.Add(Row("added-after-construction"));

        Assert.Same(page, await ReadAsync(memo, query, Context(), codec, () => ValueTask.FromResult(page)));
        Assert.Equal(2, page.Items.Count);

        var next = Page(query, [Row("complete-provider-result")]);
        Assert.Same(next, await ReadAsync(memo, query, Context(), codec, () => ValueTask.FromResult(next)));
    }

    private static ValueTask<RuntimeStorePage<DurableValueState>> ReadAsync(
        RuntimeCoalescingDurableValuePageMemo memo,
        DurableValueStatePageQuery query,
        PersistenceAccessContext context,
        object codecIdentity,
        Func<ValueTask<RuntimeStorePage<DurableValueState>>> load) =>
        memo.GetOrLoadAsync(query, context, codecIdentity, load);

    private static DurableValueStatePageQuery Query(
        string workflowExecutionId = WorkflowExecutionId,
        int limit = 100,
        string? continuationToken = null) =>
        new(workflowExecutionId, limit, continuationToken);

    private static PersistenceAccessContext Context() =>
        PersistenceAccessContext.Scoped(new PersistenceScope("tenant"));

    private static RuntimeStorePage<DurableValueState> Page(
        DurableValueStatePageQuery query,
        IReadOnlyList<DurableValueState> rows,
        string? nextContinuationToken = null) =>
        new(query, rows, nextContinuationToken);

    private static DurableValueState Row(string durableValueId, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            durableValueId,
            WorkflowExecutionId,
            "value",
            new RuntimeValueTypeDescriptor("string", null, null),
            DurableValueLifecycle.None,
            DurableValueStorage.None,
            null,
            null,
            null,
            DateTimeOffset.UnixEpoch,
            metadata ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed class EqualCodecIdentity
    {
        public override bool Equals(object? obj) => obj is EqualCodecIdentity;

        public override int GetHashCode() => 1;
    }

    private sealed class DynamicMetadata(Func<int, string> valueForEnumeration) : IReadOnlyDictionary<string, string>
    {
        private int _enumerationCount;

        private string CurrentValue => valueForEnumeration(Volatile.Read(ref _enumerationCount));

        public string this[string key] => key == "value" ? CurrentValue : throw new KeyNotFoundException(key);
        public IEnumerable<string> Keys => ["value"];
        public IEnumerable<string> Values => [CurrentValue];
        public int Count => 1;

        public bool ContainsKey(string key) => key == "value";

        public bool TryGetValue(string key, out string value)
        {
            value = CurrentValue;
            return key == "value";
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            var value = valueForEnumeration(Interlocked.Increment(ref _enumerationCount));
            yield return new KeyValuePair<string, string>("value", value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
