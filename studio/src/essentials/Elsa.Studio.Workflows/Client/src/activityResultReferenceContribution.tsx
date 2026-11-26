import type { ComponentType } from "react";
import type {
  StudioContributionRegistry,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";

/**
 * Registers the readable reference for a stored whole activity output (`ActivityResult`). The registration stays in the
 * module entry, so it holds no editor code: the editor lives in the chooser module, which hands it over when it is
 * evaluated. Every surface that renders inline expression editors (the properties panel, and the Activity Definition
 * graph editor through the inspector) imports that module statically, so the editor is always provided before one
 * mounts.
 */

const activityResultSyntax = "ActivityResult";

let providedEditor: ComponentType<StudioExpressionEditorProps> | null = null;

/** Called by the chooser module when it is evaluated. */
export function provideActivityResultReferenceEditor(editor: ComponentType<StudioExpressionEditorProps>) {
  providedEditor = editor;
}

function ActivityResultReferenceSurface(props: StudioExpressionEditorProps) {
  const Editor = providedEditor;
  return Editor ? <Editor {...props} /> : null;
}

export const activityResultReferenceContribution: StudioExpressionEditorContribution = {
  id: "studio.workflows.activity-result-reference",
  order: 120,
  supports: context => context.syntax === activityResultSyntax,
  surfaces: { inline: ActivityResultReferenceSurface },
  createDefaultValue: () => null
};

export function registerActivityResultReferenceContribution(
  registry: StudioContributionRegistry<StudioExpressionEditorContribution>
) {
  registry.add(activityResultReferenceContribution);
}


/** Member/request surfaces reuse the chooser's provided editor without pulling it into the module entry. */
export const activityDataMemberReferenceContribution: StudioExpressionEditorContribution = {
  id: "studio.workflows.activity-data-member-reference",
  order: 121,
  supports: context => ["ActivityResultMember", "VariableMember", "WorkflowRequestMember", "InputMember", "WorkflowRequest"].includes(context.syntax),
  surfaces: { inline: ActivityResultReferenceSurface },
  createDefaultValue: () => null
};

export function registerActivityDataMemberReferenceContribution(registry: StudioContributionRegistry<StudioExpressionEditorContribution>) {
  registry.add(activityDataMemberReferenceContribution);
}
