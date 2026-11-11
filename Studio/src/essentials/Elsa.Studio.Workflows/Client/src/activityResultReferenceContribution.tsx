import { useEffect, useState, type ComponentType } from "react";
import type {
  StudioContributionRegistry,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";

/**
 * Registers the readable reference for a stored whole activity output (`ActivityResult`). The registration stays in the
 * module entry, so it holds no editor code: the editor arrives with the properties panel's chooser chunk, which hands it
 * over when it loads. A surface that renders a stored reference before that chunk has loaded loads it itself.
 */

const activityResultSyntax = "ActivityResult";

let loadedEditor: ComponentType<StudioExpressionEditorProps> | null = null;

/** Called by the chooser module when it is evaluated. */
export function provideActivityResultReferenceEditor(editor: ComponentType<StudioExpressionEditorProps>) {
  loadedEditor = editor;
}

function ActivityResultReferenceSurface(props: StudioExpressionEditorProps) {
  const [Editor, setEditor] = useState(() => loadedEditor);
  useEffect(() => {
    if (Editor) return;
    let active = true;
    void import("./ActivityDataChooser").then(module => {
      if (active) setEditor(() => module.ActivityResultReferenceEditor);
    });
    return () => { active = false; };
  }, [Editor]);
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
