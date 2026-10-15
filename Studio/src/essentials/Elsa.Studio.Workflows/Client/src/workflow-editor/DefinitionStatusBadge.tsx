import { formatDate } from "../workflowFormatting";
import type { WorkflowDefinitionSummary } from "../workflowTypes";
import { resolveDefinitionPublicationStatus } from "./definitionPublicationStatus";

const tone = { deleted: "cancelled", retired: "cancelled", draft: "suspended", retracted: "suspended" } as const;
const label = { retracted: "Retracted", retired: "Retired", deleted: "Deleted", draft: "Draft" } as const;

export function DefinitionStatusBadge({ definition }: { definition: WorkflowDefinitionSummary }) {
  const publication = resolveDefinitionPublicationStatus(definition);
  const key = publication === "published-with-draft" ? "published" : publication;
  return (
    <>
      <span className="wf-status-badge" data-status={tone[key as keyof typeof tone] ?? "completed"}>
        {label[key as keyof typeof label] ?? "Published"}
      </span>
      {publication === "published-with-draft" ? <small>Newer draft</small> : publication === "deleted" ? <small>{formatDate(definition.deletedAt)}</small> : null}
    </>
  );
}
