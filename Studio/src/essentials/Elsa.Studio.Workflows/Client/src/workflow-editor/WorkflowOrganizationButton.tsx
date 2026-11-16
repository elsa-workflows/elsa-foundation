import { lazy, Suspense, useEffect, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { FolderTree, Tags } from "lucide-react";
import "./workflowOrganizationDialog.css";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "../api/capabilities";

const WorkflowTagsDialog = lazy(() => import("./WorkflowTagsDialog").then(module => ({ default: module.WorkflowTagsDialog })));
const MoveWorkflowDefinitionsDialog = lazy(() => import("./MoveWorkflowDefinitionsDialog").then(module => ({ default: module.MoveWorkflowDefinitionsDialog })));

/** Definition organization is independent from the authored document and its pinned-version state. */
export function WorkflowOrganizationButton({ context, definitionId, name, onSaved }: {
  context: StudioEndpointContext; definitionId: string; name: string; onSaved(): void;
}) {
  const [supported, setSupported] = useState(false);
  const [open, setOpen] = useState<"tags" | "move" | null>(null);
  const [canMove, setCanMove] = useState(false);
  useEffect(() => {
    let active = true;
    setSupported(false); setOpen(null); setCanMove(false);
    void hasCapabilityLink(context, capabilityIds.workflowDesign, "workflow-organization").then(value => { if (active) setSupported(value); }).catch(() => undefined);
    void import("../api/workflowOrganization").then(async ({ getWorkflowOrganization }) => {
      if (!await hasCapabilityLink(context, capabilityIds.workflowDesign, "workflow-organization")
        || !await hasCapabilityLink(context, capabilityIds.workflowDesign, "workflow-definition-folder-move")) return;
      const organization = await getWorkflowOrganization(context, definitionId);
      if (active) setCanMove(organization.permissions?.canManageFolders === true);
    }).catch(() => undefined);
    return () => { active = false; };
  }, [context, definitionId]);
  if (!supported) return null;
  return <>
    <section className="wf-organization-entry" aria-label="Workflow organization">
      <div><h3>Organization</h3><p>Keep this workflow in a folder and classify it with tags.</p></div>
      <div className="wf-organization-entry-actions">
        {canMove ? <StudioButton onClick={() => setOpen("move")}><FolderTree size={16} aria-hidden="true" />Move to folder</StudioButton> : null}
        <StudioButton onClick={() => setOpen("tags")}><Tags size={16} aria-hidden="true" />Edit tags</StudioButton>
      </div>
    </section>
    {open ? <Suspense fallback={<p role="status">Loading workflow organization…</p>}>
      {open === "tags" ? <WorkflowTagsDialog context={context} definitionId={definitionId} name={name} onSaved={onSaved} onClose={() => setOpen(null)} />
        : <MoveWorkflowDefinitionsDialog context={context} definitionIds={[definitionId]} onMoved={onSaved} onClose={() => setOpen(null)} />}
    </Suspense> : null}
  </>;
}
