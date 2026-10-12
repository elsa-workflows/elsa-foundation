namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Defines the finite provider boundary shared by distributed-runtime queries and host settings.
/// </summary>
public static class DistributedRuntimeQueryLimits
{
    public const int MaximumTake = 500;

    public static int ValidateTake(int value, string parameterName)
    {
        if (value is < 1 or > MaximumTake)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Value must be between 1 and {MaximumTake}.");
        }

        return value;
    }
}
