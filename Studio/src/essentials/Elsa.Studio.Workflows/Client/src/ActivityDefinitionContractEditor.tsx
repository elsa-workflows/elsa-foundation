import { Fragment, forwardRef, useEffect, useId, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertTriangle, Check, ChevronDown, ChevronRight, X } from "lucide-react";
import { tabElementIds, useTablistKeyboard, StudioField } from "@elsa-workflows/studio-ui";
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
import { WorkflowPropertyRemoveCell, WorkflowPropertySection } from "./WorkflowPropertiesView";
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
  memberKind?: ContractMemberKind;
  contract: ActivityContract;
  baselineContract?: ActivityContract;
  capabilities?: ActivityAuthoringCapabilities;
  expressions: StudioExpressionDescriptor[];
  providerRequiredOutcomes: ActivityOutcomeContract[];
  sourceVersionId?: string | null;
  baselineUnavailable: boolean;
  readOnly: boolean;
  capabilitiesUnavailable: boolean;
  renderOutputValue?(output: ActivityOutputContract): ReactNode;
  onChange(contract: ActivityContract): void;
  onLocalValidityChange(valid: boolean): void;
};

export const ActivityDefinitionContractEditor = forwardRef<ActivityDefinitionContractEditorHandle, ActivityDefinitionContractEditorProps>(function ActivityDefinitionContractEditor({
  memberKind = "input",
  contract,
  baselineContract,
  capabilities,
  expressions,
  providerRequiredOutcomes,
  sourceVersionId,
  baselineUnavailable,
  readOnly,
  capabilitiesUnavailable,
  renderOutputValue,
  onChange,
  onLocalValidityChange
}: ActivityDefinitionContractEditorProps, ref) {
  const [newMemberKind, setNewMemberKind] = useState<ContractMemberKind | null>(null);
  const [newName, setNewName] = useState("");
  const [pendingImpact, setPendingImpact] = useState<PendingImpact | null>(null);
  const [invalidDefaults, setInvalidDefaults] = useState<Set<string>>(() => new Set());
  const [pendingDefaults, setPendingDefaults] = useState<Set<string>>(() => new Set());
  const [selectedMemberIds, setSelectedMemberIds] = useState<Partial<Record<ContractMemberKind, string>>>({});
  const [selectionGuardMessage, setSelectionGuardMessage] = useState<string | null>(null);
  const addNameInputRef = useRef<HTMLInputElement>(null);
  const collectionElementRef = useRef<HTMLElement>(null);
  const focusOriginAddOnClose = useRef(false);
  const focusNewMemberId = useRef<string | null>(null);
  const newMemberNameInputRef = useRef<HTMLInputElement>(null);
  const editorKeys = useRef(new Map<string, string>());
  const nextEditorKey = useRef(0);
  const entries = useMemo(() => contractEntries(contract), [contract]);
  const categories = [...new Set([...contract.inputs, ...contract.outputs].map(member => member.category?.trim()).filter((category): category is string => Boolean(category)))].sort();
  const authorableTypes = useMemo(() => authorableContractTypes(capabilities), [capabilities]);
  const requiredOutcomeKeys = useMemo(() => new Set(providerRequiredOutcomes.map(outcome => outcome.referenceKey)), [providerRequiredOutcomes]);
  const expressionChoices = useMemo(() => expressions.filter(descriptor => descriptor.editingMode !== "literal"), [expressions]);
  const visibleEntries = entries.filter(entry => entry.kind === memberKind);
  const activeEntry = visibleEntries.find(entry => entry.id === selectedMemberIds[memberKind]) ?? null;
  const activeId = activeEntry?.id ?? null;
  const setSelectedMemberId = (id: string | null, kind: ContractMemberKind = memberKind) => {
    setSelectedMemberIds(current => ({ ...current, [kind]: id ?? undefined }));
  };
  const hasDefaultDrafts = invalidDefaults.size > 0 || pendingDefaults.size > 0;
  const canAddValues = !readOnly && !capabilitiesUnavailable && Boolean(capabilities) && authorableTypes.length > 0;
  const canAddOutcomes = !readOnly && !capabilitiesUnavailable && Boolean(capabilities);

  useEffect(() => {
    setSelectionGuardMessage(null);
  }, [memberKind]);

  useEffect(() => {
    if (newMemberKind) addNameInputRef.current?.focus();
    else if (focusOriginAddOnClose.current) {
      focusOriginAddOnClose.current = false;
      const origin = collectionElementRef.current?.querySelector<HTMLButtonElement>(".wf-properties-add");
      const target = origin?.isConnected ? origin : collectionElementRef.current?.querySelector<HTMLButtonElement>(".wf-properties-add");
      target?.focus();
    }
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
    const draftEntry = entries.find(candidate => selectedMemberIds[candidate.kind] === candidate.id && (invalidDefaults.has(candidate.id) || pendingDefaults.has(candidate.id)));
    if (entry.id !== draftEntry?.id && hasDefaultDrafts) {
      const targetHasDraft = invalidDefaults.has(entry.id) || pendingDefaults.has(entry.id);
      if (!draftEntry && targetHasDraft) {
        setSelectionGuardMessage(null);
        setSelectedMemberId(entry.id, entry.kind);
        return;
      }
      setSelectionGuardMessage(invalidDefaults.has(draftEntry?.id ?? "")
        ? "Correct the invalid literal text in this member before selecting another member. The unsaved text is still visible here."
        : pendingDefaults.has(draftEntry?.id ?? "")
          ? "Apply or correct this default before selecting another member. Its current candidate is still visible here."
          : "Another member has an unapplied default candidate. Resolve it there before selecting a different member.");
      return;
    }
    setSelectionGuardMessage(null);
    setSelectedMemberId(entry.id, entry.kind);
  };

  useImperativeHandle(ref, () => ({
    revealMember(kind, referenceKey) {
      const id = contractMemberId(kind, referenceKey);
      if (!entries.some(entry => entry.id === id)) return;
      setSelectionGuardMessage(null);
      setSelectedMemberIds(current => ({ ...current, [kind]: id }));
    }
  }), [entries]);

  const openCreateForm = (kind: ContractMemberKind) => {
    if (readOnly || hasDefaultDrafts || (kind === "outcome" ? !canAddOutcomes : !canAddValues)) return;
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
    setSelectedMemberId(nextId, kind);
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
      if (selectedMemberIds[kind] === contractMemberId(kind, member.referenceKey)) setSelectedMemberId(null, kind);
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
      setSelectedMemberId(nextId, kind);
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
    <StudioField label={<><span>{kindLabel(kind)} name</span></>}><input ref={addNameInputRef} value={newName} onChange={event => setNewName(event.target.value)} required disabled={readOnly} /></StudioField>
    <div><button type="submit" disabled={!newName.trim() || readOnly || hasDefaultDrafts || (kind === "outcome" ? !canAddOutcomes : !canAddValues)}>Add {kind}</button><button type="button" onClick={cancelCreateForm}>Cancel</button></div>
  </form> : null;

  const title = `${kindLabel(memberKind)}s`;
  const description = memberKind === "input" ? "Define the values this activity receives."
    : memberKind === "output" ? "Define the values this activity returns."
      : "Define the completion paths callers can follow.";
  const headers = memberKind === "input" ? ["Name", "Type", "Collection", "Default", "Storage", "Required"]
    : memberKind === "output" ? ["Name", "Type", "Collection", "Required"]
      : ["Name", "Emitted"];
  const currentEntries = entriesFor(memberKind);
  const createForm = renderCreateForm(memberKind);
  const toggleMember = (entry: ContractEntry) => {
    if (selectedMemberIds[entry.kind] === entry.id) {
      if (invalidDefaults.has(entry.id) || pendingDefaults.has(entry.id)) {
        setSelectionGuardMessage("Apply or correct this default before hiding its editor. The current candidate is still visible here.");
        return;
      }
      setSelectionGuardMessage(null);
      setSelectedMemberId(null, entry.kind);
    } else selectMember(entry);
  };
  return <section ref={collectionElementRef} className={`ad-contract-editor is-${memberKind}${memberKind === "output" ? " ad-output-editor" : ""}`} aria-label={`${title} editor`} data-active-contract-kind={memberKind}>
    {capabilitiesUnavailable ? <div className="ad-inline-error" role="alert"><strong>Contract options unavailable</strong><span>Studio could not load the available types and storage options. Existing members remain visible, but their type settings cannot be changed safely.</span></div> : null}
    {!capabilitiesUnavailable && capabilities && authorableTypes.length === 0 ? <div className="ad-inline-error" role="alert">No supported type with an active storage option is available, so inputs and outputs cannot be added.</div> : null}
    {sourceVersionId && baselineUnavailable ? <div className="ad-contract-baseline-warning" role="alert"><AlertTriangle size={16} aria-hidden /><span><strong>Published baseline unavailable.</strong> Existing-member changes are conservatively guarded as potentially breaking until the exact immutable baseline can be loaded.</span></div> : null}
    {selectionGuardMessage ? <div className="ad-contract-selection-guard" role="alert">{selectionGuardMessage}</div> : null}
    <WorkflowPropertySection
      title={title}
      headingLevel={2}
      description={description}
      addLabel={`Add ${memberKind}`}
      addDisabled={(memberKind === "outcome" ? !canAddOutcomes : !canAddValues) || hasDefaultDrafts}
      emptyLabel={`No ${title.toLowerCase()} defined.`}
      headers={headers}
      isEmpty={currentEntries.length === 0 && !createForm}
      keepMountedChildren
      onAdd={() => openCreateForm(memberKind)}
    >
      {entries.map(entry => {
        const selected = entry.id === selectedMemberIds[entry.kind];
        const focusedNameRef = focusNewMemberId.current === entry.id ? newMemberNameInputRef : undefined;
        const removable = !(entry.kind === "outcome" && requiredOutcomeKeys.has(entry.member.referenceKey));
        const name = entry.member.name || `Unnamed ${entry.kind}`;
        const defaultStatus = invalidDefaults.has(entry.id) ? "Correction needed" : pendingDefaults.has(entry.id) ? "Apply default" : null;
        const detailToggle = <button type="button" className="ad-contract-details-toggle" aria-label={`${selected ? "Hide" : "Show"} ${entry.kind} details for ${name}`} aria-expanded={selected} data-member-kind={entry.kind} data-member-reference-key={entry.member.referenceKey} onClick={() => toggleMember(entry)}>{selected ? <ChevronDown size={14} aria-hidden /> : <ChevronRight size={14} aria-hidden />}</button>;
        const onRemove = () => requestRemoval(entry.kind, entry.member);
        const row = entry.kind === "input" ? <tr key={`${entry.id}-row`} data-contract-row-kind="input" data-contract-kind="input" data-contract-index={entry.index} data-contract-reference-key={entry.member.referenceKey}>
          <td data-label="Name"><div className="ad-contract-name-cell"><StagedNameField compact field="name" value={entry.member.name} readOnly={readOnly || capabilitiesUnavailable} inputRef={focusedNameRef} onApply={value => requestMemberChange("input", entry.member, { ...entry.member, name: value }, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))} />{detailToggle}</div></td>
          <td data-label="Type"><ContractTypeCell member={entry.member} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly || capabilitiesUnavailable} onChange={next => requestMemberChange("input", entry.member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))} /></td>
          <td data-label="Collection"><ContractCollectionCell member={entry.member} type={findContractType(capabilities, entry.member.type.alias)} readOnly={readOnly || capabilitiesUnavailable} onChange={next => requestMemberChange("input", entry.member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))} /></td>
          <td data-label="Default"><button type="button" className="ad-contract-default-summary" aria-label={`${defaultStatus ? `${defaultStatus}: ` : ""}Edit ${name} default`} onClick={() => selected ? undefined : selectMember(entry)}><span>{defaultSummary(entry.member.default)}</span>{defaultStatus ? <span className="ad-contract-default-status">{defaultStatus}</span> : null}</button></td>
          <td data-label="Storage"><StorageCell member={entry.member} type={findContractType(capabilities, entry.member.type.alias)} capabilities={capabilities} readOnly={readOnly || capabilitiesUnavailable} onChange={next => requestMemberChange("input", entry.member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))} /></td>
          <td data-label="Required" className="wf-properties-cell-center"><input data-contract-field="isRequired" type="checkbox" aria-label={`${name} required`} checked={entry.member.isRequired} onChange={event => requestMemberChange("input", entry.member, { ...entry.member, isRequired: event.target.checked }, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))} disabled={readOnly || capabilitiesUnavailable} /></td>
          <WorkflowPropertyRemoveCell label={`Remove input ${name}`} disabled={readOnly} onRemove={onRemove} />
        </tr> : entry.kind === "output" ? <tr key={`${entry.id}-row`} data-contract-row-kind="output" data-contract-kind="output" data-contract-index={entry.index} data-contract-reference-key={entry.member.referenceKey}>
          <td data-label="Name"><div className="ad-contract-name-cell"><StagedNameField compact field="name" value={entry.member.name} readOnly={readOnly || capabilitiesUnavailable} inputRef={focusedNameRef} onApply={value => requestMemberChange("output", entry.member, { ...entry.member, name: value }, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))} />{detailToggle}</div></td>
          <td data-label="Type"><ContractTypeCell member={entry.member} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly || capabilitiesUnavailable} onChange={next => requestMemberChange("output", entry.member, next, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))} /></td>
          <td data-label="Collection"><ContractCollectionCell member={entry.member} type={findContractType(capabilities, entry.member.type.alias)} readOnly={readOnly || capabilitiesUnavailable} onChange={next => requestMemberChange("output", entry.member, next, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))} /></td>
          <td data-label="Required" className="wf-properties-cell-center"><input data-contract-field="isRequired" type="checkbox" aria-label={`${name} required`} checked={entry.member.isRequired} onChange={event => requestMemberChange("output", entry.member, { ...entry.member, isRequired: event.target.checked }, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))} disabled={readOnly || capabilitiesUnavailable} /></td>
          <WorkflowPropertyRemoveCell label={`Remove output ${name}`} disabled={readOnly} onRemove={onRemove} />
        </tr> : <tr key={`${entry.id}-row`} data-contract-row-kind="outcome" data-contract-kind="outcome" data-contract-index={entry.index} data-contract-reference-key={entry.member.referenceKey}>
          <td data-label="Name"><div className="ad-contract-name-cell">{requiredOutcomeKeys.has(entry.member.referenceKey) ? <span data-contract-field="name" tabIndex={-1}>{name}</span> : <StagedNameField compact field="name" value={entry.member.name} readOnly={readOnly || capabilitiesUnavailable} inputRef={focusedNameRef} onApply={value => requestMemberChange("outcome", entry.member, { ...entry.member, name: value }, accepted => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? accepted : item) }))} />}{detailToggle}</div></td>
          <td data-label="Emitted" className="wf-properties-cell-center">{requiredOutcomeKeys.has(entry.member.referenceKey)
            ? <label className="ad-contract-required-outcome" data-contract-field="isEmitted" aria-label={`${name} emitted`} tabIndex={-1}><input type="checkbox" checked={entry.member.isEmitted} disabled /></label>
            : <input data-contract-field="isEmitted" type="checkbox" aria-label={`${name} emitted`} checked={entry.member.isEmitted} onChange={event => requestMemberChange("outcome", entry.member, { ...entry.member, isEmitted: event.target.checked }, accepted => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? accepted : item) }))} disabled={readOnly || capabilitiesUnavailable} />}</td>
          {removable ? <WorkflowPropertyRemoveCell label={`Remove outcome ${name}`} disabled={readOnly} onRemove={onRemove} /> : <td />}
        </tr>;
        const detailPanel = entry.kind === "input" ? <InputEditor
          key={getEditorKey(entry.kind, entry.member.referenceKey)}
          selected={selected && entry.kind === memberKind}
          member={entry.member}
          index={entry.index}
          capabilities={capabilities}
          categories={categories}
          expressionChoices={expressionChoices}
          readOnly={readOnly || capabilitiesUnavailable}
          onStatusChange={(valid, dirty) => setDefaultStatus(entry.id, valid, dirty)}
          onChange={next => requestMemberChange("input", entry.member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? accepted : item) }))}
          onReferenceKeyChange={key => requestReferenceKeyChange("input", entry.member, key, referenceKey => onChange({ ...contract, inputs: contract.inputs.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
        /> : entry.kind === "output" ? <OutputEditor
          key={getEditorKey(entry.kind, entry.member.referenceKey)}
          selected={selected && entry.kind === memberKind}
          member={entry.member}
          valueEditor={renderOutputValue?.(entry.member)}
          index={entry.index}
          capabilities={capabilities}
          categories={categories}
          readOnly={readOnly || capabilitiesUnavailable}
          onChange={next => requestMemberChange("output", entry.member, next, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? accepted : item) }))}
          onReferenceKeyChange={key => requestReferenceKeyChange("output", entry.member, key, referenceKey => onChange({ ...contract, outputs: contract.outputs.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
        /> : <OutcomeEditor
          key={getEditorKey(entry.kind, entry.member.referenceKey)}
          selected={selected && entry.kind === memberKind}
          member={entry.member}
          index={entry.index}
          providerRequired={requiredOutcomeKeys.has(entry.member.referenceKey)}
          readOnly={readOnly || capabilitiesUnavailable}
          onChange={next => requestMemberChange("outcome", entry.member, next, accepted => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? accepted : item) }))}
          onReferenceKeyChange={key => requestReferenceKeyChange("outcome", entry.member, key, referenceKey => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === entry.member ? { ...item, referenceKey } : item) }))}
        />;
        return <Fragment key={getEditorKey(entry.kind, entry.member.referenceKey)}>
          {entry.kind === memberKind ? row : null}
          <tr hidden={entry.kind !== memberKind || !selected} className="ad-contract-detail-row" data-contract-detail-kind={entry.kind}><td className="ad-contract-row-details-cell" colSpan={headers.length + 1}>{detailPanel}</td></tr>
        </Fragment>;
      })}
      {createForm ? <tr><td className="ad-contract-row-details-cell" colSpan={headers.length + 1}>{createForm}</td></tr> : null}
    </WorkflowPropertySection>
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

