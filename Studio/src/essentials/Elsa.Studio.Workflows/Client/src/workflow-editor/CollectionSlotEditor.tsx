import { useId, useState, type ReactNode } from "react";
import { Plus, Trash2 } from "lucide-react";
import type { ActivityNode } from "../workflowTypes";
import { addCollectionItem, collectionSlotItems, removeCollectionItem, renameCollectionItem, type CollectionSlot } from "../collectionSlots";
import "./collectionSlotEditor.css";

export function CollectionSlotEditor({ activity, collection, readOnly, onChange, renderBranch }: {
  activity: ActivityNode;
  collection: CollectionSlot;
  readOnly?: boolean;
  onChange(activity: ActivityNode): void;
  renderBranch(index: number): ReactNode;
}) {
  const items = collectionSlotItems(activity, collection);
  if (!items) return null;
  const noun = collection.itemName[0].toUpperCase() + collection.itemName.slice(1);
  return (
    <section className="wf-slot-collection-slots" aria-label={collection.displayName}>
      <div className="wf-slot-collection-heading">
        <h4>{collection.displayName}</h4>
        {!readOnly ? <button type="button" onClick={() => onChange(addCollectionItem(activity, collection))}><Plus size={14} /> Add {collection.itemName}</button> : null}
      </div>
      {!items.length ? <p className="wf-muted">No {collection.displayName.toLowerCase()} yet.</p> : null}
      {items.map((item, index) => (
        <CollectionItemRow
          key={index}
          label={`${noun} ${index + 1} ${collection.labelProperty === "match" ? "match" : "name"}`}
          item={item}
          value={item[collection.labelProperty] as string}
          readOnly={readOnly}
          duplicate={value => items.some((candidate, position) => position !== index && candidate[collection.labelProperty] === value)}
          onRename={value => onChange(renameCollectionItem(activity, collection, index, item[collection.labelProperty] as string, value))}
          onRemove={() => {
            if (item[collection.childProperty] && !window.confirm(`Remove this ${collection.itemName} and its activity, including nested content?`)) return;
            onChange(removeCollectionItem(activity, collection, index, item[collection.labelProperty] as string));
          }}
          removeLabel={`Remove ${collection.itemName} ${index + 1}`}
        >
          {renderBranch(index)}
        </CollectionItemRow>
      ))}
    </section>
  );
}

function CollectionItemRow({ label, item, value, readOnly, duplicate, onRename, onRemove, removeLabel, children }: {
  label: string;
  item: Record<string, unknown>;
  value: string;
  readOnly?: boolean;
  duplicate(value: string): boolean;
  onRename(value: string): void;
  onRemove(): void;
  removeLabel: string;
  children: ReactNode;
}) {
  const id = useId();
  const signature = JSON.stringify(item);
  const [edit, setEdit] = useState({ signature, text: value });
  // Any external replacement/reorder of this item invalidates its pending local text.
  if (edit.signature !== signature) setEdit({ signature, text: value });
  const text = edit.signature === signature ? edit.text : value;
  const error = duplicate(text);
  const commit = () => { if (!readOnly && !error && text !== value) onRename(text); };
  return (
    <div className="wf-slot-collection-item">
      <label htmlFor={id}>{label}</label>
      <div className="wf-slot-collection-label-row">
        <input id={id} type="text" value={text} readOnly={readOnly} aria-invalid={error || undefined} aria-describedby={error ? `${id}-error` : undefined}
          onChange={event => setEdit({ signature, text: event.target.value })}
          onBlur={commit}
          onKeyDown={event => {
            if (event.key === "Enter") { event.preventDefault(); commit(); }
            if (event.key === "Escape") { event.preventDefault(); setEdit({ signature, text: value }); }
          }} />
        {!readOnly ? <button type="button" className="wf-slot-collection-remove" aria-label={removeLabel} title={removeLabel} onClick={onRemove}><Trash2 size={14} /></button> : null}
      </div>
      {error ? <p id={`${id}-error`} className="wf-slot-collection-error" role="alert">This value is already used. Each {label.endsWith("match") ? "case match" : "branch name"} must be unique.</p> : null}
      {children}
    </div>
  );
}
