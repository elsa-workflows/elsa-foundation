import { StatusChip, type StudioStatusTone } from "@elsa-workflows/studio-ui";

const statusTones: Record<string, StudioStatusTone> = {
  completed: "success", running: "accent", faulted: "danger", rejected: "danger",
  suspended: "warning", waiting: "warning", stopping: "warning", "needs attention": "warning"
};

export function WorkflowStatusBadge({ status, subStatus }: { status: string; subStatus?: string | null }) {
  return <StatusChip tone={statusTones[status.toLowerCase()] ?? "neutral"}>{subStatus ? `${status} · ${subStatus}` : status}</StatusChip>;
}
