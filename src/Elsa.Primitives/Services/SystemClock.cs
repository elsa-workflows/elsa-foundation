using Elsa.Primitives.Contracts;

namespace Elsa.Primitives.Services
{
    /// <inheritdoc />
    public sealed class SystemClock : ISystemClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
