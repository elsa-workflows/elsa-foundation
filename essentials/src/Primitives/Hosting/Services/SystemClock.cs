using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Hosting.Services;

/// <inheritdoc />
internal sealed class SystemClock : ISystemClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}