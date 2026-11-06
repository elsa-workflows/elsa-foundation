import { useState } from "react";
import { StudioAlert, StudioButton, StudioField, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { getWorkflowRunInputControlKind, parseWorkflowRunInputs } from "../workflowRunInputs";
import { parseLiteralJson, serializeTriggerPayload } from "./json";
import type { WorkflowInput } from "../workflowTypes";
import type { TriggerClient } from "./api";
import { ActionError, Findings, label, useTriggerCompletion } from "./shared";
import { SourceFieldSelector, realizeSourcePath, type SourcePathDraft } from "./SourceFieldSelector";
import type { DeclaredInput, InputBinding, InputMapping, JsonValue, MappingContext, MappingPreview, TargetSelector, TargetView } from "./types";

export interface MappingDraft extends SourcePathDraft { kind: "Omit" | "Null" | InputBinding["kind"]; text: string; language: string; source: string; template?: Extract<InputBinding, { kind: "Expression" }>["template"]; parameters: (SourcePathDraft & { name: string; kind: "SourceField" | "Literal"; text: string })[] }
export type MappingDrafts = Record<string, MappingDraft>;
const emptyDraft = (): MappingDraft => ({ kind: "Omit", text: "", path: [], language: "", source: "", parameters: [] });
const ownDraft = (drafts: MappingDrafts, name: string) => Object.hasOwn(drafts, name) ? drafts[name] : emptyDraft();
const ownError = (errors: Record<string, string>, name: string) => Object.hasOwn(errors, name) ? errors[name] : undefined;
export function mappingDrafts(mappings: InputMapping[]): MappingDrafts {
  return Object.fromEntries(mappings.map(mapping => {
    const binding = mapping.binding;
    return [mapping.inputName, { ...emptyDraft(), kind: binding.kind === "Literal" && binding.value === null ? "Null" : binding.kind,
      ...(binding.kind === "Literal" ? { text: typeof binding.value === "string" ? binding.value : serializeTriggerPayload(binding.value) } : binding.kind === "SourceField" ? { path: binding.path } : { template: binding.template, language: binding.template.language, source: binding.template.source, parameters: Object.entries(binding.parameters).map(([name, parameter]) => ({ name, kind: parameter.kind, text: parameter.kind === "Literal" ? serializeTriggerPayload(parameter.value) : "", path: parameter.kind === "SourceField" ? parameter.path : [] })) }) }];
  }));
}
export function realizeMappings(inputs: DeclaredInput[], drafts: MappingDrafts): { mappings: InputMapping[]; errors: Record<string, string> } {
  const errors = Object.create(null) as Record<string, string>;
  const mappings: InputMapping[] = [];
  const names = new Set(inputs.map(input => input.name));
  for (const [name, draft] of Object.entries(drafts)) if (!names.has(name) && draft.kind !== "Omit") errors[name] = "This input is absent from the selected serving contract. Remove its mapping or choose a compatible target.";
  for (const input of inputs) {
    const draft = ownDraft(drafts, input.name);
    if (draft.kind === "Omit") continue;
    try {
      if (draft.kind === "Null") mappings.push({ inputName: input.name, binding: { kind: "Literal", value: null } });
      else if (draft.kind === "Literal") {
        if (input.type.collectionKind === "Single" && /^(system\.)?string$/i.test(input.type.alias)) { mappings.push({ inputName: input.name, binding: { kind: "Literal", value: draft.text } }); continue; }
        const descriptor = { ...input, referenceKey: input.name, displayName: input.name, description: "", category: "", uiHint: "", storageDriverType: null, isNullable: true, isRequired: false } as WorkflowInput;
        if (getWorkflowRunInputControlKind(descriptor) === "json") { mappings.push({ inputName: input.name, binding: { kind: "Literal", value: parseLiteralJson(draft.text) } }); continue; }
        const parsed = parseWorkflowRunInputs([descriptor], { [input.name]: draft.text });
        if (Object.keys(parsed.errors).length) throw new Error(parsed.errors[input.name]);
        if (!Object.hasOwn(parsed.values, input.name)) throw new Error("Enter a value or choose Use workflow default / omit. Enter null as JSON for an explicit null.");
        mappings.push({ inputName: input.name, binding: { kind: "Literal", value: parsed.values[input.name] as JsonValue } });
      } else if (draft.kind === "SourceField") {
        mappings.push({ inputName: input.name, binding: { kind: "SourceField", path: realizeSourcePath(draft) } });
      } else {
        if (!draft.language || !draft.source.trim()) throw new Error("Choose a supported language and enter an expression.");
        const parameters = Object.create(null) as Extract<InputBinding, { kind: "Expression" }>["parameters"];
        for (const parameter of draft.parameters) {
          if (!parameter.name.trim() || Object.hasOwn(parameters, parameter.name)) throw new Error("Expression parameter names must be nonempty and unique.");
          if (parameter.kind === "SourceField") {
            try { parameters[parameter.name] = { kind: "SourceField", path: realizeSourcePath(parameter) }; }
            catch (error) { throw new Error(`Source for ${parameter.name}: ${(error as Error).message}`); }
          }
          else parameters[parameter.name] = { kind: "Literal", value: parseLiteralJson(parameter.text) };
        }
        mappings.push({ inputName: input.name, binding: { kind: "Expression", template: { ...(draft.template ?? { resultType: { alias: input.type.alias, collectionKind: input.type.collectionKind }, options: {}, capabilityProfile: "binding-pure-v1" as const }), language: draft.language, source: draft.source }, parameters } });
      }
    } catch (error) { errors[input.name] = error instanceof Error ? error.message : "The mapping is invalid."; }
  }
  return { mappings, errors };
}

export function MappingEditor({ client, triggerId, triggerRevision, target, context, drafts, onChange, disabled, errors }: {
  client: TriggerClient; triggerId: string; triggerRevision: number; target: TargetView; context: MappingContext; drafts: MappingDrafts; onChange(drafts: MappingDrafts): void; disabled?: boolean; errors: Record<string, string>;
}) {
  const [sample, setSample] = useState('{"payload":{}}');
  const [preview, setPreview] = useState<MappingPreview>();
  const [previewError, setPreviewError] = useState<unknown>();
  const [busy, setBusy] = useState(false);
  const [previewSignature, setPreviewSignature] = useState("");
  const complete = useTriggerCompletion();
  const signature = serializeTriggerPayload({ drafts, sample, target: target.contractRevision });
  const inputs = target.inputContract?.inputs ?? [];
  const sourceFields = context.sourceSchema.fields;
  function update(name: string, patch: Partial<MappingDraft>) { onChange({ ...drafts, [name]: { ...ownDraft(drafts, name), ...patch } }); }
  async function runPreview() {
    const built = realizeMappings(inputs, drafts);
    if (Object.keys(built.errors).length) { setPreviewError(new Error(Object.values(built.errors).join(" "))); return; }
    setBusy(true); setPreviewError(undefined); setPreview(undefined);
    try {
      const result = await client.mappingPreview(triggerId, triggerRevision, { definitionId: target.definitionId, slotName: target.slotName } satisfies TargetSelector, built.mappings, parseLiteralJson(sample), target.contractRevision);
      complete(() => { setPreviewSignature(signature); setPreview(result); setBusy(false); });
    } catch (error) { complete(() => { setPreviewError(error); setBusy(false); }); }
  }
  return <section className="wf-trigger-mappings" aria-label="Workflow input mappings">
    <h4>Declared inputs</h4><p>Mapping uses the current serving contract. Preview is advisory; actual admission rechecks the target and inputs.</p>
    <Findings items={target.findings} /><Findings items={context.findings} />
    {target.inputContract === null || target.inputContract === undefined ? <StudioAlert tone="warning">The serving input contract is unavailable. Save this association disabled and publish or select a compatible slot.</StudioAlert> : !inputs.length ? <p>This serving workflow declares no inputs.</p> : null}
    {inputs.map(input => {
      const draft = ownDraft(drafts, input.name);
      return <fieldset key={input.name} className="wf-trigger-mapping" disabled={disabled}>
        <legend>{input.name}{input.isRequired ? " · Required" : ""}</legend><p className="wf-trigger-muted">{input.type.collectionKind === "Single" ? input.type.alias : `${input.type.collectionKind}<${input.type.alias}>`}{Object.hasOwn(input, "defaultValue") ? " · Workflow default available" : ""}</p>
        <StudioField label="Value source"><select aria-label={`Value source for ${input.name}`} value={draft.kind} onChange={event => update(input.name, { kind: event.currentTarget.value as MappingDraft["kind"] })}><option value="Omit">Use workflow default / omit</option><option value="SourceField">Source field</option><option value="Literal">Configured value</option><option value="Null">Explicit null</option><option value="Expression">Expression</option></select></StudioField>
        {draft.kind === "SourceField" ? <SourceFieldSelector fields={sourceFields} value={{ kind: "SourceField", path: draft.path, pathText: draft.pathText }} label={`Source field for ${input.name}`} pathLabel={`Source path for ${input.name}`} onChange={selection => update(input.name, selection)} /> : draft.kind === "Literal" ? <StudioField label="Configured value" description={input.type.alias.toLowerCase() === "string" ? "Enter the text to supply." : "Enter a value in the declared input's format. JSON null is distinct from omission."}><input aria-label={`Configured value for ${input.name}`} value={draft.text} aria-invalid={!!ownError(errors, input.name)} onChange={event => update(input.name, { text: event.currentTarget.value })} /></StudioField> : draft.kind === "Expression" ? <>
          <StudioField label="Expression language"><select aria-label={`Expression language for ${input.name}`} value={draft.language} onChange={event => update(input.name, { language: event.currentTarget.value })}><option value="">Choose a language…</option>{context.supportedExpressions.map(language => <option key={language.language} value={language.language} disabled={!language.available}>{language.displayName}{!language.available ? ` · ${language.reason || "Unavailable"}` : ""}</option>)}</select></StudioField>
          <StudioField label="Expression" description="Only the named parameters below are available. Workflow variables and activity outputs do not exist at this point."><textarea aria-label={`Expression for ${input.name}`} value={draft.source} onChange={event => update(input.name, { source: event.currentTarget.value })} /></StudioField>
          {draft.parameters.map((parameter, index) => <div key={index} className="wf-trigger-parameter">
            <StudioField label="Parameter name"><input aria-label={`Parameter ${index + 1} name for ${input.name}`} value={parameter.name} onChange={event => { const name = event.currentTarget.value; update(input.name, { parameters: draft.parameters.map((item, position) => position === index ? { ...item, name } : item) }); }} /></StudioField>
            <SourceFieldSelector fields={sourceFields} value={parameter} label={`Parameter ${index + 1} source for ${input.name}`} pathLabel={`Parameter ${index + 1} source path for ${input.name}`} allowLiteral onChange={selection => update(input.name, { parameters: draft.parameters.map((item, position) => position === index ? { ...item, ...selection } : item) })} />
            {parameter.kind === "Literal" ? <StudioField label="JSON value"><input aria-label={`Parameter ${index + 1} value for ${input.name}`} value={parameter.text} onChange={event => { const text = event.currentTarget.value; update(input.name, { parameters: draft.parameters.map((item, position) => position === index ? { ...item, text } : item) }); }} /></StudioField> : null}
            <StudioButton type="button" onClick={() => update(input.name, { parameters: draft.parameters.filter((_, position) => position !== index) })}>Remove parameter {index + 1}</StudioButton>
          </div>)}
          <StudioButton type="button" onClick={() => update(input.name, { parameters: [...draft.parameters, { name: "", kind: "SourceField", path: [], text: "" }] })}>Add parameter for {input.name}</StudioButton>
        </> : null}
        {ownError(errors, input.name) ? <p role="alert">{ownError(errors, input.name)}</p> : null}
      </fieldset>;
    })}
    {Object.keys(drafts).filter(name => !inputs.some(input => input.name === name) && drafts[name].kind !== "Omit").map(name => <StudioAlert key={name} tone="warning"><p>{name} is absent from the current serving contract.</p><StudioButton type="button" disabled={disabled} onClick={() => onChange(Object.fromEntries(Object.entries(drafts).filter(([key]) => key !== name)))}>Remove mapping for {name}</StudioButton></StudioAlert>)}
    <details><summary>Preview with sample data</summary>
      <StudioField label="Sample source envelope" description="Synthetic sample payload and source fields; this does not send an event, accept work or start a workflow."><textarea aria-label="Sample source envelope" value={sample} disabled={disabled || busy} onChange={event => setSample(event.currentTarget.value)} /></StudioField>
      <StudioButton type="button" disabled={disabled || busy || !target.inputContract} onClick={() => void runPreview()}>{busy ? "Evaluating sample…" : "Preview mapped inputs"}</StudioButton>
      {previewError ? <ActionError error={previewError} /> : null}
      {preview && previewSignature === signature ? <><p>Sample preview · advisory</p><Findings items={preview.findings} /><StudioTableContainer role="region" aria-label="Mapped sample inputs" tabIndex={0}><table className="studio-table"><thead><tr><th scope="col">Input</th><th scope="col">Result</th><th scope="col">Value</th></tr></thead><tbody>{preview.fields.map(field => <tr key={field.inputName}><th scope="row">{field.inputName}</th><td>{label(field.state)}</td><td>{field.state === "Redacted" ? "Protected" : Object.hasOwn(field, "value") ? serializeTriggerPayload(field.value) : "—"}</td></tr>)}</tbody></table></StudioTableContainer></> : preview ? <p role="status">The sample or mappings changed. Run preview again for the current values.</p> : null}
    </details>
  </section>;
}
