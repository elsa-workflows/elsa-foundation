namespace Elsa.Server.Boot;

/// <summary>
/// Times the first HTTP request end-to-end and emits the boot phase table once it completes (spec 129). Placed at
/// the front of the pipeline so the span wraps lazy shell activation triggered by that request. Registered only
/// when the boot switch is on, so it is absent from the pipeline in a normal host.
/// </summary>
public sealed class BootFirstRequestMiddleware(RequestDelegate next, BootPhaseTimeline timeline, ILogger<BootFirstRequestMiddleware> logger)
{
    private int _firstRequestSeen;

    public async Task InvokeAsync(HttpContext context)
    {
        var isFirst = Interlocked.Exchange(ref _firstRequestSeen, 1) == 0;
        if (!isFirst)
        {
            await next(context);
            return;
        }

        var startMs = timeline.ElapsedMs;
        try
        {
            await next(context);
        }
        finally
        {
            timeline.Measure("first-request", startMs, $"{context.Request.Method} {context.Request.Path}");
            timeline.EmitTableOnce(logger, "first request completed");
        }
    }
}
