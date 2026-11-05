import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import type { WorkflowDraft } from "../workflowTypes";
import { WorkflowCodeView } from "../WorkflowCodeView";

const editor = vi.hoisted(() => ({ props: null as unknown as { readOnly: boolean; document: { value: string }; onChange(value: { value: string }): void } }));
vi.mock("@elsa-workflows/studio-code-editor", () => ({ StudioCodeEditor: (props: typeof editor.props) => {
  editor.props = props;
  return <textarea aria-label="Workflow JSON" readOnly={props.readOnly} value={props.document.value} onChange={event => props.onChange({ value: event.currentTarget.value })} />;
} }));

it("keeps unapplied authoring JSON while displaying an immutable captured snapshot", () => {
  const draft = { id: "draft", definitionId: "definition", state: { rootActivity: null }, layout: [], validationErrors: [] } as WorkflowDraft;
  const snapshot = { ...draft, state: { ...draft.state, name: "captured" } };
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container);
  const onApply = vi.fn(() => null);
  const render = (inspectionSnapshot?: WorkflowDraft) => flushSync(() => root.render(<WorkflowCodeView draft={draft} inspectionSnapshot={inspectionSnapshot} onApply={onApply} />));
  try {
    render(); flushSync(() => editor.props.onChange({ value: "unapplied authoring JSON" }));
    expect(editor.props.document.value).toBe("unapplied authoring JSON");
    render(snapshot); expect(editor.props.readOnly).toBe(true); expect(editor.props.document.value).toContain("captured");
    flushSync(() => editor.props.onChange({ value: "attempted snapshot mutation" }));
    expect(editor.props.document.value).toContain("captured");
    const lockedApply = [...container.querySelectorAll("button")].find(button => button.textContent?.trim() === "Apply")!;
    expect(lockedApply.disabled).toBe(true); expect(onApply).not.toHaveBeenCalled();
    render(); expect(editor.props.document.value).toBe("unapplied authoring JSON");
    const apply = [...container.querySelectorAll("button")].find(button => button.textContent?.trim() === "Apply")!;
    flushSync(() => apply.click()); expect(onApply).toHaveBeenCalledWith("unapplied authoring JSON");
  } finally { flushSync(() => root.unmount()); container.remove(); }
});
