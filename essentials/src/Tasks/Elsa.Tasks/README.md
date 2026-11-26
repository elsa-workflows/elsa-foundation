# Elsa.Tasks

The `Tasks` shell feature. It registers the task manager and executor that run a shell's tasks: the startup tasks
(`IStartupTask`, in `[TaskDependency]` order) when the shell activates, then its background tasks (`IBackgroundTask`)
and recurring tasks (`IRecurringTask`), which live until the shell stops. The task contracts live in `Elsa.Tasks.Core`.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the `ITaskManager` and `ITaskExecutor` contracts and the
`[SingleNodeTask]` guarantee.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| `StartupTasksOnly` | `false` | When `true`, the shell runs only its startup tasks, in dependency order, when it activates, and starts no background or recurring task. For a process, such as a one-shot tool, that needs the startup work (migrations, seeding, catalog registration) but must not run shell-lifetime work. |

In a shell's configuration:

```json
{
  "Features": {
    "Tasks": { "StartupTasksOnly": true }
  }
}
```
