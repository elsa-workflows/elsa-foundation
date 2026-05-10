using Elsa.Common.Contracts;

namespace Elsa.Common.Services
{
    /// <inheritdoc />
    public sealed class SystemClock : ISystemClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
