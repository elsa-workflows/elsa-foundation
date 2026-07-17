import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
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

type ContractMember = ActivityInputContract | ActivityOutputContract | ActivityOutcomeContract;
type PendingImpact = {
  title: string;
  description: string;
  confirmLabel: string;
  apply(): void;
};

export function ActivityDefinitionContractEditor({
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
}: {
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
}) {
  const [newName, setNewName] = useState("");
  const [pendingImpact, setPendingImpact] = useState<PendingImpact | null>(null);
  const [invalidDefaults, setInvalidDefaults] = useState<Set<string>>(() => new Set());
  const authorableTypes = useMemo(() => authorableContractTypes(capabilities), [capabilities]);
  const requiredOutcomeKeys = useMemo(() => new Set(providerRequiredOutcomes.map(outcome => outcome.referenceKey)), [providerRequiredOutcomes]);
  const expressionChoices = useMemo(() => expressions.filter(descriptor => descriptor.editingMode !== "literal"), [expressions]);

  useEffect(() => {
    onLocalValidityChange(invalidDefaults.size === 0);
  }, [invalidDefaults, onLocalValidityChange]);

  useEffect(() => () => onLocalValidityChange(true), [onLocalValidityChange]);

  const setDefaultValidity = (referenceKey: string, valid: boolean) => setInvalidDefaults(current => {
    const next = new Set(current);
    if (valid) next.delete(referenceKey);
    else next.add(referenceKey);
    return next;
  });

  const addMember = (kind: "input" | "output" | "outcome") => {
    const name = newName.trim();
    if (!name || readOnly || !capabilities) return;
    if (kind === "input") {
      const input = createInputContract(name, contract, capabilities);
      if (input) onChange({ ...contract, inputs: [...contract.inputs, input] });
    } else if (kind === "output") {
      const output = createOutputContract(name, contract, capabilities);
      if (output) onChange({ ...contract, outputs: [...contract.outputs, output] });
    } else {
      onChange({ ...contract, outcomes: [...contract.outcomes, createOutcomeContract(name, contract)] });
    }
    setNewName("");
  };

  const requestRemoval = (kind: "input" | "output" | "outcome", member: ContractMember) => {
    const apply = () => {
      if (kind === "input") onChange({ ...contract, inputs: contract.inputs.filter(item => item !== member) });
      if (kind === "output") onChange({ ...contract, outputs: contract.outputs.filter(item => item !== member) });
      if (kind === "outcome") onChange({ ...contract, outcomes: contract.outcomes.filter(item => item !== member) });
      setDefaultValidity(member.referenceKey, true);
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
    kind: "input" | "output" | "outcome",
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
      setDefaultValidity(member.referenceKey, true);
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
    kind: "input" | "output" | "outcome",
    current: T,
    next: T,
    apply: (next: T) => void
  ) => {
    const reasons = classifyBreakingContractTransition(kind, current, next);
    if (!sourceVersionId || !baselineHasMember(baselineContract, kind, current.referenceKey) || reasons.length === 0) return apply(next);
    setPendingImpact({
      title: `Review breaking ${kind} change`,
      description: reasons.join(" "),
      confirmLabel: `Apply ${kind} change`,
      apply: () => apply(next)
    });
  };

  return <section className="ad-contract-editor" aria-labelledby="activity-contract-title">
    <header className="ad-contract-header">
      <div><span className="ad-kicker">Provider-neutral public surface</span><h2 id="activity-contract-title">Contract</h2><p>Inputs, outputs, and outcomes are saved through the same exact-revision draft queue as the implementation.</p></div>
      <span className="ad-badge" data-contract-schema tabIndex={-1}>Schema {contract.contractSchemaVersion}</span>
    </header>
    {capabilitiesUnavailable ? <div className="ad-inline-error" role="alert"><strong>Contract authoring unavailable</strong><span>The authorized capability catalog could not be confirmed. Stored contract members remain visible, but Studio will not offer raw aliases, CLR types, or guessed storage drivers.</span></div> : null}
    {!capabilitiesUnavailable && capabilities && authorableTypes.length === 0 ? <div className="ad-inline-error" role="alert">No capability-catalog type has an activated durable storage driver. Input and output creation is unavailable.</div> : null}
    {sourceVersionId ? <BaselineSummary sourceVersionId={sourceVersionId} contract={contract} baseline={baselineContract} unavailable={baselineUnavailable} /> : <div className="ad-inline-status" role="status">This draft has no published baseline. New member identities become stable when the first version is published.</div>}
    <div className="ad-contract-add" role="group" aria-label="Add public contract member">
      <label><span>Member name</span><input value={newName} onChange={event => setNewName(event.target.value)} disabled={readOnly || capabilitiesUnavailable} /></label>
      <button type="button" onClick={() => addMember("input")} disabled={!newName.trim() || readOnly || !capabilities || authorableTypes.length === 0}><Plus size={15} aria-hidden /> Add input</button>
      <button type="button" onClick={() => addMember("output")} disabled={!newName.trim() || readOnly || !capabilities || authorableTypes.length === 0}><Plus size={15} aria-hidden /> Add output</button>
      <button type="button" onClick={() => addMember("outcome")} disabled={!newName.trim() || readOnly || !capabilities}><Plus size={15} aria-hidden /> Add outcome</button>
    </div>
    <ContractGroup title="Inputs" empty="No public inputs are defined.">
      {contract.inputs.map((member, index) => <InputEditor
        key={member.referenceKey}
        member={member}
        index={index}
        capabilities={capabilities}
        authorableTypes={authorableTypes}
        expressionChoices={expressionChoices}
        readOnly={readOnly || capabilitiesUnavailable}
        onValidityChange={valid => setDefaultValidity(member.referenceKey, valid)}
        onChange={next => requestMemberChange("input", member, next, accepted => onChange({ ...contract, inputs: contract.inputs.map(item => item === member ? accepted : item) }))}
        onReferenceKeyChange={key => requestReferenceKeyChange("input", member, key, referenceKey => onChange({ ...contract, inputs: contract.inputs.map(item => item === member ? { ...item, referenceKey } : item) }))}
        onRemove={() => requestRemoval("input", member)}
      />)}
    </ContractGroup>
    <ContractGroup title="Outputs" empty="No public outputs are defined.">
      {contract.outputs.map((member, index) => <OutputEditor
        key={member.referenceKey}
        member={member}
        index={index}
        capabilities={capabilities}
        authorableTypes={authorableTypes}
        readOnly={readOnly || capabilitiesUnavailable}
        onChange={next => requestMemberChange("output", member, next, accepted => onChange({ ...contract, outputs: contract.outputs.map(item => item === member ? accepted : item) }))}
        onReferenceKeyChange={key => requestReferenceKeyChange("output", member, key, referenceKey => onChange({ ...contract, outputs: contract.outputs.map(item => item === member ? { ...item, referenceKey } : item) }))}
        onRemove={() => requestRemoval("output", member)}
      />)}
    </ContractGroup>
    <ContractGroup title="Outcomes" empty="No public outcomes are defined.">
      {contract.outcomes.map((member, index) => <OutcomeEditor
        key={member.referenceKey}
        member={member}
        index={index}
        providerRequired={requiredOutcomeKeys.has(member.referenceKey)}
        readOnly={readOnly || capabilitiesUnavailable}
        onChange={next => requestMemberChange("outcome", member, next, accepted => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === member ? accepted : item) }))}
        onReferenceKeyChange={key => requestReferenceKeyChange("outcome", member, key, referenceKey => onChange({ ...contract, outcomes: contract.outcomes.map(item => item === member ? { ...item, referenceKey } : item) }))}
        onRemove={() => requestRemoval("outcome", member)}
      />)}
    </ContractGroup>
    {pendingImpact ? <ImpactReviewDialog impact={pendingImpact} onClose={() => setPendingImpact(null)} /> : null}
  </section>;
}

