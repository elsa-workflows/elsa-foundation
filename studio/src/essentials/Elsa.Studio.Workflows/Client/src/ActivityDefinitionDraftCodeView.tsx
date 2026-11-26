import { useEffect, useMemo, useState } from "react";
import { Check, Redo2, Undo2 } from "lucide-react";
import {
  StudioCodeEditor,
  type StudioCodeDiagnostic,
  type StudioCodeLanguageAdapter
} from "@elsa-workflows/studio-code-editor";
import type { ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import {
  applyActivityDefinitionDraftJson,
  serializeActivityDefinitionDraftJson
} from "./activityDefinitionDraftJson";

const jsonLanguageAdapter: StudioCodeLanguageAdapter = {
  language: "json",
  displayName: "Activity Definition JSON"
};

export interface ActivityDefinitionDraftCodeBufferState {
  dirty: boolean;
  valid: boolean;
}

export function ActivityDefinitionDraftCodeView({
  draft,
  readOnly,
  canUndo,
  canRedo,
  onApply,
  onUndo,
  onRedo,
  onBufferStateChange
}: {
  draft: ActivityDefinitionDraftView;
  readOnly: boolean;
  canUndo: boolean;
  canRedo: boolean;
  onApply(draft: ActivityDefinitionDraftView): void;
  onUndo(): void;
  onRedo(): void;
  onBufferStateChange(state: ActivityDefinitionDraftCodeBufferState): void;
}) {
  const serialized = useMemo(() => serializeActivityDefinitionDraftJson(draft), [draft]);
  const [text, setText] = useState(serialized);
  const [baseline, setBaseline] = useState(serialized);

  useEffect(() => {
    setText(serialized);
    setBaseline(serialized);
  }, [serialized]);

  const dirty = text !== baseline;
  const parsed = dirty ? applyActivityDefinitionDraftJson(draft, text) : { draft };
  const error = "error" in parsed ? parsed.error : null;
  const valid = !error;

  useEffect(() => {
    onBufferStateChange({ dirty, valid });
  }, [dirty, onBufferStateChange, valid]);

  const diagnostics: StudioCodeDiagnostic[] = error
    ? [{ severity: "error", message: error }]
    : [];

  const apply = () => {
    const result = applyActivityDefinitionDraftJson(draft, text);
    if ("error" in result) return;
    onApply(result.draft);
  };

  return (
    <div className="wf-code-view ad-code-view">
      <div className="wf-code-view-toolbar">
        <span className="wf-muted">
          Edit the authoring draft: public contract, provider payload, layout, and presentation label.
          Server identity, revision, lifecycle, and validation state remain outside this document.
        </span>
        <span className="wf-code-view-actions">
          <button
            type="button"
            disabled={dirty || !canUndo || readOnly}
            onClick={onUndo}
          >
            <Undo2 size={14} /> Undo Apply
          </button>
          <button
            type="button"
            disabled={dirty || !canRedo || readOnly}
            onClick={onRedo}
          >
            <Redo2 size={14} /> Redo Apply
          </button>
          <button
            type="button"
            disabled={!dirty || readOnly}
            onClick={() => setText(baseline)}
          >
            Reset
          </button>
          <button type="button" disabled={!dirty || !valid || readOnly} onClick={apply}>
            <Check size={14} /> Apply
          </button>
        </span>
      </div>
      <div className="wf-code-view-editor">
        <StudioCodeEditor
          ariaLabel="Activity Definition JSON"
          document={{
            uri: `elsa://activity-definitions/${encodeURIComponent(draft.definitionId)}/draft.json`,
            language: "json",
            value: text
          }}
          languageAdapter={jsonLanguageAdapter}
          diagnostics={diagnostics}
          readOnly={readOnly}
          minHeight="100%"
          theme="studio"
          onChange={next => setText(next.value)}
        />
      </div>
    </div>
  );
}
