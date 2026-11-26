import type { CreateWorkflowDraft } from "./editorTypes";

/** Copy field ownership so explicit values (including clears) survive template changes and prefill undo. */
export function getCreateMetadataEdits(draft: CreateWorkflowDraft) {
  return draft.metadataEdited ? { ...draft.metadataEdited } : {
    name: !draft.templateId && !draft.starter && draft.name !== "",
    description: !draft.templateId && !draft.starter && draft.description !== ""
  };
}
