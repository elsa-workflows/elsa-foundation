import type { TaskActor, TaskItem } from "./taskApi";

/**
 * Names a task person the way people recognise them. The signed-in person reads as "You"; anyone else uses the
 * display name the task API returns beside the stored ID, and an ID the directory cannot name stays readable.
 */
export function taskPerson(identity: TaskActor | undefined, id: string | undefined | null, name?: string | null) {
  return !id ? "" : id === identity?.userId ? "You" : name || id;
}

/** The person or group a task currently belongs to: its claimant, else its assignee, else its group. */
export function taskAssignment(identity: TaskActor | undefined, task: TaskItem) {
  return task.claimantId ? taskPerson(identity, task.claimantId, task.claimantName)
    : task.assigneeId ? taskPerson(identity, task.assigneeId, task.assigneeName)
      : taskPerson(identity, task.groupId, task.groupName);
}
