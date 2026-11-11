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
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (Editor) return;
    let active = true;
    import("./ActivityDataChooser").then(
      module => { if (active) setEditor(() => module.ActivityResultReferenceEditor); },
      error => {
        console.error("[workflow.lazy-boundary] activity output reference failed.", error);
        if (active) setFailed(true);
      });
    return () => { active = false; };
  }, [Editor]);
  if (Editor) return <Editor {...props} />;
  if (!failed) return null;
  // As WorkflowLazyBoundary does for a failed chunk: keep the stored value, say so, and offer a reload (or Clear).
  const stored = props.value && typeof props.value === "object" ? props.value as Record<string, unknown> : {};
  return (
    <div className="wf-lazy-state error" role="alert">
      <span>Studio couldn't load the activity output editor. The stored reference
        {typeof stored.producerNodeId === "string" ? ` (${stored.producerNodeId} › ${String(stored.projectionKey ?? "")})` : ""} is kept.</span>
      <button type="button" className="wf-lazy-reload" onClick={() => window.location.reload()}>Reload page</button>
      {props.disabled ? null : <button type="button" onClick={() => props.onChange(null)}>Clear</button>}
    </div>
  );
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
