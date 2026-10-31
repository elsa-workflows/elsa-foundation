import { lazy, Suspense, useEffect, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink } from "../api/capabilities";

const WorkflowOrganizationDialog = lazy(() => import("./WorkflowOrganizationDialog").then(module => ({ default: module.WorkflowOrganizationDialog })));

/** Definition organization is independent from the authored document and its pinned-version state. */
export function WorkflowOrganizationButton({ context, definitionId, name, onSaved }: {
  context: StudioEndpointContext; definitionId: string; name: string; onSaved(): void;
}) {
  const [supported, setSupported] = useState(false);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    let active = true;
    setSupported(false); setOpen(false);
    void hasCapabilityLink(context, capabilityIds.workflowDesign, "workflow-organization").then(value => { if (active) setSupported(value); }).catch(() => undefined);
    return () => { active = false; };
  }, [context, definitionId]);
  if (!supported) return null;
  return <><button type="button" onClick={() => setOpen(true)}>Organization</button>{open ? <Suspense fallback={<p role="status">Loading workflow organization…</p>}><WorkflowOrganizationDialog context={context} definitionId={definitionId} name={name} onSaved={onSaved} onClose={() => setOpen(false)} /></Suspense> : null}</>;
}
