import React, { useRef, useState } from "react";
import { StudioDialog } from "../overlay/StudioDialog";
import { StudioDialogFooter } from "../layout/DialogFooter";
import { StudioField } from "./Field";

export interface StudioResourceMetadata {
  name: string;
  description: string;
}

export function StudioNewResourceDialog({ title, onCreate, onCancel, nameMaxLength, descriptionMaxLength }: {
  title: string;
  onCreate(metadata: StudioResourceMetadata): void;
  onCancel(): void;
  nameMaxLength?: number;
  descriptionMaxLength?: number;
}) {
  const nameInput = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  return <StudioDialog open title={title} onClose={onCancel} initialFocusRef={nameInput}
    onSubmit={event => {
      event.preventDefault();
      if (name.trim()) onCreate({ name: name.trim(), description: description.trim() });
    }}
    footer={<StudioDialogFooter onCancel={onCancel} confirmLabel="Create" confirmType="submit" confirmDisabled={!name.trim()} />}>
    <StudioField label="Name"><input ref={nameInput} value={name} onChange={event => setName(event.target.value)} required maxLength={nameMaxLength} /></StudioField>
    <StudioField label="Description"><textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={descriptionMaxLength} /></StudioField>
  </StudioDialog>;
}
