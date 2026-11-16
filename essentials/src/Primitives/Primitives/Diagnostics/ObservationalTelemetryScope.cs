using System.Diagnostics;

namespace Elsa.Primitives.Diagnostics;

/// <summary>
/// Shields application behavior from exceptions raised by in-process telemetry listeners.
/// </summary>
public sealed class ObservationalTelemetryScope : IDisposable
{
    private static readonly ObservationalTelemetryScope Empty = new(null, null);
    private readonly Activity? _parentActivity;
    private Activity? _activity;

    private ObservationalTelemetryScope(Activity? activity, Activity? parentActivity)
    {
        _activity = activity;
        _parentActivity = parentActivity;
    }

    /// <summary>Starts an activity when possible; listener failures produce an empty scope.</summary>
    public static ObservationalTelemetryScope Start(ActivitySource source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Start(() => source, name);
    }

    /// <summary>Creates the source and starts an activity when possible; listener failures produce an empty scope.</summary>
    public static ObservationalTelemetryScope Start(Func<ActivitySource> sourceFactory, string name)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var parentActivity = Activity.Current;
        ActivitySource? source = null;

        for (var attempt = 0; attempt < 2 && source is null; attempt++)
        {
            try
            {
                source = sourceFactory();
            }
            catch (Exception)
            {
                RestoreAfterFailedStart(parentActivity);
            }
        }

        if (source is null)
            return Empty;

        try
        {
            var activity = source.StartActivity(name);
            return activity is null ? Empty : new ObservationalTelemetryScope(activity, parentActivity);
        }
        catch (Exception)
        {
            RestoreAfterFailedStart(parentActivity);
            return Empty;
        }
    }

    /// <summary>Sets an activity status when an activity was started.</summary>
    public void SetStatus(ActivityStatusCode status) => _activity?.SetStatus(status);

    /// <summary>Sets an activity tag when an activity was started.</summary>
    public void SetTag(string key, object? value) => _activity?.SetTag(key, value);

    /// <summary>Emits a metric or other diagnostic side effect without surfacing listener failures.</summary>
    public void Observe(Action observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        try
        {
            observation();
        }
        catch (Exception)
        {
            // Diagnostics are observational and must never change application behavior.
        }
    }

    /// <summary>Creates a diagnostic resource with one retry, then observes it at most once.</summary>
    public void Observe<T>(Func<T> resourceFactory, Action<T> observation) where T : class
    {
        ArgumentNullException.ThrowIfNull(resourceFactory);
        ArgumentNullException.ThrowIfNull(observation);

        if (!TryCreate(resourceFactory, out var resource))
            return;

        Observe(() => observation(resource));
    }

    /// <summary>Stops the activity without surfacing listener failures.</summary>
    public void Dispose()
    {
        var activity = Interlocked.Exchange(ref _activity, null);
        if (activity is null)
            return;
        var wasCurrent = ReferenceEquals(Activity.Current, activity);

        try
        {
            activity.Dispose();
        }
        catch (Exception)
        {
            // Diagnostics are observational and must never change application behavior.
        }
        finally
        {
            if (wasCurrent)
                Activity.Current = _parentActivity;
        }
    }

    private static void RestoreAfterFailedStart(Activity? parentActivity)
    {
        var failedActivity = Activity.Current;
        try
        {
            if (!ReferenceEquals(failedActivity, parentActivity))
                failedActivity?.Dispose();
        }
        catch (Exception)
        {
            // The same listener can throw while the failed activity is being stopped.
        }
        finally
        {
            Activity.Current = parentActivity;
        }
    }

    private static bool TryCreate<T>(Func<T> resourceFactory, out T resource) where T : class
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                resource = resourceFactory();
                return true;
            }
            catch (Exception)
            {
                // Diagnostic resource creation may be retried, but observation itself is never repeated.
            }
        }

        resource = null!;
        return false;
    }
}
