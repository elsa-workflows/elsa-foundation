using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-12: the unified fault-capture policy replaces the two divergent ad-hoc conventions
/// (<c>exception.ToString()</c> and message-or-type-name). Type + message are always captured; the stack trace is
/// captured only when explicitly enabled; a blank message never yields an empty fault string.
/// </summary>
public sealed class RuntimeFaultCapturePolicyTests
{
    private static DefaultRuntimeFaultCapturePolicy Policy(bool captureStackTrace = false) =>
        new(Options.Create(new RuntimeFaultCaptureOptions { CaptureStackTrace = captureStackTrace }));

    [Fact]
    public void Capture_CapturesTypeAndMessage_WithoutStackTraceByDefault()
    {
        var policy = Policy();
        Exception exception;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            exception = e;
        }

        var info = policy.Capture(exception);

        Assert.Equal("System.InvalidOperationException", info.ExceptionType);
        Assert.Equal("boom", info.Message);
        Assert.Null(info.StackTrace);
        Assert.Equal("System.InvalidOperationException: boom", info.ToSummaryString());
    }

    [Fact]
    public void Capture_IncludesStackTrace_WhenEnabled()
    {
        var policy = Policy(captureStackTrace: true);
        Exception exception;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            exception = e;
        }

        var info = policy.Capture(exception);

        Assert.False(string.IsNullOrWhiteSpace(info.StackTrace));
    }

    [Fact]
    public void Capture_FallsBackToTypeName_WhenMessageIsBlank()
    {
        var policy = Policy();

        var info = policy.Capture(new BlankMessageException());

        Assert.Equal(nameof(BlankMessageException), info.Message);
    }

    private sealed class BlankMessageException : Exception
    {
        public override string Message => "   ";
    }
}
