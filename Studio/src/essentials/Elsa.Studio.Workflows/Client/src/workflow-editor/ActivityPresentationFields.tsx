import { useState } from "react";
import { activityDescriptionMaxLength, activityDisplayNameMaxLength } from "../activityPresentation";
import type { ActivityPresentationRecord } from "../workflowTypes";

type Presentation = Pick<ActivityPresentationRecord, "displayName" | "description">;

export function ActivityPresentationFields({ presentation, label, onChange }: {
  presentation?: Presentation | null;
  label: string;
  onChange?(presentation: Presentation): void;
}) {
  const [displayName, setDisplayName] = useEditingValue(presentation?.displayName);
  const [description, setDescription] = useEditingValue(presentation?.description);
  return <div className="wf-activity-presentation-fields">
    <label>
      <span>Display name</span>
      <input type="text" maxLength={activityDisplayNameMaxLength} value={displayName}
        placeholder={label} disabled={!onChange}
        onChange={event => {
          setDisplayName(event.target.value);
          onChange?.({ displayName: event.target.value, description });
        }} />
      <small>{displayName.length}/{activityDisplayNameMaxLength}</small>
    </label>
    <label>
      <span>Description</span>
      <textarea rows={5} maxLength={activityDescriptionMaxLength} value={description}
        placeholder="Explain what this activity does in this workflow." disabled={!onChange}
        onChange={event => {
          setDescription(event.target.value);
          onChange?.({ displayName, description: event.target.value });
        }} />
      <small>{description.length}/{activityDescriptionMaxLength}</small>
    </label>
  </div>;
}

function useEditingValue(value: string | null | undefined) {
  const incoming = value ?? "";
  const [state, setState] = useState({ incoming, editing: incoming });
  if (state.incoming !== incoming) {
    // Keep a word separator while autosave echoes the trimmed value. Actual changes
    // from undo, redo, or another document replace the local field immediately.
    setState({ incoming, editing: state.editing.trim() === incoming.trim() ? state.editing : incoming });
  }
  return [state.editing, (editing: string) => setState(current => ({ ...current, editing }))] as const;
}