function ContractGroup({ title, empty, children }: { title: string; empty: string; children: ReactNode }) {
  const items = Array.isArray(children) ? children : [children];
  return <section className="ad-contract-group" aria-labelledby={`contract-${title.toLocaleLowerCase("en-US")}`}><header><h3 id={`contract-${title.toLocaleLowerCase("en-US")}`}>{title}</h3><span>{items.length}</span></header>{items.length ? <div className="ad-contract-members">{children}</div> : <p className="ad-contract-empty">{empty}</p>}</section>;
}

function InputEditor({ member, index, capabilities, authorableTypes, expressionChoices, readOnly, onValidityChange, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityInputContract;
  index: number;
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  expressionChoices: StudioExpressionDescriptor[];
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(member: ActivityInputContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <fieldset className="ad-contract-member" data-contract-kind="input" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}><legend>Input {index + 1}: {member.name}</legend>
    {!available ? <UnavailableType alias={member.type.alias} /> : null}
    <div className="ad-contract-fields">
      <StagedNameField value={member.name} readOnly={readOnly} onApply={name => onChange({ ...member, name })} />
      <TypeFields member={member} type={type} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <label className="ad-contract-check"><input data-contract-field="isRequired" type="checkbox" checked={member.isRequired} onChange={event => onChange({ ...member, isRequired: event.target.checked })} disabled={readOnly} /><span>Required effective value</span><small>After applying a default, an effective value must exist.</small></label>
      <NullabilityField
        member={member}
        type={type}
        readOnly={readOnly}
        preventsTightening={member.default?.syntax === "Literal" && member.default.value === null}
        onChange={onChange}
      />
      <DefaultEditor member={member} type={type} expressions={expressionChoices} readOnly={readOnly} onValidityChange={onValidityChange} onChange={onChange} />
      <DurabilityFields member={member} type={type} capabilities={capabilities} readOnly={readOnly} onChange={onChange} />
    </div>
    <AdvancedMemberFields member={member} readOnly={readOnly} onChange={onChange} onReferenceKeyChange={onReferenceKeyChange} />
    <button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove input</button>
  </fieldset>;
}

function OutputEditor({ member, index, capabilities, authorableTypes, readOnly, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityOutputContract;
  index: number;
  capabilities?: ActivityAuthoringCapabilities;
  authorableTypes: ActivityContractTypeCapability[];
  readOnly: boolean;
  onChange(member: ActivityOutputContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  const type = findContractType(capabilities, member.type.alias);
  const available = isContractTypeSelectionAuthorable(type, member.type.collectionKind, member.storageDriverKey, capabilities);
  return <fieldset className="ad-contract-member" data-contract-kind="output" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}><legend>Output {index + 1}: {member.name}</legend>
    {!available ? <UnavailableType alias={member.type.alias} /> : null}
    <div className="ad-contract-fields">
      <StagedNameField value={member.name} readOnly={readOnly} onApply={name => onChange({ ...member, name })} />
      <TypeFields member={member} type={type} capabilities={capabilities} authorableTypes={authorableTypes} readOnly={readOnly} onChange={onChange} />
      <label className="ad-contract-check"><input data-contract-field="isRequired" type="checkbox" checked={member.isRequired} onChange={event => onChange({ ...member, isRequired: event.target.checked })} disabled={readOnly} /><span>Must be produced</span><small>The implementation is obligated to produce this output.</small></label>
      <NullabilityField member={member} type={type} readOnly={readOnly} onChange={onChange} />
      <DurabilityFields member={member} type={type} capabilities={capabilities} readOnly={readOnly} onChange={onChange} />
    </div>
    <AdvancedMemberFields member={member} readOnly={readOnly} onChange={onChange} onReferenceKeyChange={onReferenceKeyChange} />
    <button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove output</button>
  </fieldset>;
}

function OutcomeEditor({ member, index, providerRequired, readOnly, onChange, onReferenceKeyChange, onRemove }: {
  member: ActivityOutcomeContract;
  index: number;
  providerRequired: boolean;
  readOnly: boolean;
  onChange(member: ActivityOutcomeContract): void;
  onReferenceKeyChange(key: string): void;
  onRemove(): void;
}) {
  return <fieldset className="ad-contract-member" data-contract-kind="outcome" data-contract-index={index} data-contract-reference-key={member.referenceKey} tabIndex={-1}><legend>Outcome {index + 1}: {member.name}</legend>
    {providerRequired ? <div className="ad-inline-status" role="status"><strong>Required by the implementation provider</strong><span>This outcome is supplied by provider capability metadata and cannot be removed or made non-emitted.</span></div> : null}
    <div className="ad-contract-fields">
      <StagedNameField value={member.name} readOnly={readOnly || providerRequired} onApply={name => onChange({ ...member, name })} />
      <label className="ad-contract-check"><input data-contract-field="isEmitted" type="checkbox" checked={member.isEmitted} onChange={event => onChange({ ...member, isEmitted: event.target.checked })} disabled={readOnly || providerRequired} /><span>Emitted by this activity</span></label>
    </div>
    <details className="ad-contract-advanced"><summary>Advanced identity and presentation</summary><div className="ad-contract-fields">
      <ReferenceKeyField value={member.referenceKey} readOnly={readOnly || providerRequired} onApply={onReferenceKeyChange} />
      <label><span>Description</span><textarea value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} /></label>
    </div></details>
    {!providerRequired ? <button type="button" className="ad-danger-action" onClick={onRemove} disabled={readOnly}><Trash2 size={15} aria-hidden /> Remove outcome</button> : null}
  </fieldset>;
}

function TypeFields<T extends ActivityInputContract | ActivityOutputContract>({ member, type, capabilities, authorableTypes, readOnly, onChange }: {
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
    <label><span>Capability type</span><select data-contract-field="type.alias" value={member.type.alias} onChange={event => {
      const selected = authorableTypes.find(item => item.alias === event.target.value);
      if (selected && capabilities) onChange(selectTypeForMember(member, selected, capabilities));
    }} disabled={readOnly || authorableTypes.length === 0}>
      {!authorableTypes.some(item => item.alias === member.type.alias) ? <option value={member.type.alias} disabled>{member.type.alias} · unavailable</option> : null}
      {authorableTypes.map(item => <option key={item.alias} value={item.alias} disabled={!item.supportsNull && member.isNullable}>{item.displayName} · {item.category}{!item.supportsNull && member.isNullable ? " · first disable nullability" : ""}</option>)}
    </select><small>Alias <code>{member.type.alias}</code>; no CLR identity is authored.</small></label>
    {currentTypeCanBeReapplied && type && capabilities ? <button type="button" onClick={() => onChange(selectTypeForMember(member, type, capabilities))} disabled={readOnly}>Apply advertised type defaults</button> : null}
    {type && type.supportedCollectionKinds.length > 1 ? <label><span>Collection kind</span><select data-contract-field="type.collectionKind" value={member.type.collectionKind} onChange={event => onChange({ ...member, type: { ...member.type, collectionKind: event.target.value } })} disabled={readOnly}>{type.supportedCollectionKinds.map(kind => <option key={kind}>{kind}</option>)}</select></label> : <ReadOnlyFact label="Collection kind" value={member.type.collectionKind} field="type.collectionKind" />}
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
    ? "The selected capability type cannot be nullable."
    : nullDefaultRequiresNullability
      ? "Remove or change the literal null default before tightening nullability."
      : "Independent from requiredness and production obligations.";
  return <label className="ad-contract-check"><input data-contract-field="isNullable" type="checkbox" checked={member.isNullable} onChange={event => onChange({ ...member, isNullable: event.target.checked })} disabled={readOnly || !type?.supportsNull || nullDefaultRequiresNullability} /><span>Allows null</span><small>{description}</small></label>;
}

function DefaultEditor({ member, type, expressions, readOnly, onValidityChange, onChange }: {
  member: ActivityInputContract;
  type?: ActivityContractTypeCapability;
  expressions: StudioExpressionDescriptor[];
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(member: ActivityInputContract): void;
}) {
  const [candidate, setCandidate] = useState(member.default);
  const [valid, setValid] = useState(true);
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
  const setCandidateValidity = (nextValid: boolean) => {
    setValid(nextValid);
    onValidityChange(nextValid);
  };
  return <div className="ad-default-editor" data-contract-field-container="default">
    <label><span>Default</span><select data-contract-field="default" value={mode} onChange={event => {
      const next = event.target.value;
      setCandidateValidity(true);
      if (next === "none") setCandidate(null);
      if (next === "literal") setCandidate({ syntax: "Literal", value: member.isNullable && type?.supportsNull ? null : "" });
      if (next === "expression" && expressions[0]) setCandidate({ syntax: expressions[0].type, value: "" });
    }} disabled={readOnly}>
      <option value="none">No default</option>
      <option value="literal">Literal</option>
      <option value="expression" disabled={!expressionAvailable}>Expression</option>
    </select><small>No default is distinct from a configured literal null.</small></label>
    {mode === "literal" && candidate ? <LiteralDefaultField value={candidate.value} supportsNull={member.isNullable && type?.supportsNull === true} readOnly={readOnly} onValidityChange={setCandidateValidity} onChange={value => setCandidate({ ...candidate, syntax: "Literal", value })} referenceKey={member.referenceKey} /> : null}
    {mode === "expression" && candidate ? <ExpressionDefaultFields value={candidate} expressions={expressions} readOnly={readOnly} onChange={setCandidate} /> : null}
    {!expressionAvailable ? <div className="ad-inline-status" role="status">No authorized expression syntax is advertised, so Studio does not invent one.</div> : null}
    <button type="button" onClick={() => onChange({ ...member, default: candidate })} disabled={readOnly || !dirty || !valid}>Apply default</button>
  </div>;
}

function LiteralDefaultField({ value, supportsNull, readOnly, onValidityChange, onChange, referenceKey }: {
  value: unknown;
  supportsNull: boolean;
  readOnly: boolean;
  onValidityChange(valid: boolean): void;
  onChange(value: unknown): void;
  referenceKey: string;
}) {
  const [source, setSource] = useState(() => serializeDefaultValue(value));
  const [error, setError] = useState<string | null>(null);
  useEffect(() => setSource(serializeDefaultValue(value)), [value]);
  return <label><span>Literal JSON value</span><textarea data-contract-field="default.value" value={source} onChange={event => {
    const next = event.target.value;
    setSource(next);
    const parsed = parseLiteralDefault(next);
    const nextError = !parsed.ok ? "Enter one valid JSON value." : parsed.value === null && !supportsNull ? "The selected capability type does not support null." : null;
    setError(nextError);
    onValidityChange(!nextError);
    if (parsed.ok && !nextError) onChange(parsed.value);
  }} rows={3} disabled={readOnly} aria-invalid={Boolean(error)} aria-describedby={error ? `literal-${referenceKey}-error` : undefined} />{error ? <small id={`literal-${referenceKey}-error`} role="alert">{error} The visible text is not saved.</small> : null}</label>;
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

function AdvancedMemberFields<T extends ActivityInputContract | ActivityOutputContract>({ member, readOnly, onChange, onReferenceKeyChange }: {
  member: T;
  readOnly: boolean;
  onChange(member: T): void;
  onReferenceKeyChange(key: string): void;
}) {
  return <details className="ad-contract-advanced"><summary>Advanced identity and presentation</summary><div className="ad-contract-fields">
    <ReferenceKeyField value={member.referenceKey} readOnly={readOnly} onApply={onReferenceKeyChange} />
    <label><span>Display name</span><input value={member.displayName ?? ""} onChange={event => onChange({ ...member, displayName: event.target.value || null })} disabled={readOnly} /><small>Presentation-only; changing this does not change binding identity.</small></label>
    <label><span>Description</span><textarea value={member.description ?? ""} onChange={event => onChange({ ...member, description: event.target.value || null })} disabled={readOnly} /></label>
    <label><span>Category</span><input value={member.category ?? ""} onChange={event => onChange({ ...member, category: event.target.value || null })} disabled={readOnly} /></label>
    <label><span>Order</span><input type="number" value={member.order ?? 0} onChange={event => onChange({ ...member, order: Number(event.target.value) })} disabled={readOnly} /></label>
    <label><span>UI hint</span><input value={member.uiHint ?? ""} onChange={event => onChange({ ...member, uiHint: event.target.value || null })} disabled={readOnly} /></label>
  </div></details>;
}

function ReferenceKeyField({ value, readOnly, onApply }: { value: string; readOnly: boolean; onApply(key: string): void }) {
  const [candidate, setCandidate] = useState(value);
  useEffect(() => setCandidate(value), [value]);
  return <div className="ad-contract-staged-field"><label><span>Stable reference key</span><input data-contract-reference-key-control data-contract-field="referenceKey" value={candidate} onChange={event => setCandidate(event.target.value)} disabled={readOnly} /></label><button type="button" onClick={() => onApply(candidate)} disabled={readOnly || !candidate.trim() || candidate.trim() === value}>Apply key</button><small>Changing a published key is breaking; Studio never rewrites consumers.</small></div>;
}

function StagedNameField({ value, readOnly, onApply }: { value: string; readOnly: boolean; onApply(name: string): void }) {
  const [candidate, setCandidate] = useState(value);
  useEffect(() => setCandidate(value), [value]);
  return <div className="ad-contract-staged-field"><label><span>Name</span><input value={candidate} onChange={event => setCandidate(event.target.value)} disabled={readOnly} /></label><button type="button" onClick={() => onApply(candidate.trim())} disabled={readOnly || !candidate.trim() || candidate.trim() === value}>Apply name</button><small>The technical name is behavioral. Use Display name for presentation-only copy.</small></div>;
}

function ReadOnlyFact({ label, value, field }: { label: string; value: string; field?: string }) {
  return <div className="ad-readonly-fact" data-contract-field={field} tabIndex={field ? -1 : undefined}><span>{label}</span><strong>{value}</strong></div>;
}

function UnavailableType({ alias }: { alias: string }) {
  return <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /><span>Stored type alias <code>{alias}</code> is not an authorable capability selection. This mutable draft is invalid until it is replaced with an advertised type.</span></div>;
}

function BaselineSummary({ sourceVersionId, contract, baseline, unavailable }: { sourceVersionId: string; contract: ActivityContract; baseline?: ActivityContract; unavailable: boolean }) {
  const summary = baseline ? compareContractPresentation(contract, baseline) : null;
  return <div className="ad-baseline-summary" role="status"><strong>Published baseline <code>{sourceVersionId}</code></strong>{!baseline ? <span>{unavailable ? "The exact immutable baseline could not be loaded. Existing-member changes remain conservatively guarded as potentially breaking." : "Loading the exact immutable contract used for local impact classification…"}</span> : <span>{summary!.breaking} behavioral or identity change{summary!.breaking === 1 ? "" : "s"} · {summary!.presentation} presentation-only change{summary!.presentation === 1 ? "" : "s"}. Consumer impact evidence is deferred to publication/dependency review; this editor changes only the current draft.</span>}</div>;
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

export function classifyBreakingContractTransition(
  kind: "input" | "output" | "outcome",
  current: ContractMember,
  next: ContractMember
) {
  const reasons: string[] = [];
  if (current.name !== next.name) reasons.push("Changing the technical name is classified as breaking; edit Display name for presentation-only copy.");
  if (kind !== "outcome") {
    const currentBoundary = current as ActivityInputContract | ActivityOutputContract;
    const nextBoundary = next as ActivityInputContract | ActivityOutputContract;
    if (currentBoundary.type.alias !== nextBoundary.type.alias || currentBoundary.type.collectionKind !== nextBoundary.type.collectionKind)
      reasons.push("Changing the capability type or collection kind is classified as breaking.");
    if (currentBoundary.storageDriverKey !== nextBoundary.storageDriverKey)
      reasons.push("Changing the durable storage driver is classified as breaking.");
    if (currentBoundary.isNullable && !nextBoundary.isNullable)
      reasons.push("Tightening nullability is classified as breaking.");
  }
  if (kind === "input" && "default" in current && "default" in next) {
    if (!current.isRequired && next.isRequired && next.default === null)
      reasons.push("Requiring an effective value without a default is classified as breaking.");
    if (current.default !== null && next.default === null)
      reasons.push("Removing a configured default is classified as breaking.");
    if (current.default !== null && next.default !== null && JSON.stringify(current.default) !== JSON.stringify(next.default))
      reasons.push("Changing an existing default is classified as breaking.");
  }
  if (kind === "output" && "isRequired" in current && "isRequired" in next && current.isRequired && !next.isRequired)
    reasons.push("Changing this published output production obligation is classified as breaking by the authoritative compatibility policy.");
  if (kind === "outcome" && "isEmitted" in current && "isEmitted" in next && !current.isEmitted && next.isEmitted)
    reasons.push("Changing a published outcome to emitted is classified as breaking.");
  return reasons;
}
