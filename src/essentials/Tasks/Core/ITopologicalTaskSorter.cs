namespace Elsa.Tasks.Core;

/// <summary>
/// Sorts tasks based on their dependencies using topological ordering. Implementations should detect circular dependencies and throw an exception if they occur.
/// </summary>
public interface ITopologicalTaskSorter
{
    IReadOnlyList<T> Sort<T>(IEnumerable<T> tasks) where T : ITask;
}