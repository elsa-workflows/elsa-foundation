using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeExecutionIdGeneratorTests
{
    private readonly IncrementingTimeProvider _timeProvider = new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ShortRuntimeExecutionIdGenerator _generator;

    public RuntimeExecutionIdGeneratorTests()
    {
        _generator = new(_timeProvider);
    }

    [Fact]
    public void RuntimeIdsUseShortUnprefixedIdentityFormat()
    {
        var ids = new[]
        {
            _generator.NewWorkflowExecutionId(),
            _generator.NewWorkflowExecutionCommandId(),
            _generator.NewWorkflowExecutionCommandEnvelopeId(),
            _generator.NewActivityExecutionId()
        };

        Assert.All(ids, AssertShortId);
        Assert.All(ids, id => Assert.DoesNotContain("-", id));
        Assert.DoesNotContain(ids, id => id.StartsWith("wfexec-", StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.StartsWith("wfecmd-", StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.StartsWith("wfeenv-", StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.StartsWith("actexec-", StringComparison.Ordinal));
    }

    [Fact]
    public void AddWorkflowRuntimeRegistersShortRuntimeExecutionIdGenerator()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();

        var generator = Assert.IsType<ShortRuntimeExecutionIdGenerator>(provider.GetRequiredService<IRuntimeExecutionIdGenerator>());
        AssertShortId(generator.NewWorkflowExecutionId());
    }

    private static void AssertShortId(string id)
    {
        Assert.Equal(11, id.Length);
        Assert.All(id, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    private sealed class IncrementingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow()
        {
            var value = _current;
            _current = _current.AddMilliseconds(1);
            return value;
        }
    }
}
