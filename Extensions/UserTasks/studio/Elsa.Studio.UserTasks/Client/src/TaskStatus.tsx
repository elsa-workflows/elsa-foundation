import { StatusChip } from "@elsa-workflows/studio-ui";
export function TaskStatus({ status }: { status: string }) {
  return <StatusChip tone={status === "Completed" ? "success" : "neutral"}>{status === "CompletionPending" ? "Response pending" : status}</StatusChip>;
}
