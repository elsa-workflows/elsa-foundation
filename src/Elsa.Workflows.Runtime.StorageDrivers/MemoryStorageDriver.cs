using Elsa.Workflows.Runtime.Core;
using System.ComponentModel.DataAnnotations;

namespace Elsa.Workflows.Runtime.StorageDrivers
{
    /// <summary>
    /// A storage driver that stores objects in memory.
    /// </summary>
    [Display(Name = "Memory")]
    public sealed class MemoryStorageDriver : IStorageDriver
    {
        private readonly Dictionary<string, object> _dictionary = [];
        /// <inheritdoc />
        public double Priority => 0;
        /// <inheritdoc />
        public IEnumerable<string> Tags => [];

        /// <inheritdoc />
        public ValueTask WriteAsync(string id, object value, IStorageDriverContext context)
        {
            _dictionary[id] = value;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<object?> ReadAsync(string id, IStorageDriverContext context)
        {
            var value = _dictionary.TryGetValue(id, out var v) ? v : default;
            return new(value);
        }

        /// <inheritdoc />
        public ValueTask DeleteAsync(string id, IStorageDriverContext context)
        {
            _dictionary.Remove(id);
            return ValueTask.CompletedTask;
        }
    }
}
