import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";

/** An authoring document host; lifecycle operations remain exclusive to real workflow definitions. */
export interface WorkflowEditorDocumentAdapter {
  load(context: StudioEndpointContext, id: string): Promise<WorkflowDefinitionDetails>;
  save(context: StudioEndpointContext, draft: WorkflowDraft): Promise<WorkflowDraft>;
  metadata(context: StudioEndpointContext, id: string, patch: { name: string; description?: string | null }): Promise<WorkflowDefinitionDetails>;
}
