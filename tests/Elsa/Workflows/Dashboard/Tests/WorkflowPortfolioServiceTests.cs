using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowPortfolioServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CountsCompleteOverlappingPortfolioBeyondNormalPageAndExcludesDeletedAndOtherTenants()
    {
        var definitions = Enumerable.Range(0, 125).Select(index => Definition(index)).Concat([
            Definition(125, deleted: true),
            Definition(126, "tenant-b")
        ]).ToArray();
        var drafts = Enumerable.Range(0, 30).Select(index => Draft($"draft-{index}", $"definition-{index}", Now))
            .Concat([
                Draft("older", "definition-0", Now.AddMinutes(-1)),
                Draft("deleted-draft", "definition-125", Now),
                Draft("other-draft", "definition-126", Now, "tenant-b")
            ]).ToArray();
        var references = Enumerable.Range(0, 50).Select(index => Reference(index)).ToArray();
        var publisher = new ValidationPublisher(new HashSet<string>(["draft-0", "draft-1", "draft-2"], StringComparer.Ordinal));
        var service = new WorkflowPortfolioService(
            new InMemoryWorkflowPortfolioDataSource(definitions, drafts, references),
            publisher,
            new WorkflowPortfolioOptions { MaximumConcurrentValidations = 4 },
            new FixedTimeProvider(Now));

        var result = await service.QueryAsync("tenant-a");

        Assert.Equal(125, result.ActiveDefinitionCount);
        Assert.Equal(50, result.PublishedDefinitionCount);
        Assert.Equal(30, result.UnpublishedDraftCount);
        Assert.Equal(3, result.InvalidDraftCount);
        Assert.True(publisher.MaximumConcurrency <= 4);
        Assert.Equal(30, publisher.ValidatedCount);
    }

    [Fact]
    public async Task TenantIsRequiredBeforeProviderReads()
    {
        var service = new WorkflowPortfolioService(
            new InMemoryWorkflowPortfolioDataSource([], [], []),
            new ValidationPublisher(new HashSet<string>(StringComparer.Ordinal)),
            new WorkflowPortfolioOptions(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(async () => await service.QueryAsync(""));
    }

    private static WorkflowDefinition Definition(int index, string tenant = "tenant-a", bool deleted = false) => new()
    {
        Id = $"definition-{index}",
        TenantId = tenant,
        Name = $"Definition {index}",
        CreatedAt = Now,
        LastModifiedAt = Now,
        DeletedAt = deleted ? Now : null
    };

    private static WorkflowDefinitionDraft Draft(string id, string definitionId, DateTimeOffset modified, string tenant = "tenant-a") => new()
    {
        Id = id,
        WorkflowDefinitionId = definitionId,
        TenantId = tenant,
        State = WorkflowDefinitionState.Empty,
        CreatedAt = modified,
        LastModifiedAt = modified
    };

    private static WorkflowExecutableSourceReference Reference(int index) => new(
        $"reference-{index}", $"artifact-{index}", "WorkflowDefinitionVersion", $"version-{index}", "1",
        $"definition-{index}", $"version-{index}", "1", Now, Now,
        WorkflowExecutableReferenceScope.Published);

    private sealed class ValidationPublisher(IReadOnlySet<string> invalidIds) : IInlineEventPublisher
    {
        private int _active;
        private int _maximum;
        private int _validated;
        public int MaximumConcurrency => _maximum;
        public int ValidatedCount => _validated;

        public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            InterlockedExtensions.Max(ref _maximum, active);
            try
            {
                await Task.Delay(2, cancellationToken);
                var validating = Assert.IsType<DraftValidating>(@event);
                if (invalidIds.Contains(validating.Draft.Id))
                    validating.Errors.Add(new ValidationError("$workflow", "Test", "Invalid"));
                Interlocked.Increment(ref _validated);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current) return;
                current = previous;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
