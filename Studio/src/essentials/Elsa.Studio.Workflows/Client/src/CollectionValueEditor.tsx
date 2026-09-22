import { type DragEvent, useState } from "react";
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

export function CollectionValueEditor({
  input,
  elementTypeName,
  value,
  editors,
  context,
  disabled,
  onChange
}: {
  input: StudioActivityInputDescriptor;
  elementTypeName: string | null;
  value: unknown;
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  onChange(value: unknown): void;
}) {
  const items = toLiteralCollection(value);
  const collectionContext: StudioActivityPropertyEditorContext = { ...context, scope: "collection" };
  const collectionEditor = resolveEditor(editors, input, collectionContext)?.component;

  if (collectionEditor) {
    return renderEditor(collectionEditor, input, value, disabled, collectionContext, onChange);
  }

  return (
    <CollectionRepeater
      input={input}
      elementTypeName={elementTypeName}
      items={items}
      editors={editors}
      context={context}
      disabled={disabled}
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
  onChange
}: {
  input: StudioActivityInputDescriptor;
  elementTypeName: string | null;
  items: unknown[];
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  onChange(value: unknown): void;
}) {
  const elementDescriptor = makeCollectionElementDescriptor(input, elementTypeName);
  const elementContext: StudioActivityPropertyEditorContext = { ...context, scope: "element" };
  const ElementComponent = resolveEditor(editors, elementDescriptor, elementContext)?.component;
  const label = input.displayName || input.name;
  const replaceAt = (index: number, next: unknown) =>
    onChange(items.map((current, position) => (position === index ? next : current)));
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
    if (dragIndex !== null && dragIndex !== index) onChange(moveCollectionItem(items, dragIndex, index));
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
              key={index}
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
                {renderEditor(ElementComponent, elementDescriptor, item, disabled, elementContext, next => replaceAt(index, next))}
              </div>
              <div className="wf-collection-item-actions">
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} up`}
                  disabled={disabled || index === 0}
                  onClick={() => onChange(moveCollectionItem(items, index, index - 1))}
                >
                  <ChevronUp size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button"
                  aria-label={`Move ${label} item ${index + 1} down`}
                  disabled={disabled || index === items.length - 1}
                  onClick={() => onChange(moveCollectionItem(items, index, index + 1))}
                >
                  <ChevronDown size={13} />
                </button>
                <button
                  type="button"
                  className="wf-collection-item-button danger"
                  aria-label={`Remove ${label} item ${index + 1}`}
                  disabled={disabled}
                  onClick={() => onChange(items.filter((_, position) => position !== index))}
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
        onClick={() => onChange([...items, defaultCollectionItem(elementTypeName)])}
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

function renderEditor(
  EditorComponent: StudioActivityPropertyEditorContribution["component"] | undefined,
  input: StudioActivityInputDescriptor,
  value: unknown,
  disabled: boolean,
  context: StudioActivityPropertyEditorContext,
  onChange: (value: unknown) => void
) {
  return EditorComponent ? (
    <EditorComponent
      descriptor={input}
      value={value}
      disabled={disabled}
      context={context}
      onChange={onChange}
    />
  ) : (
    <input type="text" value={value == null ? "" : String(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />
  );
}
