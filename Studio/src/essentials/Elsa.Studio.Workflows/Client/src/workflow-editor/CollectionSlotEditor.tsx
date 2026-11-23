import { StudioField } from "@elsa-workflows/studio-ui";
import { useId, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import type { ActivityNode } from "../workflowTypes";
import { getDialogs } from "./dialogs";
import { addCollectionItem, collectionSlotItems, removeCollectionItem, renameCollectionItem, moveCollectionItem, setCollectionCondition, setSwitchMode, type CollectionSlot } from "../collectionSlots";
import "./collectionSlotEditor.css";

export function CollectionSlotEditor({ activity, collection, readOnly, onChange, renderBranch, renderCondition, hasOutgoingConnections }: {
  activity: ActivityNode;
  collection: CollectionSlot;
  readOnly?: boolean;
  onChange(activity: ActivityNode): void;
  renderBranch(index: number): ReactNode;
  renderCondition?(item: Record<string, unknown>): ReactNode;
  hasOutgoingConnections?(index: number): boolean;
}) {
  const currentActivity = useRef<ActivityNode | null>(null);
  useLayoutEffect(() => {
    currentActivity.current = readOnly ? null : activity;
    return () => { currentActivity.current = null; };
  }, [activity, readOnly]);
  const modeId = useId();
  const items = collectionSlotItems(activity, collection);
  if (!items) return null;
  const noun = collection.itemName[0].toUpperCase() + collection.itemName.slice(1);
  const isSwitch = activity.structure?.kind === "elsa.switch.structure" && collection.property === "cases";
  const rawMode = activity.structure?.payload.mode;
  // Match .NET IsNullOrWhiteSpace: trim() also removes BOM and omits NEXT LINE.
  const defaultMode = rawMode == null || (typeof rawMode === "string" && /^\p{White_Space}*$/u.test(rawMode));
  const modeValue = typeof rawMode === "string" ? rawMode : "";
  const mode = defaultMode ? "MatchFirst" : modeValue;
  return (
    <section className="wf-slot-collection-slots" aria-label={collection.displayName}>
      <div className="wf-slot-collection-heading">
        <h4>{collection.displayName}</h4>
        {!readOnly ? <button type="button" onClick={() => onChange(addCollectionItem(activity, collection))}><Plus size={14} /> Add {collection.itemName}</button> : null}
      </div>
      {isSwitch ? <>
        <label htmlFor={modeId}>Match mode</label>
        <select id={modeId} value={mode} disabled={readOnly} onChange={event => {
          if (!readOnly && (event.target.value === "MatchFirst" || event.target.value === "MatchAny"))
            onChange(setSwitchMode(activity, event.target.value));
        }}>
          <option value="MatchFirst">Match first</option>
          <option value="MatchAny">Match any</option>
          {mode !== "MatchFirst" && mode !== "MatchAny" ? <option value={mode}>{typeof rawMode === "string" ? `Unknown mode: ${mode}` : "Unknown mode"}</option> : null}
        </select>
        <p className="wf-muted">{mode === "MatchAny"
          ? "All matching cases run. Their activities can wait independently; the Switch completes after all selected activities finish. Default runs only when no case matches."
          : mode === "MatchFirst" ? "Cases run in order. The first matching case wins; otherwise Default runs."
            : "Choose a supported match mode before publishing."}</p>
      </> : null}
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
          onRemove={async () => {
            const connected = hasOutgoingConnections?.(index) === true;
            const message = connected
              ? `Remove this ${collection.itemName}, its outgoing connections, and any nested activity content?`
              : `Remove this ${collection.itemName} and its activity, including nested content?`;
            if (item[collection.childProperty] || connected) {
              const accepted = await getDialogs().confirm({ title: `Remove ${collection.itemName}?`, message,
                confirmLabel: `Remove ${collection.itemName}`, cancelLabel: "Cancel", tone: "danger" });
              const latest = currentActivity.current;
              if (!accepted || !latest || latest.nodeId !== activity.nodeId) return;
              const latestItem = collectionSlotItems(latest, collection)?.[index];
              if (JSON.stringify(latestItem) !== JSON.stringify(item)) return;
              onChange(removeCollectionItem(latest, collection, index, item[collection.labelProperty] as string));
              return;
            }
            onChange(removeCollectionItem(activity, collection, index, item[collection.labelProperty] as string));
          }}
          removeLabel={`Remove ${collection.itemName} ${index + 1}`}
        >
          {collection.itemName === "case" && !(item[collection.labelProperty] as string).trim()
            ? <p className="wf-muted" role="note">A non-blank case label is needed to expose an outcome port.</p> : null}
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
    <div className="wf-slot-collection-item"><StudioField controlId={id} label={<>{label}</>}>

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
    </StudioField></div>
  );
}
