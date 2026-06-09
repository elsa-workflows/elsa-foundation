using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Provides context for storage drivers.
/// </summary>
public interface IStorageDriverContext
{
    IDictionary<string, object> ExecutionContextProperties { get; }

    IVariable Variable { get; }

    CancellationToken CancellationToken { get; }
}