import { getWorkflowTemplate, saveWorkflowTemplate, templateContent, type WorkflowTemplate } from "../workflowTemplates";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { WorkflowEditorDocumentAdapter } from "./WorkflowEditorDocumentAdapter";
export function createTemplateDocumentAdapter(): WorkflowEditorDocumentAdapter {
  let current: WorkflowTemplate | null = null;
  let pendingMetadata: { name: string; description?: string | null } | null = null;
  let queue: Promise<unknown> = Promise.resolve();
  const details = (value: WorkflowTemplate): WorkflowDefinitionDetails => ({
    definition: { id: value.id, name: value.name, description: value.description, createdAt: value.createdAt, lastModifiedAt: value.lastModifiedAt, draftId: value.id, versionCount: 0 },
    draft: { id: value.id, definitionId: value.id, ...structuredClone(value.content), validationErrors: [] }, versions: []
  });
  const serialize = <T,>(action: () => Promise<T>) => { const next = queue.then(action, action); queue = next.catch(() => undefined); return next; };
  return {
    load: async (context, id) => { current = await getWorkflowTemplate(context, id); pendingMetadata = null; return details(current); },
    save: (context, draft: WorkflowDraft) => serialize(async () => {
      if (!current || current.id !== draft.id) throw new Error("The template document is not loaded.");
      const pending = pendingMetadata;
      current = await saveWorkflowTemplate(context, { id: current.id, name: pending?.name ?? current.name, description: pending ? pending.description : current.description, expectedRevision: current.revision, content: templateContent(draft) });
      if (pendingMetadata === pending) pendingMetadata = null;
      return details(current).draft!;
    }),
    metadata: (context, id, patch) => {
      pendingMetadata = patch;
      return serialize(async () => {
        if (!current || current.id !== id) throw new Error("The template document is not loaded.");
        current = await saveWorkflowTemplate(context, { id, ...patch, content: current.content, expectedRevision: current.revision });
        if (pendingMetadata === patch) pendingMetadata = null;
        return details(current);
      });
    }
  };
}
