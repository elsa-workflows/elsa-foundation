namespace Elsa.Tasks.Options;

/// <summary>How the shell's initializer starts the registered tasks; bound from <see cref="TasksFeature"/>'s settings.</summary>
internal sealed class TaskStartOptions
{
    /// <summary>When set, the initializer runs only the startup tasks and starts no background or recurring task.</summary>
    public bool StartupTasksOnly { get; set; }
}
