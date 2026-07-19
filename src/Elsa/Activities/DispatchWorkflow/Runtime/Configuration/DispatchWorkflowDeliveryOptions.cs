namespace Elsa.Activities.DispatchWorkflow.Runtime.Configuration;

/// <summary>Host policy for durable DispatchWorkflow delivery responsibilities.</summary>
public sealed class DispatchWorkflowDeliveryOptions
{
    public const int DefaultMaxDeliveryAttempts = 4;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

    private int _maxDeliveryAttempts = DefaultMaxDeliveryAttempts;
    private TimeSpan _retryDelay = DefaultRetryDelay;

    /// <summary>Total child-start delivery attempts, including the initial attempt.</summary>
    public int MaxDeliveryAttempts
    {
        get => _maxDeliveryAttempts;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum child-start delivery attempts must be positive.");

            _maxDeliveryAttempts = value;
        }
    }

    /// <summary>Positive delay between child-start delivery attempts.</summary>
    public TimeSpan RetryDelay
    {
        get => _retryDelay;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Child-start retry delay must be positive.");

            _retryDelay = value;
        }
    }
}
