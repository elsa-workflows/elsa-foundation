import { forwardRef, useEffect, useId, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, Plus, Trash2, X } from "lucide-react";
import type { StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type {
  ActivityAuthoringCapabilities,
  ActivityContract,
  ActivityContractTypeCapability,
  ActivityInputContract,
  ActivityOutcomeContract,
  ActivityOutputContract
} from "./activityDefinitionTypes";
import { classifyBreakingContractTransition } from "./activityContractComparison";
import {
  activatedStorageDrivers,
  authorableContractTypes,
  createInputContract,
  createOutcomeContract,
  createOutputContract,
  defaultMode,
  findContractType,
  isContractTypeSelectionAuthorable,
  parseLiteralDefault,
  selectTypeForMember,
  serializeDefaultValue
} from "./activityContractAuthoring";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import "./activityContractEditor.css";

type ContractMember = ActivityInputContract | ActivityOutputContract | ActivityOutcomeContract;
type ContractMemberKind = "input" | "output" | "outcome";
type ContractEntry =
  | { kind: "input"; index: number; member: ActivityInputContract; id: string }
  | { kind: "output"; index: number; member: ActivityOutputContract; id: string }
  | { kind: "outcome"; index: number; member: ActivityOutcomeContract; id: string };
type PendingImpact = {
  title: string;
  description: string;
  confirmLabel: string;
  apply(): void;
};

export type ActivityDefinitionContractEditorHandle = {
  revealMember(kind: ContractMemberKind, referenceKey: string): void;
};

type ActivityDefinitionContractEditorProps = {
  contract: ActivityContract;
  baselineContract?: ActivityContract;
  capabilities?: ActivityAuthoringCapabilities;
  expressions: StudioExpressionDescriptor[];
  providerRequiredOutcomes: ActivityOutcomeContract[];
  sourceVersionId?: string | null;
  baselineUnavailable: boolean;
  readOnly: boolean;
  capabilitiesUnavailable: boolean;
  onChange(contract: ActivityContract): void;
  onLocalValidityChange(valid: boolean): void;
};

export const ActivityDefinitionContractEditor = forwardRef<ActivityDefinitionContractEditorHandle, ActivityDefinitionContractEditorProps>(function ActivityDefinitionContractEditor({
  contract,
  baselineContract,
  capabilities,
  expressions,
  providerRequiredOutcomes,
  sourceVersionId,
  baselineUnavailable,
  readOnly,
  capabilitiesUnavailable,
  onChange,
  onLocalValidityChange
}: ActivityDefinitionContractEditorProps, ref) {
  const [newMemberKind, setNewMemberKind] = useState<ContractMemberKind | null>(null);
  const [newName, setNewName] = useState("");
  const [pendingImpact, setPendingImpact] = useState<PendingImpact | null>(null);
  const [invalidDefaults, setInvalidDefaults] = useState<Set<string>>(() => new Set());
  const [pendingDefaults, setPendingDefaults] = useState<Set<string>>(() => new Set());
  const [selectedMemberId, setSelectedMemberId] = useState<string | null>(() => firstContractEntry(contract)?.id ?? null);
  const [selectionGuardMessage, setSelectionGuardMessage] = useState<string | null>(null);
  const addNameInputRef = useRef<HTMLInputElement>(null);
  const originatingAddButtonRef = useRef<HTMLButtonElement | null>(null);
  const focusOriginAddOnClose = useRef(true);
  const focusNewMemberId = useRef<string | null>(null);
  const newMemberNameInputRef = useRef<HTMLInputElement>(null);
  const editorKeys = useRef(new Map<string, string>());
  const nextEditorKey = useRef(0);
  const entries = useMemo(() => contractEntries(contract), [contract]);
  const authorableTypes = useMemo(() => authorableContractTypes(capabilities), [capabilities]);
  const requiredOutcomeKeys = useMemo(() => new Set(providerRequiredOutcomes.map(outcome => outcome.referenceKey)), [providerRequiredOutcomes]);
  const expressionChoices = useMemo(() => expressions.filter(descriptor => descriptor.editingMode !== "literal"), [expressions]);
  const activeEntry = entries.find(entry => entry.id === selectedMemberId) ?? entries[0] ?? null;
  const activeId = activeEntry?.id ?? null;
  const hasDefaultDrafts = invalidDefaults.size > 0 || pendingDefaults.size > 0;
  const canAddValues = !readOnly && !capabilitiesUnavailable && Boolean(capabilities) && authorableTypes.length > 0;
  const canAddOutcomes = !readOnly && !capabilitiesUnavailable && Boolean(capabilities);

  useEffect(() => {
    if (activeId !== selectedMemberId) setSelectedMemberId(activeId);
  }, [activeId, selectedMemberId]);

  useEffect(() => {
    if (newMemberKind) addNameInputRef.current?.focus();
    else if (focusOriginAddOnClose.current) originatingAddButtonRef.current?.focus();
  }, [newMemberKind]);

  useLayoutEffect(() => {
    if (!focusNewMemberId.current || focusNewMemberId.current !== activeId) return;
    newMemberNameInputRef.current?.focus();
    focusNewMemberId.current = null;
  }, [activeId, entries]);

  useEffect(() => {
    onLocalValidityChange(invalidDefaults.size === 0 && pendingDefaults.size === 0);
  }, [invalidDefaults, pendingDefaults, onLocalValidityChange]);

  useEffect(() => () => onLocalValidityChange(true), [onLocalValidityChange]);

  const setDefaultStatus = (memberId: string, valid: boolean, dirty: boolean) => {
    setInvalidDefaults(current => updateSetMembership(current, memberId, !valid));
    setPendingDefaults(current => updateSetMembership(current, memberId, dirty));
  };

  const getEditorKey = (kind: ContractMemberKind, referenceKey: string) => {
    const id = contractMemberId(kind, referenceKey);
    const existing = editorKeys.current.get(id);
    if (existing) return existing;
    const key = `contract-member-${nextEditorKey.current++}`;
    editorKeys.current.set(id, key);
    return key;
  };

  const selectMember = (entry: ContractEntry) => {
    if (entry.id !== activeId && hasDefaultDrafts) {
      const activeHasDraft = invalidDefaults.has(activeId ?? "") || pendingDefaults.has(activeId ?? "");
      const targetHasDraft = invalidDefaults.has(entry.id) || pendingDefaults.has(entry.id);
      if (!activeHasDraft && targetHasDraft) {
        setSelectionGuardMessage(null);
        setSelectedMemberId(entry.id);
        return;
      }
      setSelectionGuardMessage(invalidDefaults.has(activeId ?? "")
        ? "Correct the invalid literal text in this member before selecting another member. The unsaved text is still visible here."
        : pendingDefaults.has(activeId ?? "")
          ? "Apply or correct this default before selecting another member. Its current candidate is still visible here."
          : "Another member has an unapplied default candidate. Resolve it there before selecting a different member.");
      return;
    }
    setSelectionGuardMessage(null);
    setSelectedMemberId(entry.id);
  };

  useImperativeHandle(ref, () => ({
    revealMember(kind, referenceKey) {
      const id = contractMemberId(kind, referenceKey);
      if (!entries.some(entry => entry.id === id)) return;
      setSelectionGuardMessage(null);
      setSelectedMemberId(id);
    }
  }), [entries]);

  const openCreateForm = (kind: ContractMemberKind, addButton: HTMLButtonElement) => {
    if (readOnly || hasDefaultDrafts || (kind === "outcome" ? !canAddOutcomes : !canAddValues)) return;
    originatingAddButtonRef.current = addButton;
    focusOriginAddOnClose.current = true;
    setNewName("");
    setNewMemberKind(kind);
  };

  const cancelCreateForm = () => {
    setNewMemberKind(null);
    setNewName("");
  };

  const addMember = (kind: ContractMemberKind) => {
    const name = newName.trim();
    if (!name || readOnly || hasDefaultDrafts || kind !== newMemberKind) return;
    let member: ContractMember | null = null;
    let nextContract = contract;
    if (kind === "input") {
      if (!canAddValues || !capabilities) return;
      const input = createInputContract(name, contract, capabilities);
      if (!input) return;
      member = input;
      nextContract = { ...contract, inputs: [...contract.inputs, input] };
    } else if (kind === "output") {
      if (!canAddValues || !capabilities) return;
      const output = createOutputContract(name, contract, capabilities);
      if (!output) return;
      member = output;
      nextContract = { ...contract, outputs: [...contract.outputs, output] };
    } else {
      member = createOutcomeContract(name, contract);
      nextContract = { ...contract, outcomes: [...contract.outcomes, member] };
    }
    if (!member) return;
    onChange(nextContract);
    const nextId = contractMemberId(kind, member.referenceKey);
    focusOriginAddOnClose.current = false;
    focusNewMemberId.current = nextId;
    setSelectedMemberId(nextId);
    setSelectionGuardMessage(null);
    setNewMemberKind(null);
    setNewName("");
  };

  const requestRemoval = (kind: ContractMemberKind, member: ContractMember) => {
    const apply = () => {
      if (kind === "input") onChange({ ...contract, inputs: contract.inputs.filter(item => item !== member) });
      if (kind === "output") onChange({ ...contract, outputs: contract.outputs.filter(item => item !== member) });
      if (kind === "outcome") onChange({ ...contract, outcomes: contract.outcomes.filter(item => item !== member) });
      setDefaultStatus(contractMemberId(kind, member.referenceKey), true, false);
      editorKeys.current.delete(contractMemberId(kind, member.referenceKey));
    };
    if (!sourceVersionId || !baselineHasMember(baselineContract, kind, member.referenceKey)) return apply();
    setPendingImpact({
      title: `Remove published-baseline ${kind}`,
      description: `Removing ${member.name} is a breaking contract change because the published baseline identifies it as ${member.referenceKey}.`,
      confirmLabel: `Confirm ${kind} removal`,
      apply
    });
  };

  const requestReferenceKeyChange = (
    kind: ContractMemberKind,
    member: ContractMember,
    requestedKey: string,
    apply: (referenceKey: string) => void
  ) => {
    const nextKey = requestedKey.trim();
    if (!nextKey || nextKey === member.referenceKey) return;
    const kindKeys = kind === "input"
      ? contract.inputs.map(item => item.referenceKey)
      : kind === "output"
        ? contract.outputs.map(item => item.referenceKey)
        : contract.outcomes.map(item => item.referenceKey);
    if (kindKeys.some(key => key === nextKey && key !== member.referenceKey)) return;
    const commit = () => {
      const currentId = contractMemberId(kind, member.referenceKey);
      const nextId = contractMemberId(kind, nextKey);
      editorKeys.current.set(nextId, getEditorKey(kind, member.referenceKey));
      editorKeys.current.delete(currentId);
      setInvalidDefaults(current => transferSetMembership(current, currentId, nextId));
      setPendingDefaults(current => transferSetMembership(current, currentId, nextId));
      setSelectedMemberId(nextId);
      apply(nextKey);
    };
    if (!sourceVersionId || !baselineHasMember(baselineContract, kind, member.referenceKey)) return commit();
    setPendingImpact({
      title: "Change a published reference key",
      description: `Changing ${member.referenceKey} to ${nextKey} removes the baseline binding identity and adds a new one. The backend will classify this as breaking.`,
      confirmLabel: "Change reference key",
      apply: commit
    });
  };

  const requestMemberChange = <T extends ContractMember>(
    kind: ContractMemberKind,
    current: T,
    next: T,
    apply: (next: T) => void
  ) => {
    if (pendingImpact) return;
    const reasons = classifyBreakingContractTransition(kind, current, next);
    if (!sourceVersionId || !baselineHasMember(baselineContract, kind, current.referenceKey) || reasons.length === 0) return apply(next);
    setPendingImpact({
      title: `Review breaking ${kind} change`,
      description: reasons.join(" "),
      confirmLabel: `Apply ${kind} change`,
      apply: () => apply(next)
    });
  };

  const entriesFor = (kind: ContractMemberKind) => entries.filter(entry => entry.kind === kind);
  const renderCreateForm = (kind: ContractMemberKind) => newMemberKind === kind ? <form className="ad-contract-create-form" onSubmit={event => { event.preventDefault(); addMember(kind); }} onKeyDown={event => { if (event.key === "Escape") { event.preventDefault(); cancelCreateForm(); } }}>
    <label><span>{kindLabel(kind)} name</span><input ref={addNameInputRef} value={newName} onChange={event => setNewName(event.target.value)} required disabled={readOnly} /></label>
    <div><button type="submit" disabled={!newName.trim() || readOnly || hasDefaultDrafts || (kind === "outcome" ? !canAddOutcomes : !canAddValues)}>Add {kind}</button><button type="button" onClick={cancelCreateForm}>Cancel</button></div>
  </form> : null;

  return <section className="ad-contract-editor" aria-labelledby="activity-contract-title">
    <header className="ad-contract-header">
      <div><h2 id="activity-contract-title">Public interface</h2><p>Choose an input, output or outcome to edit its details. Changes save automatically.</p></div>
    </header>
    {capabilitiesUnavailable ? <div className="ad-inline-error" role="alert"><strong>Contract options unavailable</strong><span>Studio could not load the available types and storage options. Existing members remain visible, but their type settings cannot be changed safely.</span></div> : null}
    {!capabilitiesUnavailable && capabilities && authorableTypes.length === 0 ? <div className="ad-inline-error" role="alert">No supported type with an active storage option is available, so inputs and outputs cannot be added.</div> : null}
    {sourceVersionId && baselineUnavailable ? <div className="ad-contract-baseline-warning" role="alert"><AlertTriangle size={16} aria-hidden /><span><strong>Published baseline unavailable.</strong> Existing-member changes are conservatively guarded as potentially breaking until the exact immutable baseline can be loaded.</span></div> : null}
    <div className="ad-contract-workbench">
      <nav className="ad-contract-workbench-list" aria-label="Public interface members">
        <ContractMemberGroup
          kind="input"
          entries={entriesFor("input")}
          capabilities={capabilities}
          selectedId={activeId}
          invalidDefaults={invalidDefaults}
          pendingDefaults={pendingDefaults}
          onSelect={selectMember}
          onAdd={button => openCreateForm("input", button)}
          addDisabled={readOnly || !canAddValues || hasDefaultDrafts}
          createForm={renderCreateForm("input")}
        />
        <ContractMemberGroup
          kind="output"
          entries={entriesFor("output")}
          capabilities={capabilities}
          selectedId={activeId}
          invalidDefaults={invalidDefaults}
          pendingDefaults={pendingDefaults}
          onSelect={selectMember}
          onAdd={button => openCreateForm("output", button)}
          addDisabled={readOnly || !canAddValues || hasDefaultDrafts}
          createForm={renderCreateForm("output")}
        />
        <ContractMemberGroup
          kind="outcome"
          entries={entriesFor("outcome")}
          capabilities={capabilities}
          selectedId={activeId}
          invalidDefaults={invalidDefaults}
          pendingDefaults={pendingDefaults}
          onSelect={selectMember}
          onAdd={button => openCreateForm("outcome", button)}
          addDisabled={!canAddOutcomes || hasDefaultDrafts}
          createForm={renderCreateForm("outcome")}
        />
      </nav>
      <div className="ad-contract-workbench-detail" aria-label="Selected contract member">
        {selectionGuardMessage ? <div className="ad-contract-selection-guard" role="alert">{selectionGuardMessage}</div> : null}
        {activeEntry ? <div className="ad-contract-workbench-panes">
          {entries.map(entry => {
            const selected = entry.id === activeId;
            const editorKey = getEditorKey(entry.kind, entry.member.referenceKey);
            if (entry.kind === "input") return <InputEditor
              key={editorKey}
              selected={selected}
              nameInputRef={focusNewMemberId.current === entry.id ? newMemberNameInputRef : undefined}
              member={entry.member}
              index={entry.index}
              capabilities={capabilities}
              authorableTypes={authorableTypes}
              expressionChoices={expressionChoices}
              readOnly={readOnly || capabilitiesUnavailable}
              onStatusChange={(valid, dirty) => setDefaultStatus(entry.id, valid, dirty)}
              onChange={next => requestMemberChange("input", entry.member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))}
              onReferenceKeyChange={key => requestReferenceKeyChange("input", entry.member, key, referenceKey => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
              onRemove={() => requestRemoval("input", entry.member)}
            />;
            if (entry.kind === "output") return <OutputEditor
              key={editorKey}
              selected={selected}
              nameInputRef={focusNewMemberId.current === entry.id ? newMemberNameInputRef : undefined}
              member={entry.member}
              index={entry.index}
              capabilities={capabilities}
              authorableTypes={authorableTypes}
              readOnly={readOnly || capabilitiesUnavailable}
              onChange={next => requestMemberChange("output", entry.member, next, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))}
              onReferenceKeyChange={key => requestReferenceKeyChange("output", entry.member, key, referenceKey => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
              onRemove={() => requestRemoval("output", entry.member)}
            />;
            return <OutcomeEditor
              key={editorKey}
              selected={selected}
              nameInputRef={focusNewMemberId.current === entry.id ? newMemberNameInputRef : undefined}
              member={entry.member}
              index={entry.index}
              providerRequired={requiredOutcomeKeys.has(entry.member.referenceKey)}
              readOnly={readOnly || capabilitiesUnavailable}
              onChange={next => requestMemberChange("outcome", entry.member, next, accepted => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? accepted : item) }))}
              onReferenceKeyChange={key => requestReferenceKeyChange("outcome", entry.member, key, referenceKey => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
              onRemove={() => requestRemoval("outcome", entry.member)}
            />;
          })}
        </div> : <div className="ad-contract-workbench-empty"><strong>No contract member selected</strong><span>Add an input, output, or outcome from the list.</span></div>}
      </div>
    </div>
    <details className="ad-contract-workbench-advanced" data-contract-schema tabIndex={-1}>
      <summary>Advanced interface details</summary>
      <div className="ad-contract-workbench-facts">
        <ReadOnlyFact label="Schema version" value={contract.contractSchemaVersion} />
        {sourceVersionId ? <ReadOnlyFact label="Published baseline version ID" value={sourceVersionId} /> : null}
        {sourceVersionId && baselineContract ? <BaselineClassification contract={contract} baseline={baselineContract} /> : null}
        {sourceVersionId && !baselineContract && !baselineUnavailable ? <p className="ad-contract-workbench-note">Loading the exact immutable contract used for local impact classification…</p> : null}
        {sourceVersionId && baselineUnavailable ? <p className="ad-contract-workbench-note">Impact counts are unavailable until the immutable baseline can be loaded.</p> : null}
      </div>
    </details>
    {pendingImpact ? <ImpactReviewDialog impact={pendingImpact} onClose={() => setPendingImpact(null)} /> : null}
  </section>;
});

function ContractMemberGroup({ kind, entries, capabilities, selectedId, invalidDefaults, pendingDefaults, onSelect, onAdd, addDisabled, createForm }: {
  kind: ContractMemberKind;
  entries: ContractEntry[];
  capabilities?: ActivityAuthoringCapabilities;
  selectedId: string | null;
  invalidDefaults: Set<string>;
  pendingDefaults: Set<string>;
  onSelect(entry: ContractEntry): void;
  onAdd(button: HTMLButtonElement): void;
  addDisabled: boolean;
  createForm: ReactNode;
}) {
  const title = `${kindLabel(kind)}s`;
  const id = `contract-list-${kind}`;
  return <section className="ad-contract-workbench-group" aria-labelledby={id}>
    <header><div><h3 id={id}>{title}</h3><span>{entries.length}</span></div><button type="button" onClick={event => onAdd(event.currentTarget)} disabled={addDisabled} aria-label={`Add ${kind}`}><Plus size={14} aria-hidden /> Add</button></header>
    {createForm}
    {entries.length ? <ul>{entries.map(entry => {
      const selected = entry.id === selectedId;
      const invalid = invalidDefaults.has(entry.id);
      const pending = pendingDefaults.has(entry.id);
      const detail = entry.kind === "outcome"
        ? (entry.member.isEmitted ? "Emitted" : "Not emitted")
        : findContractType(capabilities, entry.member.type.alias)?.displayName || entry.member.type.alias;
      return <li key={entry.id}><button type="button" className={selected ? "is-selected" : undefined} data-member-kind={entry.kind} data-member-reference-key={entry.member.referenceKey} aria-pressed={selected} onClick={() => onSelect(entry)}>
        <span className="ad-contract-workbench-member-name">{entry.member.name || "Unnamed member"}</span><span className="ad-contract-workbench-member-detail">{detail}</span>{invalid ? <span className="ad-contract-workbench-member-invalid">Correction needed</span> : pending ? <span className="ad-contract-workbench-member-invalid">Apply default</span> : null}
      </button></li>;
    })}</ul> : !createForm ? <p className="ad-contract-workbench-group-empty">No {kind === "input" ? "inputs" : kind === "output" ? "outputs" : "outcomes"} yet.</p> : null}
  </section>;
}

function contractMemberId(kind: ContractMemberKind, referenceKey: string) {
  return JSON.stringify([kind, referenceKey]);
}

function updateSetMembership(current: Set<string>, key: string, present: boolean) {
  if (current.has(key) === present) return current;
  const next = new Set(current);
  if (present) next.add(key);
  else next.delete(key);
  return next;
}

function transferSetMembership(current: Set<string>, previousKey: string, nextKey: string) {
  if (!current.has(previousKey)) return current;
  const next = new Set(current);
  next.delete(previousKey);
  next.add(nextKey);
  return next;
}

function contractEntries(contract: ActivityContract): ContractEntry[] {
  return [
    ...contract.inputs.map((member, index): ContractEntry => ({ kind: "input", index, member, id: contractMemberId("input", member.referenceKey) })),
    ...contract.outputs.map((member, index): ContractEntry => ({ kind: "output", index, member, id: contractMemberId("output", member.referenceKey) })),
    ...contract.outcomes.map((member, index): ContractEntry => ({ kind: "outcome", index, member, id: contractMemberId("outcome", member.referenceKey) }))
  ];
}

function firstContractEntry(contract: ActivityContract) {
  return contractEntries(contract)[0] ?? null;
}

function kindLabel(kind: ContractMemberKind) {
  return kind.charAt(0).toLocaleUpperCase("en-US") + kind.slice(1);
}

function BaselineClassification({ contract, baseline }: { contract: ActivityContract; baseline: ActivityContract }) {
  const summary = compareContractPresentation(contract, baseline);
  return <div className="ad-contract-workbench-classification" role="status">
    <ReadOnlyFact label="Behavioral or identity changes" value={String(summary.breaking)} />
    <ReadOnlyFact label="Presentation-only changes" value={String(summary.presentation)} />
    <p>Consumer impact evidence is reviewed separately during publication and dependency review.</p>
  </div>;
}

function InputEditor({ member, index, selected, nameInputRef, capabilities, authorableTypes, expressionChoices, readOnly, onStatusChange, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityInputContract;
  index: number;
  selected: boolean;
  nameInputRef?: { current: HTMLInputElement | null };
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  expressionChoices: StudioExpressionDescriptor[];
  readOnly: boolean;
  onStatusChange(valid: boolean, dirty: boolean): void;
  onChange(member: ActivityInputContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-input-${index}-title`} data-contract-kind="input" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header"><div><span>Input {index + 1}</span><h3 id={`contract-input-${index}-title`}>{member.name || "Unnamed input"}</h3></div><button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove input</button></header>
    {capabilities && !available ? <UnavailableType alias={member.type.alias} /> : null}
    <div className="ad-contract-fields">
      <StagedNameField field="name" value={member.name} readOnly={readOnly} inputRef={nameInputRef} onApply={name => onChange({ ...member, name })} />
      <TypeFields member={member} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <label className="ad-contract-description"><span>Description</span><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></label>
      <label className="ad-contract-check"><input data-contract-field="isRequired" type="checkbox" checked={member.isRequired} onChange={event => onChange({ ...member, isRequired: event.target.checked })} disabled={readOnly} /><span>Required</span><small>A value must be available after the default is applied.</small></label>
      <DefaultEditor member={member} type={type} expressions={expressionChoices} readOnly={readOnly} onStatusChange={onStatusChange} onChange={onChange} />
    </div>
    <AdvancedMemberFields member={member} readOnly={readOnly} onChange={onChange} onReferenceKeyChange={onReferenceKeyChange}>
      <NullabilityField
        member={member}
        type={type}
        readOnly={readOnly}
        preventsTightening={member.default?.syntax === "Literal" && member.default.value === null}
        onChange={onChange}
      />
      <AdvancedTypeFields member={member} type={type} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <DurabilityFields member={member} type={type} capabilities={capabilities} readOnly={readOnly} onChange={onChange} />
    </AdvancedMemberFields>
  </section>;
}

function OutputEditor({ member, index, selected, nameInputRef, capabilities, authorableTypes, readOnly, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityOutputContract;
  index: number;
  selected: boolean;
  nameInputRef?: { current: HTMLInputElement | null };
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  readOnly: boolean;
  onChange(member: ActivityOutputContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-output-${index}-title`} data-contract-kind="output" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header"><div><span>Output {index + 1}</span><h3 id={`contract-output-${index}-title`}>{member.name || "Unnamed output"}</h3></div><button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove output</button></header>
    {capabilities && !available ? <UnavailableType alias={member.type.alias} /> : null}
    <div className="ad-contract-fields">
      <StagedNameField field="name" value={member.name} readOnly={readOnly} inputRef={nameInputRef} onApply={name => onChange({ ...member, name })} />
      <TypeFields member={member} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <label className="ad-contract-description"><span>Description</span><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></label>
      <label className="ad-contract-check"><input data-contract-field="isRequired" type="checkbox" checked={member.isRequired} onChange={event => onChange({ ...member, isRequired: event.target.checked })} disabled={readOnly} /><span>Required</span><small>The activity must produce this output.</small></label>
    </div>
    <AdvancedMemberFields member={member} readOnly={readOnly} onChange={onChange} onReferenceKeyChange={onReferenceKeyChange}>
      <NullabilityField member={member} type={type} readOnly={readOnly} onChange={onChange} />
      <AdvancedTypeFields member={member} type={type} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <DurabilityFields member={member} type={type} capabilities={capabilities} readOnly={readOnly} onChange={onChange} />
    </AdvancedMemberFields>
  </section>;
}

function OutcomeEditor({ member, index, selected, nameInputRef, providerRequired, readOnly, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityOutcomeContract;
  index: number;
  selected: boolean;
  nameInputRef?: { current: HTMLInputElement | null };
  providerRequired: boolean;
  readOnly: boolean;
  onChange(member: ActivityOutcomeContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-outcome-${index}-title`} data-contract-kind="outcome" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header"><div><span>Outcome {index + 1}</span><h3 id={`contract-outcome-${index}-title`}>{member.name || "Unnamed outcome"}</h3></div>{!providerRequired ? <button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove outcome</button> : null}</header>
    {providerRequired ? <div className="ad-inline-status" role="status"><strong>Required by the implementation provider</strong><span>This outcome is supplied by provider capability metadata and cannot be removed or made non-emitted.</span></div> : null}
    <div className="ad-contract-fields">
      <StagedNameField field="name" value={member.name} readOnly={readOnly || providerRequired} inputRef={nameInputRef} onApply={name => onChange({ ...member, name })} />
      <label className="ad-contract-description"><span>Description</span><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></label>
      <label className="ad-contract-check"><input data-contract-field="isEmitted" type="checkbox" checked={member.isEmitted} onChange={event => onChange({ ...member, isEmitted: event.target.checked })} disabled={readOnly || providerRequired} /><span>Emitted by this activity</span></label>
    </div>
    <details className="ad-contract-advanced"><summary>Advanced</summary><div className="ad-contract-fields">
      <ReferenceKeyField value={member.referenceKey} readOnly={readOnly || providerRequired} onApply={onReferenceKeyChange} />
    </div></details>
  </section>;
}

function TypeFields<T extends ActivityInputContract | ActivityOutputContract>({ member, capabilities, authorableTypes, readOnly, onChange }: {
  member: T;
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  readOnly: boolean;
  onChange(member: T): void;
}) {
  return <>
    <label><span>Type</span><select data-contract-field="type.alias" value={member.type.alias} onChange={event => {
      const selected = authorableTypes.find(item => item.alias === event.target.value);
      if (selected && capabilities) onChange(selectTypeForMember(member, selected, capabilities));
    }} disabled={readOnly || authorableTypes.length === 0}>
      {!authorableTypes.some(item => item.alias === member.type.alias) ? <option value={member.type.alias} disabled>{member.type.alias} · unavailable</option> : null}
      {authorableTypes.map(item => <option key={item.alias} value={item.alias} disabled={!item.supportsNull && member.isNullable}>{item.displayName} · {item.category}{!item.supportsNull && member.isNullable ? " · first disable Allow null" : ""}</option>)}
    </select></label>
  </>;
}

function AdvancedTypeFields<T extends ActivityInputContract | ActivityOutputContract>({ member, type, capabilities, authorableTypes, readOnly, onChange }: {
  member: T;
  type?: ActivityContractTypeCapability;
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const currentTypeCanBeReapplied = Boolean(
    type &&
    capabilities &&
    authorableTypes.some(candidate => candidate.alias === type.alias) &&
    !isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities)
  );
  return <>
    {type && type.supportedCollectionKinds.length > 1 ? <label><span>Collection</span><select data-contract-field="type.collectionKind" value={member.type.collectionKind} onChange={event => onChange({ ...member, type: { ...member.type, collectionKind: event.target.value } })} disabled={readOnly}>{type.supportedCollectionKinds.map(kind => <option key={kind}>{kind}</option>)}</select></label> : <ReadOnlyFact label="Collection" value={member.type.collectionKind} field="type.collectionKind" />}
    <ReadOnlyFact label="Type alias" value={member.type.alias} field="type.alias" />
    <small>Type aliases identify available contract types; CLR type names are not stored.</small>
    {currentTypeCanBeReapplied && type && capabilities ? <button type="button" onClick={() => onChange(selectTypeForMember(member, type, capabilities))} disabled={readOnly}>Apply type defaults</button> : null}
  </>;
}

function DurabilityFields<T extends ActivityInputContract | ActivityOutputContract>({ member, type, capabilities, readOnly, onChange }: {
  member: T;
  type?: ActivityContractTypeCapability;
  capabilities?: ActivityAuthoringCapabilities;
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const drivers = activatedStorageDrivers(type, capabilities);
  return <>
    <ReadOnlyFact label="Durability" value="Required" field="durability" />
    {drivers.length > 1 ? <label><span>Durable storage driver</span><select data-contract-field="storageDriverKey" value={member.storageDriverKey} onChange={event => onChange({ ...member, storageDriverKey: event.target.value })} disabled={readOnly}>{drivers.map(driver => <option key={driver}>{driver}</option>)}</select></label> : <ReadOnlyFact label="Durable storage driver" value={member.storageDriverKey || "Unavailable"} field="storageDriverKey" />}
  </>;
}

function NullabilityField<T extends ActivityInputContract | ActivityOutputContract>({ member, type, readOnly, preventsTightening = false, onChange }: {
  member: T;
  type?: ActivityContractTypeCapability;
  readOnly: boolean;
  preventsTightening?: boolean;
  onChange(member: T): void;
}) {
  const nullDefaultRequiresNullability = member.isNullable && preventsTightening;
  const description = !type?.supportsNull
    ? "This type does not allow null."
    : nullDefaultRequiresNullability
      ? "Remove or change the default value of null before turning this off."
      : "A required value may still be null.";
  return <label className="ad-contract-check"><input data-contract-field="isNullable" type="checkbox" checked={member.isNullable} onChange={event => onChange({ ...member, isNullable: event.target.checked })} disabled={readOnly || !type?.supportsNull || nullDefaultRequiresNullability} /><span>Allow null</span><small>{description}</small></label>;
}

function DefaultEditor({ member, type, expressions, readOnly, onStatusChange, onChange }: {
  member: ActivityInputContract;
  type?: ActivityContractTypeCapability;
  expressions: StudioExpressionDescriptor[];
  readOnly: boolean;
  onStatusChange(valid: boolean, dirty: boolean): void;
  onChange(member: ActivityInputContract): void;
}) {
  const [candidate, setCandidate] = useState(member.default);
  const [valid, setValid] = useState(true);
  const statusHandler = useRef(onStatusChange);
  statusHandler.current = onStatusChange;
  const upstreamSignature = JSON.stringify(member.default);
  const previousUpstreamSignature = useRef(upstreamSignature);
  useEffect(() => {
    if (upstreamSignature === previousUpstreamSignature.current) return;
    const previous = previousUpstreamSignature.current;
    setCandidate(current => !valid || JSON.stringify(current) !== previous ? current : member.default);
    previousUpstreamSignature.current = upstreamSignature;
  }, [member.default, upstreamSignature, valid]);
  const mode = defaultMode(candidate);
  const currentExpression = mode === "expression" ? candidate?.syntax : null;
  const expressionAvailable = expressions.length > 0 || Boolean(currentExpression);
  const dirty = JSON.stringify(candidate) !== JSON.stringify(member.default);
  const primitiveEditor = primitiveDefaultEditor(type, member.type.collectionKind);
  const supportsNull = member.isNullable && type?.supportsNull === true;
  const nullMode = mode === "literal" && candidate?.value === null;
  const simpleLiteral = mode === "literal" && candidate && primitiveEditor !== null && primitiveLiteralMatches(primitiveEditor, candidate.value, supportsNull);
  const selectedMode = nullMode ? "null" : mode;
  const literalOptionLabel = primitiveEditor && (mode !== "literal" || simpleLiteral) ? "Value" : "Value (JSON)";

  useEffect(() => {
    statusHandler.current(valid, dirty);
  }, [valid, dirty]);

  const setCandidateValidity = (nextValid: boolean) => {
    setValid(nextValid);
  };
  const updateCandidate = (next: ActivityInputContract["default"]) => {
    setCandidate(next);
    setCandidateValidity(true);
  };
  const literalCandidate = (value: unknown) => ({ ...(candidate?.syntax === "Literal" ? candidate : {}), syntax: "Literal", value });
  const initialLiteral = () => primitiveEditor
    ? primitiveEditor === "text" ? "" : primitiveEditor === "number" ? 0 : false
    : supportsNull ? null : "";

  return <div className="ad-default-editor" data-contract-field-container="default">
    <label><span>Default</span><select data-contract-field="default" aria-label="Default" value={selectedMode} onChange={event => {
      const next = event.target.value;
      if (next === "none") updateCandidate(null);
      if (next === "literal") updateCandidate(literalCandidate(initialLiteral()));
      if (next === "null") updateCandidate(literalCandidate(null));
      if (next === "expression" && expressions[0]) updateCandidate({ syntax: expressions[0].type, value: "" });
    }} disabled={readOnly}>
      <option value="none">No default</option>
      <option value="literal">{literalOptionLabel}</option>
      {supportsNull || nullMode ? <option value="null" disabled={!supportsNull}>{supportsNull ? "Null" : "Null · unavailable"}</option> : null}
      <option value="expression" disabled={!expressionAvailable}>Expression</option>
    </select><small>No default differs from an explicit null value.</small></label>
    {mode === "literal" && candidate ? nullMode
      ? <p className="ad-default-null-value" data-contract-field="default.value">The default is explicitly null.</p>
      : simpleLiteral && primitiveEditor
        ? <PrimitiveDefaultField editor={primitiveEditor} value={candidate.value} readOnly={readOnly} onValidityChange={setCandidateValidity} onChange={value => updateCandidate({ ...candidate, syntax: "Literal", value })} />
      : <>
        {primitiveEditor ? <small className="ad-default-editor-note">The stored value does not match this type’s simple editor, so JSON editing keeps it visible.</small> : null}
        <LiteralDefaultField value={candidate.value} supportsNull={supportsNull} readOnly={readOnly} onValidityChange={setCandidateValidity} onChange={value => updateCandidate({ ...candidate, syntax: "Literal", value })} />
      </>
      : null}
    {mode === "expression" && candidate ? <ExpressionDefaultFields value={candidate} expressions={expressions} readOnly={readOnly} onChange={updateCandidate} /> : null}
    {!expressionAvailable ? <div className="ad-inline-status" role="status">No authorized expression syntax is advertised, so Studio does not invent one.</div> : null}
    <button type="button" onClick={() => onChange({ ...member, default: candidate })} disabled={readOnly || !dirty || !valid}>Apply default</button>
  </div>;
}

type PrimitiveDefaultEditor = "text" | "number" | "boolean";

function primitiveDefaultEditor(type: ActivityContractTypeCapability | undefined, collectionKind: string): PrimitiveDefaultEditor | null {
  if (!type || collectionKind !== "Single") return null;
  const editor = type.defaultEditor.trim().toLocaleLowerCase("en-US");
  if (editor === "text") return "text";
  if (editor === "number") return "number";
  if (["checkbox", "boolean", "bool"].includes(editor)) return "boolean";
  return null;
}

function primitiveLiteralMatches(editor: PrimitiveDefaultEditor, value: unknown, supportsNull: boolean) {
  if (value === null) return supportsNull;
  if (editor === "text") return typeof value === "string";
  if (editor === "number") return typeof value === "number" && Number.isFinite(value);
  return typeof value === "boolean";
}

function PrimitiveDefaultField({ editor, value, readOnly, onValidityChange, onChange }: {
  editor: PrimitiveDefaultEditor;
  value: unknown;
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(value: unknown): void;
}) {
  if (value === null) return <p className="ad-default-null-value" data-contract-field="default.value">The default is explicitly null.</p>;
  if (editor === "text") return <label><span>Value</span><input data-contract-field="default.value" aria-label="Value" type="text" value={String(value)} onChange={event => { onValidityChange(true); onChange(event.target.value); }} disabled={readOnly} /></label>;
  if (editor === "boolean") return <label><span>Value</span><select data-contract-field="default.value" aria-label="Value" value={String(value)} onChange={event => { onValidityChange(true); onChange(event.target.value === "true"); }} disabled={readOnly}><option value="true">True</option><option value="false">False</option></select></label>;
  return <NumberDefaultField value={value as number} readOnly={readOnly} onValidityChange={onValidityChange} onChange={onChange} />;
}

function NumberDefaultField({ value, readOnly, onValidityChange, onChange }: {
  value: number;
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(value: number): void;
}) {
  const errorId = useId();
  const [source, setSource] = useState(() => String(value));
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setSource(String(value)), [value]);
  return <label><span>Value</span><input data-contract-field="default.value" type="text" aria-label="Value" inputMode="decimal" value={source} onChange={event => {
    const next = event.target.value;
    setSource(next);
    const isNumber = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(next) && Number.isFinite(Number(next));
    const nextError = isNumber ? null : "Enter one finite number.";
    setError(nextError);
    onValidityChange(!nextError);
    if (isNumber) onChange(Number(next));
  }} disabled={readOnly} aria-invalid={Boolean(error)} aria-describedby={error ? `${errorId}-error` : undefined} />{error ? <small id={`${errorId}-error`} role="alert">{error} This text is not saved.</small> : null}</label>;
}

function LiteralDefaultField({ value, supportsNull, readOnly, onValidityChange, onChange }: {
  value: unknown;
  supportsNull: boolean;
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(value: unknown): void;
}) {
  const errorId = useId();
  const [source, setSource] = useState(() => serializeDefaultValue(value));
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setSource(serializeDefaultValue(value)), [value]);
  return <label><span>Value (JSON)</span><textarea data-contract-field="default.value" aria-label="Value (JSON)" value={source} onChange={event => {
    const next = event.target.value;
    setSource(next);
    const parsed = parseLiteralDefault(next);
    const nextError = !parsed.ok ? parsed.error ?? "Enter one valid JSON value." : parsed.value === null && !supportsNull ? "The selected capability type does not support null." : null;
    setError(nextError);
    onValidityChange(!nextError);
    if (parsed.ok && !nextError) onChange(parsed.value);
  }} rows={3} disabled={readOnly} aria-invalid={Boolean(error)} aria-describedby={error ? `${errorId}-error` : undefined} />{error ? <small id={`${errorId}-error`} role="alert">{error} The visible text is not saved.</small> : null}</label>;
}

function ExpressionDefaultFields({ value, expressions, readOnly, onChange }: {
  value: NonNullable<ActivityInputContract["default"]>;
  expressions: StudioExpressionDescriptor[];
  readOnly: boolean;
  onChange(value: NonNullable<ActivityInputContract["default"]>): void;
}) {
  const current = value.syntax;
  const known = expressions.some(expression => expression.type === current);
  return <>
    <label><span>Expression syntax</span><select data-contract-field="default.syntax" value={current} onChange={event => onChange({ ...value, syntax: event.target.value })} disabled={readOnly || expressions.length === 0}>
      {!known ? <option value={current} disabled>{current} · unavailable</option> : null}
      {expressions.map(expression => <option key={expression.type} value={expression.type}>{expression.displayName || expression.type}</option>)}
    </select></label>
    <label><span>Expression source</span><textarea data-contract-field="default.value" value={typeof value.value === "string" ? value.value : serializeDefaultValue(value.value)} onChange={event => onChange({ ...value, value: event.target.value })} rows={3} disabled={readOnly || !known} /></label>
    {!known ? <div className="ad-inline-error" role="alert">The stored expression syntax remains visible but is unavailable for mutable authoring. Select an advertised syntax to continue.</div> : null}
  </>;
}

function AdvancedMemberFields<T extends ActivityInputContract | ActivityOutputContract>({ member, readOnly, onChange, onReferenceKeyChange, children }: {
  member: T;
  readOnly: boolean;
  onChange(member: T): void;
  onReferenceKeyChange(key: string): void;
  children?: ReactNode;
}) {
  return <details className="ad-contract-advanced"><summary>Advanced</summary><div className="ad-contract-fields">
    {children}
    <small>Name changes affect the contract. Use Display name for presentation-only text.</small>
    <ReferenceKeyField value={member.referenceKey} readOnly={readOnly} onApply={onReferenceKeyChange} />
    <label><span>Display name</span><input value={member.displayName ?? ""} onChange={event => onChange({ ...member, displayName: event.target.value || null })} disabled={readOnly} /><small>Optional label shown to workflow authors.</small></label>
    <label><span>Category</span><input value={member.category ?? ""} onChange={event => onChange({ ...member, category: event.target.value || null })} disabled={readOnly} /></label>
    <label><span>Order</span><input type="number" value={member.order ?? 0} onChange={event => onChange({ ...member, order: Number(event.target.value) })} disabled={readOnly} /></label>
    <label><span>UI hint</span><input value={member.uiHint ?? ""} onChange={event => onChange({ ...member, uiHint: event.target.value || null })} disabled={readOnly} /></label>
  </div></details>;
}

function ReferenceKeyField({ value, readOnly, onApply }: { value: string; readOnly: boolean; onApply(key: string): void }) {
  const [candidate, setCandidate] = useState(value);
  useEffect(() => setCandidate(value), [value]);
  return <div className="ad-contract-staged-field"><label><span>Stable reference key</span><input data-contract-reference-key-control data-contract-field="referenceKey" value={candidate} onChange={event => setCandidate(event.target.value)} disabled={readOnly} /></label><button type="button" onClick={() => onApply(candidate)} disabled={readOnly || !candidate.trim() || candidate.trim() === value}>Apply key</button><small>Changing a published key is a breaking change.</small></div>;
}

function StagedNameField({ value, field = "name", readOnly, inputRef, onApply }: { value: string; field?: string; readOnly: boolean; inputRef?: { current: HTMLInputElement | null }; onApply(name: string): void }) {
  const [candidate, setCandidate] = useState(value);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setCandidate(value), [value]);
  const applyCandidate = () => {
    const name = candidate.trim();
    if (!name) {
      setError("Name cannot be blank.");
      return;
    }
    setError(null);
    if (name !== value) onApply(name);
    else setCandidate(value);
  };
  return <div className="ad-contract-staged-field">
    <label><span>Name</span><input ref={inputRef} data-contract-field={field} value={candidate} onChange={event => { setCandidate(event.target.value); setError(null); }} onBlur={applyCandidate} onKeyDown={event => {
      if (event.key === "Enter") {
        event.preventDefault();
        applyCandidate();
      }
    }} disabled={readOnly} aria-invalid={Boolean(error)} /></label>
    {candidate.trim() !== value ? <small>Press Enter or leave the field to review this name change. If confirmation is canceled, the candidate stays here so you can retry or revise it.</small> : null}
    {error ? <small role="alert">{error}</small> : null}
  </div>;
}

function ReadOnlyFact({ label, value, field }: { label: string; value: string; field?: string }) {
  return <div className="ad-readonly-fact" data-contract-field={field} tabIndex={field ? -1 : undefined}><span>{label}</span><strong>{value}</strong></div>;
}

function UnavailableType({ alias }: { alias: string }) {
  return <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /><span>Stored type alias <code>{alias}</code> is not an authorable capability selection. This mutable draft is invalid until it is replaced with an advertised type.</span></div>;
}

function ImpactReviewDialog({ impact, onClose }: { impact: PendingImpact; onClose(): void }) {
  const ref = useRef<HTMLElement>(null);
  useDialogFocus(ref, onClose);
  return <div className="ad-dialog-backdrop" role="presentation"><section ref={ref} className="ad-dialog ad-impact-dialog" role="dialog" aria-modal="true" aria-labelledby="contract-impact-title" tabIndex={-1}>
    <header><div><span className="ad-kicker">Draft-local impact review</span><h2 id="contract-impact-title">{impact.title}</h2></div><button type="button" className="ad-icon-button" aria-label="Close" onClick={onClose}><X size={18} /></button></header>
    <p>{impact.description}</p>
    <dl className="ad-impact-facts"><div><dt>Current draft</dt><dd>Will change after exact-revision autosave</dd></div><div><dt>Activity Definitions</dt><dd>None rewritten</dd></div><div><dt>Published versions</dt><dd>None rewritten</dd></div><div><dt>Workflows and occurrences</dt><dd>None rewritten</dd></div></dl>
    <div className="ad-inline-status" role="status">Authoritative member-level usage evidence is reviewed separately when the backend advertises it. This action does not infer or mutate consumers.</div>
    <footer><button type="button" onClick={onClose}>Keep current contract</button><button type="button" className="ad-danger-action" onClick={() => { impact.apply(); onClose(); }}>{impact.confirmLabel}</button></footer>
  </section></div>;
}

function baselineHasMember(baseline: ActivityContract | undefined, kind: "input" | "output" | "outcome", referenceKey: string) {
  if (!baseline) return true;
  const members = kind === "input" ? baseline.inputs : kind === "output" ? baseline.outputs : baseline.outcomes;
  return members.some(member => member.referenceKey === referenceKey);
}

function compareContractPresentation(current: ActivityContract, baseline: ActivityContract) {
  let breaking = 0;
  let presentation = 0;
  for (const [currentMembers, baselineMembers] of [
    [current.inputs, baseline.inputs],
    [current.outputs, baseline.outputs],
    [current.outcomes, baseline.outcomes]
  ] as const) {
    const baselineByKey = new Map(baselineMembers.map(member => [member.referenceKey, member]));
    breaking += baselineMembers.filter(member => !currentMembers.some(candidate => candidate.referenceKey === member.referenceKey)).length;
    for (const member of currentMembers) {
      const previous = baselineByKey.get(member.referenceKey) as ContractMember | undefined;
      if (!previous) continue;
      if (JSON.stringify(behavioralContractFields(member)) !== JSON.stringify(behavioralContractFields(previous))) breaking += 1;
      if (JSON.stringify(presentationContractFields(member)) !== JSON.stringify(presentationContractFields(previous))) presentation += 1;
    }
  }
  return { breaking, presentation };
}

function behavioralContractFields(member: ContractMember) {
  if ("type" in member) {
    return {
      name: member.name,
      type: member.type,
      isRequired: member.isRequired,
      isNullable: member.isNullable,
      storageDriverKey: member.storageDriverKey,
      durability: member.durability,
      ...("default" in member ? { default: member.default } : {})
    };
  }
  return { name: member.name, isEmitted: member.isEmitted };
}

function presentationContractFields(member: ContractMember) {
  return {
    ...("displayName" in member ? { displayName: member.displayName } : {}),
    description: member.description,
    ...("category" in member ? {
      category: member.category,
      order: member.order,
      uiHint: member.uiHint,
      uiSpecifications: member.uiSpecifications
    } : {})
  };
}

export { classifyBreakingContractTransition } from "./activityContractComparison";
