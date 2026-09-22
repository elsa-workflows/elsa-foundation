export function WorkflowStatusBadge({ status, subStatus }: { status: string; subStatus?: string | null }) {
  return <span className="wf-status-badge" data-status={status.toLowerCase()}>{subStatus ? `${status} · ${subStatus}` : status}</span>;
}
