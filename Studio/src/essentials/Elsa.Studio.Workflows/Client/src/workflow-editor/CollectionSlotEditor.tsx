import { useId, useState, type ReactNode } from "react";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import type { ActivityNode } from "../workflowTypes";
import { addCollectionItem, collectionSlotItems, removeCollectionItem, renameCollectionItem, moveCollectionItem, setCollectionCondition, type CollectionSlot } from "../collectionSlots";
import "./collectionSlotEditor.css";

export function CollectionSlotEditor({ activity, collection, readOnly, onChange, renderBranch, renderCondition }: {
  activity: ActivityNode;
  collection: CollectionSlot;
  readOnly?: boolean;
  onChange(activity: ActivityNode): void;
  renderBranch(index: number): ReactNode;
  renderCondition?(item: Record<string, unknown>): ReactNode;
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
      {collection.conditionProperty ? <p className="wf-muted">Cases run in order. The first true condition wins; otherwise Default runs.</p> : null}
      {!items.length ? <p className="wf-muted">No {collection.displayName.toLowerCase()} yet.</p> : null}
      {items.map((item, index) => (
        <CollectionItemRow
          key={collection.itemIdProperty ? item[collection.itemIdProperty] as string : index}
          label={`${noun} ${index + 1} ${collection.conditionProperty ? "label" : collection.labelProperty === "match" ? "match" : "name"}`}
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
          {collection.conditionProperty && collection.itemIdProperty ? <>
            {item[collection.conditionProperty] != null ? renderCondition?.(item) : <div className="wf-slot-collection-priority">
              <p className="wf-muted">Matches Value against this case label.</p>
              {!readOnly ? <button type="button" onClick={() => onChange(setCollectionCondition(activity, collection, item[collection.itemIdProperty!] as string,
                { value: { expressionType: "Literal", value: false } }))}>Use condition</button> : null}
            </div>}
            {!readOnly ? <div className="wf-slot-collection-priority">
              <button type="button" aria-label={`Move case ${index + 1} up`} disabled={index === 0}
                onClick={() => onChange(moveCollectionItem(activity, collection, item[collection.itemIdProperty!] as string, -1))}><ArrowUp size={14} /> Move up</button>
              <button type="button" aria-label={`Move case ${index + 1} down`} disabled={index === items.length - 1}
                onClick={() => onChange(moveCollectionItem(activity, collection, item[collection.itemIdProperty!] as string, 1))}><ArrowDown size={14} /> Move down</button>
            </div> : null}
          </> : null}
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
      {error ? <p id={`${id}-error`} className="wf-slot-collection-error" role="alert">This value is already used. Each {label.endsWith("match") ? "case match" : label.endsWith("label") ? "case label" : "branch name"} must be unique.</p> : null}
      {children}
    </div>
  );
}
