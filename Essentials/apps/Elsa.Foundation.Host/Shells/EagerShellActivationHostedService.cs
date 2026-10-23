using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CShells.Hosting;
using CShells.Lifecycle;

namespace Elsa.Foundation.Host.Shells;

/// <summary>
/// Optional. Activates the configured shell(s) at boot so shell-lifetime work starts without waiting for the first HTTP request —
/// most importantly the feed's Tasks feature, whose shell initializer runs every registered startup task and starts the
/// background/recurring task loops on activation.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a HOST-level <see cref="IHostedService"/>: CShells does not run shell-scoped hosted services, so the trigger
/// that activates the shells must live outside any shell. It delegates activation attempts, retries, and their lifetime to the
/// root-owned CShells runner. The service keeps Foundation's ordered pre-listen pass, refusal policy, fatal boundary, and
/// readiness/Attention projection.
/// </para>
/// <para>
/// Gated by <c>Elsa:Boot:EagerShellActivation:Enabled</c> (default on). A shell that fails an ordinary activation remains
/// eligible for independent retries with the capped exponential/jitter delay. An EF module refusal uses the existing
/// maximum-interval retry policy for operator action. Recovery for a target ends if an owned activation succeeds or the runner observes a
/// settled external activation; a later deactivation remains a live readiness failure and does not restart this boot run.
/// </para>
/// <para>
/// The existing fatal CLR exceptions retain Foundation's initial-pass and background behavior. Initial fatal failure cancels
/// the pass before later targets and is rethrown after the owned run joins. A background fatal stops only its target; other
/// targets continue. Shutdown retains and joins the same run across caller-bounded waits.
/// </para>
/// </remarks>
public sealed class EagerShellActivationHostedService(
    IShellRegistry registry,
    IShellActivationRunner runner,
    IConfiguration configuration,
    ShellActivationTracker tracker,
    ILogger<EagerShellActivationHostedService> logger) : IHostedService, IDisposable
{
    public const string EnabledKey = "Elsa:Boot:EagerShellActivation:Enabled";

    // The configured shell names are the child keys of the CShells composition — the same source CShells'
    // own configuration blueprint provider reads, so it matches shells.json exactly.
    private const string ConfiguredShellsSection = "CShells:Shells";

    // Default ON in code (absent or unparseable value means enabled), matching the Foundation reload profile.
    // The default lives here, not only in the shipped appsettings.json, so a
    // consumer who REPLACES appsettings.json (rather than layering onto it) still gets eager activation —
    // set the key to "false" to opt out.
    public static bool IsEnabled(IConfiguration configuration) =>
        !bool.TryParse(configuration[EnabledKey], out var enabled) || enabled;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentQueue<Exception> _lifetimeErrors = new();
    private Task? _startTask;
    private Task<StopResult>? _stopTask;
    private IShellActivationRun? _run;
    private RunContext? _context;
    private CancellationTokenRegistration _startupRegistration;
    private bool _hasStartupRegistration;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_startTask is not null)
                return _startTask;

            if (_stopTask is not null || _disposed)
                return Task.FromCanceled(new CancellationToken(canceled: true));

            _startTask = StartCoreAsync(cancellationToken);
            return _startTask;
        }
    }

    /// <summary>
    /// Requests shutdown and joins the one retained run. The supplied token only bounds this caller's wait; a later StopAsync
    /// observes the same join and does not start another activation attempt.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stop = GetOrStartStopTask();
        var result = await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowStopResult(result);
    }

    public void Dispose()
    {
        Task? start;
        Task<StopResult> stop;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            start = _startTask;
            stop = _stopTask ??= StopAndJoinAsync();
        }

        _ = DisposeAfterJoinAsync(start, stop);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled(configuration))
            return;

        var shellNames = configuration.GetSection(ConfiguredShellsSection)
            .GetChildren()
            .Select(child => child.Key)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (shellNames.Length == 0)
        {
            logger.LogWarning("Eager shell activation is enabled but no shells are configured under '{Section}'.", ConfiguredShellsSection);
            return;
        }

        // Before the first attempt, so a shell that is activated by a request or a reload while it is failing is forgotten.
        tracker.Observe(registry);
        var retry = EagerShellActivationRetryOptions.Read(configuration);
        var context = new RunContext(shellNames, retry);
        _context = context;

        // The runner owns the run token. Forward the host's startup token only while this method is pending; after the
        // initial pass returns successfully, later caller cancellation must not stop Foundation's background recovery.
        var startupRegistration = cancellationToken.Register(static state =>
            ((EagerShellActivationHostedService)state!).ForwardStartupCancellation(), this);
        lock (_gate)
        {
            _startupRegistration = startupRegistration;
            _hasStartupRegistration = true;
        }

        Exception? initialPassError = null;
        try
        {
            var run = runner.Start(shellNames, context.Decide, new AttemptObserver(this, context), _lifetime.Token);
            _run = run;
            try
            {
                await run.InitialPass.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                initialPassError = exception;
            }
        }
        finally
        {
            DisposeStartupRegistration();
        }

        if (context.InitialFatal is { } initialFatal)
        {
            var stopped = await GetOrStartStopTask().ConfigureAwait(false);
            ThrowInitialFatal(initialFatal, stopped);
            return;
        }

        if (initialPassError is not null)
        {
            var stopped = await GetOrStartStopTask().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested && initialPassError is OperationCanceledException)
            {
                ThrowWithCleanupErrors(new OperationCanceledException(cancellationToken), stopped.Errors);
                return;
            }

            ThrowWithCleanupErrors(initialPassError, stopped.Errors);
            return;
        }

    }

    private ValueTask ObserveAttemptAsync(RunContext context, ShellActivationAttempt attempt, CancellationToken runToken)
    {
        if (attempt.Outcome == ShellActivationAttemptOutcome.Succeeded)
        {
            tracker.Activated(attempt.ShellName);
            logger.LogInformation("Eagerly activated shell '{Shell}' at boot.", attempt.ShellName);
            return ValueTask.CompletedTask;
        }

        if (attempt.Exception is OperationCanceledException && runToken.IsCancellationRequested)
            return ValueTask.CompletedTask;

        if (attempt.Exception is { } fatal && IsFatal(fatal))
        {
            RecordFatalAndStopInitial(context, attempt, fatal);

            logger.LogCritical(fatal,
                "Eager activation of shell '{Shell}' failed with a fatal exception on attempt {Attempt}; {Action}.",
                attempt.ShellName, attempt.AttemptNumber, FatalAction(attempt));
            return ValueTask.CompletedTask;
        }

        FailureDetails details;
        try
        {
            details = ReadFailureDetails(attempt.ShellName, attempt.Exception);
        }
        catch (Exception classificationError) when (IsFatal(classificationError))
        {
            RecordFatalAndStopInitial(context, attempt, classificationError);
            logger.LogCritical(classificationError,
                "Eager activation of shell '{Shell}' could not be classified because of a fatal exception on attempt {Attempt}; {Action}.",
                attempt.ShellName, attempt.AttemptNumber, FatalAction(attempt));
            return ValueTask.CompletedTask;
        }
        var refusal = details.Refusal;
        var failureType = attempt.Outcome == ShellActivationAttemptOutcome.NotCurrent
            ? "NotCurrent"
            : attempt.Exception?.GetType().Name ?? attempt.ErrorType ?? "ActivationFailed";

        ShellActivationFailure recorded;
        try
        {
            recorded = tracker.FailedAttempt(
                attempt.ShellName,
                attempt.AttemptNumber,
                failureType,
                refusal,
                attempt.CompletedAt,
                context.Retry);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // Classification or registry inspection should not turn an ordinary activation failure into a permanent
            // policy error. Keep a safe fallback decision and let the host log retain the original activation exception.
            recorded = new ShellActivationFailure(
                attempt.ShellName,
                attempt.AttemptNumber,
                failureType,
                refusal,
                attempt.CompletedAt,
                attempt.CompletedAt,
                context.Retry.MaxDelay);
        }
        catch (Exception fatalClassificationError)
        {
            RecordFatalAndStopInitial(context, attempt, fatalClassificationError);
            logger.LogCritical(fatalClassificationError,
                "Eager activation of shell '{Shell}' could not be recorded because of a fatal exception on attempt {Attempt}; {Action}.",
                attempt.ShellName, attempt.AttemptNumber, FatalAction(attempt));
            return ValueTask.CompletedTask;
        }

        // Capture the exact decision before logging. The logger is external code; if it fails, the runner still receives
        // the same retry decision and the tracker already has the failure projection.
        context.RecordDecision(
            attempt.ShellName,
            attempt.AttemptNumber,
            ShellActivationRetryDecision.RetryAfter(recorded.RetryDelay));

        if (attempt.Outcome == ShellActivationAttemptOutcome.NotCurrent)
        {
            logger.LogWarning(
                "Eager activation of shell '{Shell}' failed because the returned shell was not current (attempt {Attempt}); trying again in {Delay}.",
                attempt.ShellName, recorded.Attempts, recorded.RetryDelay);
        }
        else if (refusal is null)
        {
            logger.LogWarning(attempt.Exception,
                "Eager activation of shell '{Shell}' failed (attempt {Attempt}); trying again in {Delay}.",
                attempt.ShellName, recorded.Attempts, recorded.RetryDelay);
        }
        else
        {
            logger.LogWarning(
                "Eager activation of shell '{Shell}' was refused (attempt {Attempt}) and waits for an operator; checking again in {Delay}. {Error}",
                attempt.ShellName, recorded.Attempts, recorded.RetryDelay, details.Error);
        }

        return ValueTask.CompletedTask;
    }

    private static FailureDetails ReadFailureDetails(string shell, Exception? exception)
    {
        if (exception is null)
            return new(null, string.Empty);

        try
        {
            var described = ShellReloadFailure.Describe(shell, exception, ShellReloadFailure.HostDirectory);
            return new(described.Refusal, described.Error);
        }
        catch (Exception classificationError) when (!IsFatal(classificationError))
        {
            // A malformed third-party refusal is an ordinary activation failure. Its original exception remains available
            // to the host log; the public diagnostic falls back to its type and never includes its message.
            return new(null, string.Empty);
        }
    }

    private static string FatalAction(ShellActivationAttempt attempt) => attempt.AttemptNumber == 1
        ? "stopping the startup run before later targets"
        : "stopping further startup activation of this shell";

    private void ForwardStartupCancellation() => CancelLifetime();

    private void DisposeStartupRegistration()
    {
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            if (!_hasStartupRegistration)
                return;

            registration = _startupRegistration;
            _startupRegistration = default;
            _hasStartupRegistration = false;
        }

        // Dispose waits for an in-flight caller-token callback, so its cancellation errors are retained before the join
        // snapshots them and a late caller cancellation cannot leak into the background run.
        registration.Dispose();
    }

    private void RecordFatalAndStopInitial(RunContext context, ShellActivationAttempt attempt, Exception exception)
    {
        context.RecordFatal(attempt, exception);

        // The initial pass is serial. Cancel synchronously in the observer so the runner's post-observer
        // cancellation check prevents the next configured target from starting.
        if (attempt.AttemptNumber == 1)
            CancelLifetime();
    }

    private void CancelLifetime()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (Exception exception)
        {
            // Cancellation has already been requested even when one or more callbacks fail. Retain those failures for the
            // owned join rather than letting a callback escape through the caller's CancellationTokenSource.Cancel().
            EnqueueErrors(_lifetimeErrors, exception);
        }
    }

    private Task<StopResult> GetOrStartStopTask()
    {
        lock (_gate)
            return _stopTask ??= StopAndJoinAsync();
    }

    private async Task<StopResult> StopAndJoinAsync()
    {
        var errors = new List<Exception>();
        DisposeStartupRegistration();
        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AddErrors(errors, exception);
        }

        while (_lifetimeErrors.TryDequeue(out var cancellationError))
            errors.Add(cancellationError);

        if (_run is { } run)
        {
            try
            {
                // Do not forward a caller token here. StopAsync callers bound only their own wait around this retained task.
                await run.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AddErrors(errors, exception);
            }
        }

        while (_lifetimeErrors.TryDequeue(out var cancellationError))
            errors.Add(cancellationError);

        IReadOnlyList<FatalFailure> fatalFailures = _context?.FatalFailures ?? Array.Empty<FatalFailure>();
        return new StopResult(fatalFailures, errors);
    }

    private async Task DisposeAfterJoinAsync(Task? start, Task<StopResult> stop)
    {
        if (start is not null)
        {
            try
            {
                await start.ConfigureAwait(false);
            }
            catch
            {
                // StartAsync reports its own failure to the host. Disposal waits for its startup-token registration to leave
                // scope before releasing the lifetime source.
            }
        }

        await stop.ConfigureAwait(false);
        // StopAndJoinAsync stores failures as data on the retained task; disposal only waits for its resources to settle.
        _lifetime.Dispose();
    }

    private static void ThrowInitialFatal(FatalFailure fatal, StopResult stopped)
    {
        var errors = new List<Exception> { fatal.DispatchInfo.SourceException };
        errors.AddRange(stopped.Errors);
        errors.AddRange(stopped.BackgroundFatals.Select(item => item.DispatchInfo.SourceException));
        ThrowErrors(errors, fatal.DispatchInfo);
    }

    private static void ThrowWithCleanupErrors(Exception original, IReadOnlyList<Exception> cleanupErrors)
    {
        var errors = new List<Exception> { original };
        errors.AddRange(cleanupErrors);
        ThrowErrors(errors, ExceptionDispatchInfo.Capture(original));
    }

    private static void ThrowStopResult(StopResult result)
    {
        var fatals = result.BackgroundFatals;
        if (fatals.Count == 0 && result.Errors.Count == 0)
            return;

        if (result.Errors.Count == 0)
        {
            // Preserve the configured-order fatal as the shutdown exception. Other fatal identities remain in the retained
            // StopResult and were logged at capture time; concurrent completion order does not choose the surfaced failure.
            fatals[0].DispatchInfo.Throw();
            return;
        }

        var errors = new List<Exception>(fatals.Count + result.Errors.Count);
        errors.AddRange(fatals.Select(item => item.DispatchInfo.SourceException));
        errors.AddRange(result.Errors);
        ThrowErrors(errors, null);
    }

    private static void ThrowErrors(IReadOnlyList<Exception> errors, ExceptionDispatchInfo? preferred)
    {
        if (errors.Count == 1)
        {
            (preferred ?? ExceptionDispatchInfo.Capture(errors[0])).Throw();
            return;
        }

        throw new AggregateException("Eager shell activation failed while the host was starting or stopping.", errors);
    }

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException or BadImageFormatException;

    private static void AddErrors(List<Exception> errors, Exception exception)
    {
        if (exception is AggregateException aggregate)
            errors.AddRange(aggregate.Flatten().InnerExceptions);
        else
            errors.Add(exception);
    }

    private static void EnqueueErrors(ConcurrentQueue<Exception> errors, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
                errors.Enqueue(inner);
        }
        else
        {
            errors.Enqueue(exception);
        }
    }

    private sealed class AttemptObserver(EagerShellActivationHostedService owner, RunContext context) : IShellActivationAttemptObserver
    {
        public ValueTask OnAttemptCompletedAsync(ShellActivationAttempt attempt, CancellationToken cancellationToken) =>
            owner.ObserveAttemptAsync(context, attempt, cancellationToken);
    }

    private sealed class RunContext
    {
        private readonly IReadOnlyDictionary<string, int> _targetOrder;
        private readonly ConcurrentDictionary<string, RetryDecision> _decisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, FatalFailure> _fatalFailures = new();

        public RunContext(IReadOnlyList<string> shellNames, EagerShellActivationRetryOptions retry)
        {
            var targetOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < shellNames.Count; index++)
                targetOrder.TryAdd(shellNames[index], index);

            _targetOrder = targetOrder;
            Retry = retry;
        }

        public EagerShellActivationRetryOptions Retry { get; }

        public IReadOnlyList<FatalFailure> FatalFailures => _fatalFailures.Values.OrderBy(failure => failure.TargetOrder).ToArray();

        public FatalFailure? InitialFatal => _fatalFailures.Values
            .Where(failure => failure.AttemptNumber == 1)
            .OrderBy(failure => failure.TargetOrder)
            .FirstOrDefault();

        public void RecordFatal(ShellActivationAttempt attempt, Exception exception)
        {
            var order = _targetOrder[attempt.ShellName];
            _fatalFailures.TryAdd(order, new FatalFailure(order, attempt.AttemptNumber, ExceptionDispatchInfo.Capture(exception)));
        }

        public void RecordDecision(string shell, int attemptNumber, ShellActivationRetryDecision decision) =>
            _decisions[shell] = new RetryDecision(attemptNumber, decision);

        public ShellActivationRetryDecision Decide(ShellActivationAttempt attempt)
        {
            if (_targetOrder.TryGetValue(attempt.ShellName, out var order) &&
                _fatalFailures.TryGetValue(order, out var fatal) && fatal.AttemptNumber > 1)
                return ShellActivationRetryDecision.Stop;

            return _decisions.TryGetValue(attempt.ShellName, out var decision) && decision.AttemptNumber == attempt.AttemptNumber
                ? decision.Decision
                : ShellActivationRetryDecision.Stop;
        }
    }

    private sealed record FatalFailure(int TargetOrder, int AttemptNumber, ExceptionDispatchInfo DispatchInfo);

    private sealed record RetryDecision(int AttemptNumber, ShellActivationRetryDecision Decision);

    private sealed record FailureDetails(ShellActivationRefusal? Refusal, string Error);

    private sealed record StopResult(IReadOnlyList<FatalFailure> FatalFailures, IReadOnlyList<Exception> Errors)
    {
        public IReadOnlyList<FatalFailure> BackgroundFatals => FatalFailures.Where(failure => failure.AttemptNumber > 1).ToArray();
    }
}
