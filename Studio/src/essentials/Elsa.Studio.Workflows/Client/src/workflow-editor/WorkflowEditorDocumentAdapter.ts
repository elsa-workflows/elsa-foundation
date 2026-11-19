import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";

/** An authoring document host; lifecycle operations remain exclusive to real workflow definitions. */
export interface WorkflowEditorDocumentAdapter {
  load(context: StudioEndpointContext, id: string): Promise<WorkflowDefinitionDetails>;
  save(context: StudioEndpointContext, draft: WorkflowDraft): Promise<WorkflowDraft>;
  metadata(context: StudioEndpointContext, id: string, patch: { name: string; description?: string | null }): Promise<WorkflowDefinitionDetails>;
  /**
   * The Weaver resource this document is (spec 1193 FR-024): a Reusable Workflow Template is its own resource, disclosed as
   * `workflows.template-editor` with its persisted revision, never as a workflow definition. Absent for a definition.
   */
  weaverResource?: { resourceType: "workflow-template"; contentType: "workflows.template-editor"; revision(): string | null };
}
