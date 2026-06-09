namespace Elsa.Tasks.Core.Attributes;

/// <summary>
/// Configures a task to be executed on a single node in a multi-node environment.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SingleNodeTaskAttribute : Attribute;