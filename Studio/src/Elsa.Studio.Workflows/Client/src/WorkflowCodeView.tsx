import { useEffect, useMemo, useState } from "react";
import { Check } from "lucide-react";
import { StudioCodeEditor, type StudioCodeDiagnostic, type StudioCodeLanguageAdapter } from "@elsa-workflows/studio-code-editor";
import { serializeDraftToJson } from "./workflowSerialization";
import type { WorkflowDraft } from "./workflowTypes";

const jsonLanguageAdapter: StudioCodeLanguageAdapter = { language: "json", displayName: "JSON" };

export function WorkflowCodeView({ draft, onApply }: {
  draft: WorkflowDraft;
  onApply(text: string): string | null;
}) {
  const serialized = useMemo(() => serializeDraftToJson(draft), [draft]);
  const [text, setText] = useState(serialized);
  const [baseline, setBaseline] = useState(serialized);
  const [error, setError] = useState<string | null>(null);

  // Re-sync the editor whenever the draft changes (Apply, undo/redo, external edits).
  useEffect(() => {
    setText(serialized);
    setBaseline(serialized);
    setError(null);
  }, [serialized]);

  const dirty = text !== baseline;
  const diagnostics: StudioCodeDiagnostic[] = error ? [{ severity: "error", message: error }] : [];

  const apply = () => setError(onApply(text));

  return (
    <div className="wf-code-view">
      <div className="wf-code-view-toolbar">
        <span className="wf-muted">Edit the workflow definition as JSON. Changes apply to the draft when you click Apply.</span>
        <span className="wf-code-view-actions">
          <button type="button" disabled={!dirty} onClick={() => { setText(baseline); setError(null); }}>Reset</button>
          <button type="button" disabled={!dirty} onClick={apply}><Check size={14} /> Apply</button>
        </span>
      </div>
      <div className="wf-code-view-editor">
        <StudioCodeEditor
          ariaLabel="Workflow JSON"
          document={{ uri: "elsa://workflows/definition.json", language: "json", value: text }}
          languageAdapter={jsonLanguageAdapter}
          diagnostics={diagnostics}
          minHeight="100%"
          theme="studio"
          onChange={next => { setText(next.value); if (error) setError(null); }}
        />
      </div>
    </div>
  );
}