function kindLabel(kind: ContractMemberKind) {
  return kind.charAt(0).toLocaleUpperCase("en-US") + kind.slice(1);
}

type ValueContractMember = ActivityInputContract | ActivityOutputContract;

function ContractTypeCell<T extends ValueContractMember>({ member, capabilities, authorableTypes, readOnly, onChange }: {
  member: T;
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const known = authorableTypes.some(type => type.alias === member.type.alias);
  return <select aria-label="Type" data-contract-field="type.alias" value={member.type.alias} onChange={event => {
    const type = authorableTypes.find(candidate => candidate.alias === event.target.value);
    if (type && capabilities) onChange(selectTypeForMember(member, type, capabilities));
  }} disabled={readOnly || authorableTypes.length === 0}>
    {!known ? <option value={member.type.alias} disabled>{member.type.alias} · unavailable</option> : null}
    {authorableTypes.map(type => <option key={type.alias} value={type.alias} disabled={!type.supportsNull && member.isNullable}>{type.displayName}{!type.supportsNull && member.isNullable ? " · first disable Allow null" : ""}</option>)}
  </select>;
}

function ContractCollectionCell<T extends ValueContractMember>({ member, type, readOnly, onChange }: {
  member: T;
  type?: ActivityContractTypeCapability;
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const kinds = type?.supportedCollectionKinds ?? [member.type.collectionKind];
  if (kinds.length <= 1) return <span data-contract-field="type.collectionKind" tabIndex={-1}>{member.type.collectionKind}</span>;
  return <select aria-label="Collection" data-contract-field="type.collectionKind" value={member.type.collectionKind} onChange={event => onChange({ ...member, type: { ...member.type, collectionKind: event.target.value } })} disabled={readOnly}>
    {!kinds.includes(member.type.collectionKind) ? <option value={member.type.collectionKind} disabled>{member.type.collectionKind} · unavailable</option> : null}
    {kinds.map(kind => <option key={kind} value={kind}>{kind}</option>)}
  </select>;
}

function StorageCell<T extends ValueContractMember>({ member, type, capabilities, readOnly, onChange }: {
  member: T;
  type?: ActivityContractTypeCapability;
  capabilities?: ActivityAuthoringCapabilities;
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const drivers = activatedStorageDrivers(type, capabilities);
  const available = drivers.includes(member.storageDriverKey);
  return <select aria-label="Durable storage driver" data-contract-field="storageDriverKey" title={drivers.length === 0 ? "No compatible storage driver is active." : drivers.length === 1 ? "Only one compatible storage driver is active." : "Choose where this value is stored durably."} value={member.storageDriverKey} onChange={event => onChange({ ...member, storageDriverKey: event.target.value })} disabled={readOnly || drivers.length === 0}>
    {!available ? <option value={member.storageDriverKey} disabled>{member.storageDriverKey || "No driver"} · unavailable</option> : null}
    {drivers.map(driver => <option key={driver} value={driver}>{driver}</option>)}
  </select>;
}

function defaultSummary(value: ActivityInputContract["default"]) {
  const mode = defaultMode(value);
  if (mode === "none") return "No default";
  if (mode === "expression") return `Expression · ${value?.syntax}`;
  return `Value · ${serializeDefaultValue(value?.value)}`;
}

function BaselineClassification({ contract, baseline }: { contract: ActivityContract; baseline: ActivityContract }) {
  const summary = compareContractPresentation(contract, baseline);
  return <div className="ad-contract-workbench-classification" role="status">
    <ReadOnlyFact label="Behavioral or identity changes" value={String(summary.breaking)} />
    <ReadOnlyFact label="Presentation-only changes" value={String(summary.presentation)} />
    <p>Consumer impact evidence is reviewed separately during publication and dependency review.</p>
  </div>;
}

function InputEditor({ member, index, selected, capabilities, categories, expressionChoices, readOnly, onStatusChange, onChange, onReferenceKeyChange }: {
  member: ActivityInputContract;
  index: number;
  selected: boolean;
  capabilities?: ActivityAuthoringCapabilities;
  categories: string[];
  expressionChoices: StudioExpressionDescriptor[];
  readOnly: boolean;
  onStatusChange(valid: boolean, dirty: boolean): void;
  onChange(member: ActivityInputContract): void;
  onReferenceKeyChange(key: string): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-input-${index}-title`} data-contract-kind="input" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header"><div><span>Input details</span><h3 id={`contract-input-${index}-title`}>{member.name || "Unnamed input"}</h3></div></header>
    {capabilities && !available ? <UnavailableType alias={member.type.alias} /> : null}
    <MemberFieldTabs general={<div className="ad-contract-fields">
      <StudioField className="ad-contract-description" label={<><span>Description</span></>}><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></StudioField>
      <DefaultEditor member={member} type={type} expressions={expressionChoices} readOnly={readOnly} onStatusChange={onStatusChange} onChange={onChange} />
    </div>
    } presentation={<PresentationFields member={member} categories={categories} readOnly={readOnly} onChange={onChange} />} advanced={<div className="ad-contract-fields">
      <NullabilityField
        member={member}
        type={type}
        readOnly={readOnly}
        preventsTightening={member.default?.syntax === "Literal" && member.default.value === null}
        onChange={onChange}
      />
      <ReferenceKeyField value={member.referenceKey} readOnly={readOnly} onApply={onReferenceKeyChange} />
    </div>} />
  </section>;
}

function OutputEditor({ member, valueEditor, index, selected, capabilities, categories, readOnly, onChange, onReferenceKeyChange }: {
  valueEditor?: ReactNode;
  member: ActivityOutputContract;
  index: number;
  selected: boolean;
  capabilities?: ActivityAuthoringCapabilities;
  categories: string[];
  readOnly: boolean;
  onChange(member: ActivityOutputContract): void;
  onReferenceKeyChange(key: string): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-output-${index}-title`} data-contract-kind="output" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header ad-output-member-header"><div><h3 id={`contract-output-${index}-title`}>{member.name || "Unnamed output"} details</h3></div></header>
    {capabilities && !available ? <UnavailableType alias={member.type.alias} /> : null}
    <div className={`ad-output-primary${valueEditor ? " has-value-editor" : ""}`}>
      <div className="ad-contract-fields ad-output-declaration">
        <StudioField className="ad-contract-description" label={<><span>Description</span></>}><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></StudioField>
      </div>
      {valueEditor}
    </div>
    <details className="ad-output-disclosure"><summary>Presentation</summary><PresentationFields member={member} categories={categories} readOnly={readOnly} onChange={onChange} /></details>
    <details className="ad-output-disclosure"><summary>Advanced</summary><div className="ad-contract-fields">
      <NullabilityField member={member} type={type} readOnly={readOnly} onChange={onChange} />
      <ReferenceKeyField value={member.referenceKey} readOnly={readOnly} onApply={onReferenceKeyChange} />
    </div></details>
  </section>;
}

function OutcomeEditor({ member, index, selected, providerRequired, readOnly, onChange, onReferenceKeyChange }: {
  member: ActivityOutcomeContract;
  index: number;
  selected: boolean;
  providerRequired: boolean;
  readOnly: boolean;
  onChange(member: ActivityOutcomeContract): void;
  onReferenceKeyChange(key: string): void;
}) {
  return <section className="ad-contract-workbench-member" hidden={!selected} data-contract-selected={selected ? "true" : undefined} aria-labelledby={`contract-outcome-${index}-title`} data-contract-kind="outcome" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}>
    <header className="ad-contract-workbench-member-header"><div><span>Outcome details</span><h3 id={`contract-outcome-${index}-title`}>{member.name || "Unnamed outcome"}</h3></div></header>
    <div className="ad-contract-fields">
      <StudioField className="ad-contract-description" label={<><span>Description</span></>}><textarea data-contract-field="description" value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} rows={2} /></StudioField>
    </div>
    <details className="ad-contract-advanced"><summary>Advanced</summary><div className="ad-contract-fields">
      <ReferenceKeyField value={member.referenceKey} readOnly={readOnly || providerRequired} onApply={onReferenceKeyChange} />
    </div></details>
  </section>;
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
    <StudioField label={<><span>Default</span></>}><select data-contract-field="default" aria-label="Default" value={selectedMode} onChange={event => {
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
    </select><small>No default differs from an explicit null value.</small></StudioField>
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
  if (editor === "text") return <StudioField label={<><span>Value</span></>}><input data-contract-field="default.value" aria-label="Value" type="text" value={String(value)} onChange={event => { onValidityChange(true); onChange(event.target.value); }} disabled={readOnly} /></StudioField>;
  if (editor === "boolean") return <StudioField label={<><span>Value</span></>}><select data-contract-field="default.value" aria-label="Value" value={String(value)} onChange={event => { onValidityChange(true); onChange(event.target.value === "true"); }} disabled={readOnly}><option value="true">True</option><option value="false">False</option></select></StudioField>;
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
  return <StudioField label={<><span>Value</span></>}><input data-contract-field="default.value" type="text" aria-label="Value" inputMode="decimal" value={source} onChange={event => {
    const next = event.target.value;
    setSource(next);
    const isNumber = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(next) && Number.isFinite(Number(next));
    const nextError = isNumber ? null : "Enter one finite number.";
    setError(nextError);
    onValidityChange(!nextError);
    if (isNumber) onChange(Number(next));
  }} disabled={readOnly} aria-invalid={Boolean(error)} aria-describedby={error ? `${errorId}-error` : undefined} />{error ? <small id={`${errorId}-error`} role="alert">{error} This text is not saved.</small> : null}</StudioField>;
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
  return <StudioField label={<><span>Value (JSON)</span></>}><textarea data-contract-field="default.value" aria-label="Value (JSON)" value={source} onChange={event => {
    const next = event.target.value;
    setSource(next);
    const parsed = parseLiteralDefault(next);
    const nextError = !parsed.ok ? parsed.error ?? "Enter one valid JSON value." : parsed.value === null && !supportsNull ? "The selected capability type does not support null." : null;
    setError(nextError);
    onValidityChange(!nextError);
    if (parsed.ok && !nextError) onChange(parsed.value);
  }} rows={3} disabled={readOnly} aria-invalid={Boolean(error)} aria-describedby={error ? `${errorId}-error` : undefined} />{error ? <small id={`${errorId}-error`} role="alert">{error} The visible text is not saved.</small> : null}</StudioField>;
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
    <StudioField label={<><span>Expression syntax</span></>}><select data-contract-field="default.syntax" value={current} onChange={event => onChange({ ...value, syntax: event.target.value })} disabled={readOnly || expressions.length === 0}>
      {!known ? <option value={current} disabled>{current} · unavailable</option> : null}
      {expressions.map(expression => <option key={expression.type} value={expression.type}>{expression.displayName || expression.type}</option>)}
    </select></StudioField>
    <StudioField label={<><span>Expression source</span></>}><textarea data-contract-field="default.value" value={typeof value.value === "string" ? value.value : serializeDefaultValue(value.value)} onChange={event => onChange({ ...value, value: event.target.value })} rows={3} disabled={readOnly || !known} /></StudioField>
    {!known ? <div className="ad-inline-error" role="alert">The stored expression syntax remains visible but is unavailable for mutable authoring. Select an advertised syntax to continue.</div> : null}
  </>;
}

const memberTabs = ["General", "Presentation", "Advanced"];

function MemberFieldTabs({ general, presentation, advanced }: { general: ReactNode; presentation: ReactNode; advanced: ReactNode }) {
  const [active, setActive] = useState("General");
  const id = useId();
  const onKeyDown = useTablistKeyboard(memberTabs, active, setActive);
  const panels = [general, presentation, advanced];
  return <div className="ad-member-settings">
    <div className="ad-tabs ad-member-tabs" role="tablist" aria-label="Member settings" onKeyDown={onKeyDown}>
      {memberTabs.map((tab, index) => <button key={tab} type="button" role="tab" id={tabElementIds(id, index).tabId} data-tab-id={tab} aria-controls={tabElementIds(id, index).panelId} aria-selected={active === tab} tabIndex={active === tab ? 0 : -1} onClick={() => setActive(tab)}>{tab}</button>)}
    </div>
    {panels.map((panel, index) => <div key={memberTabs[index]} role="tabpanel" id={tabElementIds(id, index).panelId} aria-labelledby={tabElementIds(id, index).tabId} hidden={active !== memberTabs[index]}>{panel}</div>)}
  </div>;
}

function PresentationFields<T extends ActivityInputContract | ActivityOutputContract>({ member, categories, readOnly, onChange }: {
  member: T;
  categories: string[];
  readOnly: boolean;
  onChange(member: T): void;
}) {
  const categoryListId = useId();
  return <div className="ad-contract-fields">
    <StudioField label={<><span>Display name</span></>}><input data-contract-field="displayName" value={member.displayName ?? ""} onChange={event => onChange({ ...member, displayName: event.target.value || null })} disabled={readOnly} /><small>Optional label shown to workflow authors.</small></StudioField>
    <StudioField label={<><span>Category</span><datalist id={categoryListId}>{categories.map(category => <option key={category} value={category} />)}</datalist></>}><input data-contract-field="category" list={categoryListId} value={member.category ?? ""} onChange={event => onChange({ ...member, category: event.target.value || null })} disabled={readOnly} /><small>Choose an existing category or enter a new one.</small></StudioField>
    <StudioField label={<><span>Order</span></>}><input data-contract-field="order" type="number" value={member.order ?? 0} onChange={event => onChange({ ...member, order: Number(event.target.value) })} disabled={readOnly} /></StudioField>
    <StudioField label={<><span>UI hint</span></>}><input data-contract-field="uiHint" value={member.uiHint ?? ""} onChange={event => onChange({ ...member, uiHint: event.target.value || null })} disabled={readOnly} /></StudioField>
  </div>;
}

function ReferenceKeyField({ value, readOnly, onApply }: { value: string; readOnly: boolean; onApply(key: string): void }) {
  const [candidate, setCandidate] = useState(value);
  const helpId = useId();
  useEffect(() => setCandidate(value), [value]);
  return <div className="ad-contract-staged-field ad-contract-reference-field">
    <StudioField label={<><span>Stable reference key</span></>}><input data-contract-reference-key-control data-contract-field="referenceKey" value={candidate} aria-describedby={helpId} onChange={event => setCandidate(event.target.value)} disabled={readOnly} /></StudioField>
    <button type="button" aria-label="Apply key" title="Apply key" onClick={() => onApply(candidate)} disabled={readOnly || !candidate.trim() || candidate.trim() === value}><Check size={16} aria-hidden /></button>
    <small id={helpId}>Used in saved bindings. Changing a published key is a breaking change.</small>
  </div>;
}

function StagedNameField({ value, field = "name", compact, readOnly, inputRef, onApply }: { value: string; field?: string; compact?: boolean; readOnly: boolean; inputRef?: { current: HTMLInputElement | null }; onApply(name: string): void }) {
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
  return <div className={`ad-contract-staged-field${compact ? " is-compact" : ""}`}>
    {compact ? <input ref={inputRef} aria-label="Name" data-contract-field={field} value={candidate} onChange={event => { setCandidate(event.target.value); setError(null); }} onBlur={applyCandidate} onKeyDown={event => {
      if (event.key === "Enter") {
        event.preventDefault();
        applyCandidate();
      }
    }} disabled={readOnly} aria-invalid={Boolean(error)} /> : <StudioField label={<><span>Name</span></>}><input ref={inputRef} data-contract-field={field} value={candidate} onChange={event => { setCandidate(event.target.value); setError(null); }} onBlur={applyCandidate} onKeyDown={event => {
      if (event.key === "Enter") {
        event.preventDefault();
        applyCandidate();
      }
    }} disabled={readOnly} aria-invalid={Boolean(error)} /></StudioField>}
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
