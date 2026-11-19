import { useEffect, useId, useRef, useState } from "react";
import { Check, Plus, X } from "lucide-react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  createWorkflowTag, getWorkflowTag, listWorkflowTags, workflowTagAssignmentKey,
  type WorkflowTag, type WorkflowTagAssignment
} from "../api/workflowOrganization";

const normalize = (name: string) => name.trim().toUpperCase();
const merge = (current: WorkflowTag[], next: WorkflowTag[]) => [...new Map([...current, ...next].map(tag => [tag.id, tag])).values()];

/** One picker for ordinary labels and controlled values; assignments retain their server identities. */
export function WorkflowTagAssignmentsEditor({ context, assignments, disabled, allowRetired = false, onChange, onBusyChange }: {
  context: StudioEndpointContext; assignments: WorkflowTagAssignment[]; disabled: boolean; allowRetired?: boolean;
  onChange(next: WorkflowTagAssignment[]): void; onBusyChange(busy: boolean): void;
}) {
  const id = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [catalog, setCatalog] = useState<WorkflowTag[]>([]);
  const [results, setResults] = useState<WorkflowTag[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [canCreate, setCanCreate] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [refresh, setRefresh] = useState(0);
  const generationRef = useRef(0);
  const createRef = useRef(false);
  const locked = disabled || creating;
  useEffect(() => { if (!locked) inputRef.current?.focus(); }, [locked]);
  useEffect(() => {
    const timer = window.setTimeout(() => setTerm(search.trim()), 200);
    return () => window.clearTimeout(timer);
  }, [search]);
  useEffect(() => {
    const generation = ++generationRef.current;
    setLoading(true); setError(""); setActive(0);
    void listWorkflowTags(context, term).then(page => {
      if (generation !== generationRef.current) return;
      setCatalog(current => merge(current, page.items)); setResults(page.items);
      setCursor(page.nextContinuationToken ?? null); setCanCreate(page.permissions?.canManageTags === true);
    }).catch(caught => { if (generation === generationRef.current) setError(message(caught)); })
      .finally(() => { if (generation === generationRef.current) setLoading(false); });
    return () => { generationRef.current += 1; };
  }, [context, term, refresh]);
  const missingIds = JSON.stringify([...new Set(assignments.map(assignment => assignment.tagId))].filter(tagId => !catalog.some(tag => tag.id === tagId)));
  useEffect(() => {
    const ids = JSON.parse(missingIds) as string[];
    if (!ids.length) return;
    let current = true;
    void Promise.allSettled(ids.map(tagId => getWorkflowTag(context, tagId))).then(values => {
      if (current) setCatalog(tags => merge(tags, values.flatMap(value => value.status === "fulfilled" ? [value.value] : [])));
    });
    return () => { current = false; };
  }, [context, missingIds]);

  const selected = new Set(assignments.map(workflowTagAssignmentKey));
  const pending = loading || search.trim() !== term;
  const options = pending ? [] : results.flatMap(tag => {
    if (!allowRetired && tag.deprecated) return [];
    return (tag.mode === "marker" ? [{ assignment: { tagId: tag.id } as WorkflowTagAssignment, label: tag.name, retired: tag.deprecated }]
      : tag.values.filter(value => allowRetired || !value.deprecated).map(value => ({ assignment: { tagId: tag.id, valueId: value.id }, label: `${tag.name}: ${value.name}`, retired: tag.deprecated || value.deprecated })))
      .filter(option => !selected.has(workflowTagAssignmentKey(option.assignment)));
  });
  const createName = search.trim();
  const showCreate = !pending && !error && !allowRetired && canCreate && createName.length > 0 && createName.length <= 200
    && !catalog.some(tag => normalize(tag.name) === normalize(createName));
  const optionCount = options.length + (showCreate ? 1 : 0);
  const activeIndex = Math.min(active, optionCount - 1);
  useEffect(() => {
    if (open && activeIndex >= 0) document.getElementById(`${id}-option-${activeIndex}`)?.scrollIntoView({ block: "nearest" });
  }, [activeIndex, id, open]);
  const labelFor = (assignment: WorkflowTagAssignment) => {
    const tag = catalog.find(item => item.id === assignment.tagId);
    const value = tag?.values.find(item => item.id === assignment.valueId);
    const label = tag ? `${tag.name}${assignment.valueId ? `: ${value?.name ?? "Unavailable value"}` : ""}` : `Unavailable tag: ${assignment.tagId}`;
    return `${label}${tag?.deprecated || value?.deprecated ? " (retired)" : ""}`;
  };
  const remove = (assignment: WorkflowTagAssignment) => onChange(assignments.filter(item => workflowTagAssignmentKey(item) !== workflowTagAssignmentKey(assignment)));
  const select = (assignment: WorkflowTagAssignment) => {
    const tag = catalog.find(item => item.id === assignment.tagId);
    onChange([...assignments.filter(item => allowRetired || tag?.cardinality !== "single" || item.tagId !== assignment.tagId), assignment]);
    setSearch(""); setActive(0); setStatus(""); inputRef.current?.focus();
  };
  const create = async () => {
    if (!showCreate || locked || createRef.current) return;
    createRef.current = true; setCreating(true); onBusyChange(true); setError("");
    try {
      const tag = await createWorkflowTag(context, { name: createName, mode: "marker", cardinality: "single", values: [] });
      setCatalog(current => merge(current, [tag]));
      onChange([...assignments, { tagId: tag.id }]); setSearch(""); setActive(0);
      setStatus(`Created ${tag.name}. Save changes to apply it.`);
    } catch (caught) { setError(`Couldn't create this tag. ${message(caught)} Try searching again if it already exists.`); }
    finally { createRef.current = false; setCreating(false); onBusyChange(false); inputRef.current?.focus(); }
  };
  const loadMore = async () => {
    if (!cursor || pending) return;
    const generation = generationRef.current;
    setLoading(true);
    try {
      const page = await listWorkflowTags(context, term, cursor);
      if (generation !== generationRef.current) return;
      setResults(current => merge(current, page.items)); setCatalog(current => merge(current, page.items));
      setCursor(page.nextContinuationToken ?? null);
    } catch (caught) { if (generation === generationRef.current) setError(message(caught)); }
    finally { if (generation === generationRef.current) setLoading(false); }
  };
  return <div className="wf-tag-editor">
    <label id={`${id}-label`} htmlFor={`${id}-input`}>Tags</label>
    <div className="wf-tag-input" data-disabled={locked || undefined}>
      {assignments.map(assignment => <span className="wf-tag-chip" key={workflowTagAssignmentKey(assignment)}>
        <span>{labelFor(assignment)}</span>
        <button type="button" className="wf-tag-chip-remove" aria-label={`Remove ${labelFor(assignment)}`} disabled={locked} onClick={() => { remove(assignment); inputRef.current?.focus(); }}><X size={13} aria-hidden="true" /></button>
      </span>)}
      <input ref={inputRef} id={`${id}-input`} role="combobox" aria-labelledby={`${id}-label`} aria-describedby={`${id}-help`} aria-autocomplete="list"
        aria-expanded={open && !locked} aria-controls={`${id}-options`} aria-activedescendant={open && !locked && activeIndex >= 0 ? `${id}-option-${activeIndex}` : undefined}
        autoComplete="off" placeholder={assignments.length ? "Add another tag…" : "Find or create a tag…"} maxLength={200} value={search} disabled={locked}
        onFocus={() => setOpen(true)} onChange={event => { setSearch(event.target.value); setOpen(true); setActive(0); }} onKeyDown={event => {
          if (event.nativeEvent.isComposing) return;
          if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); setOpen(false); }
          else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault(); setOpen(true);
            setActive(index => optionCount ? (index + (event.key === "ArrowDown" ? 1 : -1) + optionCount) % optionCount : 0);
          } else if (event.key === "Enter") {
            event.preventDefault();
            if (!open) { setOpen(true); return; }
            if (activeIndex >= 0 && options[activeIndex]) select(options[activeIndex].assignment);
            else if (showCreate) void create();
          } else if (event.key === "Backspace" && !search && assignments.length) remove(assignments[assignments.length - 1]);
        }} />
    </div>
    <p className="wf-library-help" id={`${id}-help`}>{allowRetired ? "Choose tags to remove, including retired tags." : canCreate ? "Type to find tags, or create a new label. Save changes to apply your selection." : "Type to find tags and choose a suggestion."}</p>
    {open && !locked ? <div className="wf-tag-suggestions">
      <div role="listbox" id={`${id}-options`} aria-labelledby={`${id}-label`} aria-busy={pending}>
        {options.map((option, index) => <div role="option" aria-selected={index === activeIndex} id={`${id}-option-${index}`} className="wf-tag-option" key={workflowTagAssignmentKey(option.assignment)}
          onMouseDown={event => event.preventDefault()} onMouseEnter={() => setActive(index)} onClick={() => select(option.assignment)}>
          <span>{option.label}{option.retired ? " (retired)" : ""}</span><Plus size={14} aria-hidden="true" />
        </div>)}
        {showCreate ? <div role="option" aria-selected={activeIndex === options.length} id={`${id}-option-${options.length}`} className="wf-tag-option wf-tag-create"
          onMouseDown={event => event.preventDefault()} onMouseEnter={() => setActive(options.length)} onClick={() => void create()}><Plus size={15} aria-hidden="true" /><span>Create “{createName}”</span></div> : null}
      </div>
      {pending ? <p role="status">Finding tags…</p> : !options.length && !showCreate ? <p>{search.trim() ? "No matching tags available" : assignments.length ? <><Check size={14} aria-hidden="true" /> All available tags selected</> : "No tags yet. Type a name to get started."}</p> : null}
      {cursor && !pending ? <StudioButton size="sm" onClick={() => void loadMore()}>Load more tags</StudioButton> : null}
    </div> : null}
    {creating ? <p role="status">Creating tag…</p> : null}
    {status ? <p className="wf-library-help" role="status">{status}</p> : null}
    {error ? <p role="alert">{error} <StudioButton size="sm" disabled={locked} onClick={() => { setRefresh(value => value + 1); setOpen(true); }}>Retry tag search</StudioButton></p> : null}
  </div>;
}

function message(caught: unknown) { return caught instanceof Error ? caught.message : "The server could not complete this request."; }
