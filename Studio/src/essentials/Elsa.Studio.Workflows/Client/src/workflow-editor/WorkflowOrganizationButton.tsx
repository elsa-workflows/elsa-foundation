import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import { FolderTree, Tags } from "lucide-react";
import "./workflowOrganizationDialog.css";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { useNotificationGuard } from "./notifications";
import { capabilityIds, hasCapabilityLink } from "../api/capabilities";

const WorkflowTagsDialog = lazy(() => import("./WorkflowTagsDialog").then(module => ({ default: module.WorkflowTagsDialog })));
const MoveWorkflowDefinitionsDialog = lazy(() => import("./MoveWorkflowDefinitionsDialog").then(module => ({ default: module.MoveWorkflowDefinitionsDialog })));

/** Definition organization is independent from the authored document and its pinned-version state. */
export function WorkflowOrganizationButton({ context, definitionId, name, onSaved }: {
  context: StudioEndpointContext; definitionId: string; name: string; onSaved(): void;
}) {
  const [supported, setSupported] = useState(false);
  const [open, setOpen] = useState<{ kind: "tags" | "move"; saved(): void } | null>(null);
  const notificationScope = useMemo(() => ({ context, definitionId }), [context, definitionId]);
  const captureNotificationGuard = useNotificationGuard(notificationScope);
  const openDialog = (kind: "tags" | "move") => {
    const canNotify = captureNotificationGuard();
    setOpen({ kind, saved: () => { if (canNotify()) onSaved(); } });
  };
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
        {canMove ? <StudioButton onClick={() => openDialog("move")}><FolderTree size={16} aria-hidden="true" />Move to folder</StudioButton> : null}
        <StudioButton onClick={() => openDialog("tags")}><Tags size={16} aria-hidden="true" />Edit tags</StudioButton>
      </div>
    </section>
    {open ? <Suspense fallback={<p role="status">Loading workflow organization…</p>}>
      {open.kind === "tags" ? <WorkflowTagsDialog context={context} definitionId={definitionId} name={name} onSaved={open.saved} onClose={() => setOpen(current => current === open ? null : current)} />
        : <MoveWorkflowDefinitionsDialog context={context} definitionIds={[definitionId]} onMoved={open.saved} onClose={() => setOpen(current => current === open ? null : current)} />}
    </Suspense> : null}
  </>;
}
