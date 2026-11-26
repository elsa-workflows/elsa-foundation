import { StudioField } from "./Field";
import React, { useEffect, useState } from "react";
import type {
  ElsaStudioModuleApi,
  StudioSettingDescriptor,
  StudioSettingEditorContribution,
  StudioSettingEditorProps
} from "@elsa-workflows/studio-sdk";

export function registerBuiltInSettingEditors(api: ElsaStudioModuleApi) {
  const contributions: StudioSettingEditorContribution[] = [
    { id: "select", presentsField: true, order: 10, supports: setting => setting.options.length > 0 || hasHint(setting, "select"), component: SelectSettingEditor },
    { id: "boolean", order: 20, supports: setting => normalize(setting.jsonType) === "boolean", component: BooleanSettingEditor },
    { id: "number", presentsField: true, order: 30, supports: setting => ["integer", "number"].includes(normalize(setting.jsonType)), component: NumberSettingEditor },
    { id: "json", presentsField: true, order: 40, supports: setting => ["object", "array"].includes(normalize(setting.jsonType)) || hasHint(setting, "json") || hasHint(setting, "textarea"), component: JsonSettingEditor },
    { id: "secret", presentsField: true, order: 50, supports: setting => setting.secret || setting.sensitive, component: SecretSettingEditor },
    { id: "text", presentsField: true, order: 100, supports: () => true, component: TextSettingEditor }
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

function TextSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  return <SettingOutline setting={setting}><input type="text" value={inputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} /></SettingOutline>;
}

function SecretSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  return <SettingOutline setting={setting}><input type="password" value={inputValue(value)} disabled={disabled} onChange={event => onChange(event.target.value)} /></SettingOutline>;
}

function NumberSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  return <SettingOutline setting={setting}><input type="number" value={inputValue(value)} disabled={disabled} onChange={event => onChange(
    normalize(setting.jsonType) === "integer" ? Number.parseInt(event.target.value || "0", 10) : Number.parseFloat(event.target.value || "0")
  )} /></SettingOutline>;
}

function SelectSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const selected = JSON.stringify(value ?? "");
  return <SettingOutline setting={setting}><select value={selected} disabled={disabled} onChange={event => onChange(
    setting.options.find(option => JSON.stringify(option.value) === event.target.value)?.value ?? ""
  )}>
    {!setting.required ? <option value={JSON.stringify("")}>Empty</option> : null}
    {setting.options.map(option => <option key={`${setting.name}-${JSON.stringify(option.value)}`} value={JSON.stringify(option.value)}>{option.label}</option>)}
  </select></SettingOutline>;
}

function JsonSettingEditor({ setting, value, disabled, onChange }: StudioSettingEditorProps) {
  const [text, setText] = useState(() => jsonText(value, setting));
  const [invalid, setInvalid] = useState(false);
  useEffect(() => { setText(jsonText(value, setting)); setInvalid(false); }, [setting, value]);
  return <SettingOutline setting={setting} error={invalid ? "Invalid JSON" : undefined}><textarea value={text} disabled={disabled} rows={5} aria-invalid={invalid} onChange={event => {
    setText(event.target.value);
    try { onChange(JSON.parse(event.target.value || defaultJson(setting))); setInvalid(false); }
    catch { setInvalid(true); }
  }} /></SettingOutline>;
}

function normalize(value?: string | null) { return (value ?? "").trim().toLowerCase(); }
function hasHint(setting: StudioSettingDescriptor, value: string) { return normalize(setting.uiHint).includes(value); }
function inputValue(value: unknown) { return value == null ? "" : typeof value === "object" ? JSON.stringify(value) : String(value); }
function defaultJson(setting: StudioSettingDescriptor) { return normalize(setting.jsonType) === "array" ? "[]" : "{}"; }
function jsonText(value: unknown, setting: StudioSettingDescriptor) { return value == null || value === "" ? defaultJson(setting) : JSON.stringify(value, null, 2); }

function SettingOutline({ setting, children, error }: { setting: StudioSettingDescriptor; children: React.ReactNode; error?: string }) {
  return <StudioField label={setting.displayName || setting.name} description={setting.description} error={error} errorRole="alert"
    meta={<>{setting.required ? <em>Required</em> : null}{setting.restartRequired ? <em>Reload</em> : null}{setting.advanced ? <em>Advanced</em> : null}</>}>
    {children}
  </StudioField>;
}
