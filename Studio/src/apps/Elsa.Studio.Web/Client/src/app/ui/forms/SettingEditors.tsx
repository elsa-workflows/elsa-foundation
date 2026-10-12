import React, { useEffect, useState } from "react";
import type {
  ElsaStudioModuleApi,
  StudioSettingDescriptor,
  StudioSettingEditorContribution,
  StudioSettingEditorProps
} from "../../../sdk";

export function registerBuiltInSettingEditors(api: ElsaStudioModuleApi) {
  const contributions: StudioSettingEditorContribution[] = [
    { id: "select", order: 10, supports: setting => setting.options.length > 0 || hasHint(setting, "select"), component: SelectSettingEditor },
    { id: "boolean", order: 20, supports: setting => normalize(setting.jsonType) === "boolean", component: BooleanSettingEditor },
    { id: "number", order: 30, supports: setting => ["integer", "number"].includes(normalize(setting.jsonType)), component: NumberSettingEditor },
    { id: "json", order: 40, supports: setting => ["object", "array"].includes(normalize(setting.jsonType)) || hasHint(setting, "json") || hasHint(setting, "textarea"), component: JsonSettingEditor },
    { id: "secret", order: 50, supports: setting => setting.secret || setting.sensitive, component: SecretSettingEditor },
    { id: "text", order: 100, supports: () => true, component: TextSettingEditor }
  ];

  const registered = new Set(api.settingEditors.list().map(editor => editor.id));
  for (const contribution of contributions) if (!registered.has(contribution.id)) api.settingEditors.add(contribution);
}

export function selectSettingEditor(api: ElsaStudioModuleApi, setting: StudioSettingDescriptor) {
  const editor = api.settingEditors.list()
    .filter(contribution => contribution.supports(setting))
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))[0];
  if (!editor) throw new Error(`No setting editor is registered for '${setting.name}'.`);
  return editor;
}

function BooleanSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="checkbox" className="feature-management-setting-switch-input" checked={Boolean(value)} disabled={disabled} onChange={event => onChange(event.target.checked)} />;
}

function TextSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="text" value={inputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />;
}

function SecretSettingEditor({ value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="password" value={inputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} />;
}

function NumberSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  return <input type="number" value={inputValue(value)} disabled={disabled} onChange={event => onChange(
    normalize(setting.jsonType) === "integer" ? Number.parseInt(event.target.value || "0", 10) : Number.parseFloat(event.target.value || "0")
  )} />;
}

function SelectSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const selected = JSON.stringify(value ?? "");
  return <select value={selected} disabled={disabled} onChange={event => onChange(
    setting.options.find(option => JSON.stringify(option.value) === event.target.value)?.value ?? ""
  )}>
    {!setting.required ? <option value={JSON.stringify("")}>Empty</option> : null}
    {setting.options.map(option => <option key={`${setting.name}-${JSON.stringify(option.value)}`} value={JSON.stringify(option.value)}>{option.label}</option>)}
  </select>;
}

function JsonSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const [text, setText] = useState(() => jsonText(value, setting));
  const [invalid, setInvalid] = useState(false);
  useEffect(() => { setText(jsonText(value, setting)); setInvalid(false); }, [setting, value]);
  return <><textarea value={text} disabled={disabled} rows={5} aria-invalid={invalid} onChange={event => {
    setText(event.target.value);
    try { onChange(JSON.parse(event.target.value || defaultJson(setting))); setInvalid(false); }
    catch { setInvalid(true); }
  }} />{invalid ? <small role="alert">Invalid JSON</small> : null}</>;
}

function normalize(value?: string | null) { return (value ?? "").trim().toLowerCase(); }
function hasHint(setting: StudioSettingDescriptor, value: string) { return normalize(setting.uiHint).includes(value); }
function inputValue(value: unknown) { return value == null ? "" : typeof value === "object" ? JSON.stringify(value) : String(value); }
function defaultJson(setting: StudioSettingDescriptor) { return normalize(setting.jsonType) === "array" ? "[]" : "{}"; }
function jsonText(value: unknown, setting: StudioSettingDescriptor) { return value == null || value === "" ? defaultJson(setting) : JSON.stringify(value, null, 2); }
