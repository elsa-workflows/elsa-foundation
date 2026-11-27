namespace Elsa.Tasks.Core;

public interface ITaskSchedule
{
    IScheduledTaskExecution ScheduleExecution(Func<Task> action);
}