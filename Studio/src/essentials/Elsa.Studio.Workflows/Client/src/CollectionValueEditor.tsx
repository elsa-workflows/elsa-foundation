import { type DragEvent, useRef, useState } from "react";
import { ChevronDown, ChevronUp, GripVertical, Plus, Trash2 } from "lucide-react";
import type {
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext
} from "@elsa-workflows/studio-sdk";
import {
  defaultCollectionItem,
  makeCollectionElementDescriptor,
  moveCollectionItem,
  toLiteralCollection
} from "./activityProperties";
import { ReportedPropertyEditor } from "./ReportedPropertyEditor";

let nextCollectionItemId = 0;

function createCollectionItemId() {
  nextCollectionItemId += 1;
  return `collection-item-${nextCollectionItemId}`;
}

export function CollectionValueEditor({
  input,
  elementTypeName,
  value,
  editors,
  context,
  disabled,
  validationScopeKey = "standalone",
  onValidationChange,
  onDisclosureFallbackChange,
  onChange
}: {
  input: StudioActivityInputDescriptor;
  elementTypeName: string | null;
  value: unknown;
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  validationScopeKey?: string;
  onValidationChange?(instanceKey: string, hasError: boolean): void;
  onDisclosureFallbackChange?(instanceKey: string, shouldForceOpen: boolean): void;
  onChange(value: unknown): void;
}) {
  const items = toLiteralCollection(value);
  const collectionContext: StudioActivityPropertyEditorContext = { ...context, scope: "collection" };
  const collectionEditor = resolveEditor(editors, input, collectionContext);

  if (collectionEditor) {
    return (
      <ReportedPropertyEditor
        key={`${validationScopeKey}\u001fcollection\u001f${collectionEditor.id}`}
        editor={collectionEditor}
        descriptor={input}
        value={value}
        disabled={disabled}
        context={collectionContext}
        onChange={onChange}
        validationInstanceKey="collection"
        onValidationChange={onValidationChange}
        onDisclosureFallbackChange={onDisclosureFallbackChange}
      />
    );
  }

  return (
    <CollectionRepeater
      input={input}
      elementTypeName={elementTypeName}
      items={items}
      editors={editors}
      context={context}
      disabled={disabled}
      validationScopeKey={validationScopeKey}
      onValidationChange={onValidationChange}
      onDisclosureFallbackChange={onDisclosureFallbackChange}
      onChange={onChange}
    />
  );
}

function CollectionRepeater({
  input,
  elementTypeName,
  items,
  editors,
  context,
  disabled,
  validationScopeKey = "standalone",
  onValidationChange,
  onDisclosureFallbackChange,
  onChange
}: {
  input: StudioActivityInputDescriptor;
  elementTypeName: string | null;
  items: unknown[];
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  validationScopeKey?: string;
  onValidationChange?(instanceKey: string, hasError: boolean): void;
  onDisclosureFallbackChange?(instanceKey: string, shouldForceOpen: boolean): void;
  onChange(value: unknown): void;
}) {
  const elementDescriptor = makeCollectionElementDescriptor(input, elementTypeName);
  const elementContext: StudioActivityPropertyEditorContext = { ...context, scope: "element" };
  const elementEditor = resolveEditor(editors, elementDescriptor, elementContext);
  const itemIds = useRef<string[]>([]);
  if (itemIds.current.length !== items.length) itemIds.current = items.map(() => createCollectionItemId());
  const label = input.displayName || input.name;
  const replaceAt = (index: number, next: unknown) =>
    onChange(items.map((current, position) => (position === index ? next : current)));
  const removeAt = (index: number) => {
    itemIds.current = itemIds.current.filter((_, position) => position !== index);
    onChange(items.filter((_, position) => position !== index));
  };
  const addItem = () => {
    itemIds.current = [...itemIds.current, createCollectionItemId()];
    onChange([...items, defaultCollectionItem(elementTypeName)]);
  };
  const moveItem = (from: number, to: number) => {
    itemIds.current = moveCollectionItem(itemIds.current, from, to);
    onChange(moveCollectionItem(items, from, to));
  };
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const resetDrag = () => {
    setDragIndex(null);
    setOverIndex(null);
  };
  const handleDragStart = (index: number) => (event: DragEvent) => {
    setDragIndex(index);
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", String(index));
  };
  const handleDragOver = (index: number) => (event: DragEvent) => {
    if (dragIndex === null) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    if (overIndex !== index) setOverIndex(index);
  };
  const handleDrop = (index: number) => (event: DragEvent) => {
    event.preventDefault();
    if (dragIndex !== null && dragIndex !== index) moveItem(dragIndex, index);
    resetDrag();
  };

  return (
    <div className="wf-collection-editor">
      {items.length === 0 ? (
        <p className="wf-collection-empty">No items yet.</p>
      ) : (
        <ul className="wf-collection-items">
          {items.map((item, index) => (
            <li
              key={itemIds.current[index]}
              className={collectionItemClassName(index, dragIndex, overIndex)}
              onDragOver={handleDragOver(index)}
              onDrop={handleDrop(index)}
            >
              <span
                className="wf-collection-item-handle"
                draggable={!disabled}
                aria-label={`Drag ${label} item ${index + 1} to reorder`}
                title="Drag to reorder"
                onDragStart={handleDragStart(index)}
                onDragEnd={resetDrag}
              >
                <GripVertical size={13} aria-hidden="true" />
              </span>
              <div className="wf-collection-item-editor">
                {elementEditor ? (
                  <ReportedPropertyEditor
                    key={`${validationScopeKey}\u001f${itemIds.current[index]}\u001f${elementEditor.id}`}
                    editor={elementEditor}
                    descriptor={elementDescriptor}
                    value={item}
                    disabled={disabled}
                    context={elementContext}
                    onChange={next => replaceAt(index, next)}
                    validationInstanceKey={`collection:${itemIds.current[index]}`}
                    onValidationChange={onValidationChange}
                    onDisclosureFallbackChange={onDisclosureFallbackChange}
                  />
                ) : (
                  <input type="text" value={item == null ? "" : String(item)} disabled={disabled} onChange={event => replaceAt(index, event.target.value)} />
                )}
              </div>
              <div className="wf-collection-item-actions">
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} up`}
                  disabled={disabled || index === 0}
                  onClick={() => moveItem(index, index - 1)}
                >
                  <ChevronUp size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} down`}
                  disabled={disabled || index === items.length - 1}
                  onClick={() => moveItem(index, index + 1)}
                >
                  <ChevronDown size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button danger"
                  aria-label={`Remove ${label} item ${index + 1}`}
                  disabled={disabled}
                  onClick={() => removeAt(index)}
                >
                  <Trash2 size={13} />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <button
        type="button"
        className="wf-collection-add"
        disabled={disabled}
        onClick={addItem}
      >
        <Plus size={13} /> Add item
      </button>
    </div>
  );
}

function collectionItemClassName(index: number, dragIndex: number | null, overIndex: number | null) {
  return [
    "wf-collection-item",
    dragIndex === index ? "dragging" : "",
    dragIndex !== null && dragIndex !== index && overIndex === index ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}

function resolveEditor(
  editors: StudioActivityPropertyEditorContribution[],
  input: StudioActivityInputDescriptor,
  context: StudioActivityPropertyEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => editor.supports(input, context));
}
