using Elsa.Expressions.Core;

namespace Elsa.Workflows.Runtime.Core
{
    /// <summary>
    /// Provides context for storage drivers.
    /// </summary>
    public interface IStorageDriverContext
    {
        IDictionary<string, object> ExecutionContextProperties { get; }

        IVariable Variable { get; }

        CancellationToken CancellationToken { get; }
    }  
}
