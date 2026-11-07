import React, { useEffect, useId, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { createRoot } from "react-dom/client";
import { CreateSecretDialog } from "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs";
import { SecretFormField } from "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretFormField";
import type { SecretDescriptorsResponse } from "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/secretTypes";
import { ThemeProvider, useTheme } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/styles.css";
import "./secretFieldPrototypeFixture.css";

const query = new URLSearchParams(location.search);
const descriptors: SecretDescriptorsResponse = {
  types: [
    { name: "text", displayName: "Text", description: "A text value, concealed until you choose to reveal it.", supportedStoreNames: ["encrypted", "configuration"] },
    { name: "json", displayName: "JSON", editorHint: "multiline", description: "Enter the JSON content for this secret.", supportedStoreNames: ["encrypted"] },
    { name: "rsa-key", displayName: "RSA key", editorHint: "multiline", description: "Paste the PEM-encoded key.", supportedStoreNames: ["encrypted"] },
    { name: "x509-certificate", displayName: "Certificate", editorHint: "multiline", description: "Paste the PEM-encoded certificate.", supportedStoreNames: ["encrypted"] }
  ],
  stores: [
    { name: "encrypted", displayName: "Encrypted", description: "Stored with the configured secret persistence provider.", isReadOnly: false },
    { name: "configuration", displayName: "Configuration", description: "Look up the value from an existing configuration key.", isReadOnly: true }
  ],
  scopes: [
    { name: "Integration:Billing", displayName: "Billing integration", description: "Credentials for billing.", source: "Billing connector" },
    { name: "Integration:CRM", displayName: "CRM integration", description: "Credentials for customer records.", source: "CRM connector" }
  ]
};

type FieldStyle = "filled" | "outlined";
const fieldStyleStorageKey = "secret-field-prototype-style";

function readFieldStyle(): FieldStyle {
  const stored = query.get("field-style") ?? localStorage.getItem(fieldStyleStorageKey);
  return stored === "filled" ? "filled" : "outlined";
}

function FieldStyleSwitch({ value, onChange }: { value: FieldStyle; onChange(value: FieldStyle): void }) {
  const name = useId();
  return <fieldset className="secret-prototype-field-style">
    <legend>Field style</legend>
    {(["filled", "outlined"] as const).map(style => <label key={style}>
      <input type="radio" name={name} value={style} tabIndex={value === style ? 0 : -1} checked={value === style} onChange={() => onChange(style)} />
      {style === "filled" ? "Filled" : "Outlined"}
    </label>)}
  </fieldset>;
}

function StateSheet() {
  const [value, setValue] = useState("");
  return <section className="secrets-modal secret-prototype-states" aria-label="Representative field states">
    <SecretFormField label="Empty field" value={value} hint="Enter a value to compare the empty and populated states.">
      {attributes => <input {...attributes} value={value} placeholder=" " onChange={event => setValue(event.target.value)} />}
    </SecretFormField>
    <SecretFormField label="Populated field" value="Billing connection">
      {attributes => <input {...attributes} defaultValue="Billing connection" placeholder=" " />}
    </SecretFormField>
    <SecretFormField label="Invalid field" value="has spaces" error="Use letters, numbers, dot, dash, underscore, or colon.">
      {attributes => <input {...attributes} defaultValue="has spaces" placeholder=" " />}
    </SecretFormField>
    <SecretFormField label="Disabled field" value="Waiting for creation" disabled>
      {attributes => <input {...attributes} value="Waiting for creation" readOnly />}
    </SecretFormField>
    <SecretFormField label="Read-only field" value="billing.connection" hint="Read only; the value can still be selected and copied.">
      {attributes => <input {...attributes} value="billing.connection" readOnly />}
    </SecretFormField>
    <SecretFormField label="A longer translated label that still needs a clear, readable name" value="Localized content" raised>
      {attributes => <input {...attributes} defaultValue="Localized content" />}
    </SecretFormField>
  </section>;
}

function Prototype() {
  const { availableThemes, currentTheme, mode, setTheme, setMode, supportedModes, workspaceStyle, setWorkspaceStyle } = useTheme();
  const [fieldStyle, setFieldStyle] = useState<FieldStyle>(readFieldStyle);
  const mainRef = useRef<HTMLElement>(null);
  const [dialogHeader, setDialogHeader] = useState<HTMLElement | null>(null);
  const [open, setOpen] = useState(query.get("open") === "1");
  const [states, setStates] = useState(query.get("view") === "states");
  const [saveMode, setSaveMode] = useState(query.get("save") ?? "success");
  const [catalog, setCatalog] = useState(query.get("catalog") ?? "ready");
  const [message, setMessage] = useState("");
  useEffect(() => {
    localStorage.setItem(fieldStyleStorageKey, fieldStyle);
    const url = new URL(location.href);
    url.searchParams.set("field-style", fieldStyle);
    history.replaceState(null, "", url);
  }, [fieldStyle]);
  useEffect(() => {
    setDialogHeader(open ? mainRef.current?.querySelector<HTMLElement>(".secrets-create-header") ?? null : null);
  }, [open]);
  return <main ref={mainRef} className="secret-prototype" data-field-style={fieldStyle} onFocusCapture={event => {
    // Native focus scrolls the control; include its border label and help in the preview.
    if (fieldStyle === "outlined") (event.target as HTMLElement).closest(".secrets-compact-field")?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }}>
    <header><h1>Compact Secret fields</h1><p>Interactive component prototype · demonstration data</p></header>
    <div className="secret-prototype-toolbar">
      <FieldStyleSwitch value={fieldStyle} onChange={setFieldStyle} />
      <label>Theme<select aria-label="Preview theme" value={currentTheme.id} onChange={event => setTheme(event.target.value)}>{availableThemes.map(theme => <option key={theme.id} value={theme.id}>{theme.name}</option>)}</select></label>
      <label>Appearance<select aria-label="Preview appearance" value={mode} onChange={event => setMode(event.target.value as typeof mode)}>{supportedModes.map(item => <option key={item} value={item}>{item}</option>)}</select></label>
      <label>Workspace<select aria-label="Preview workspace" value={workspaceStyle ?? "theme"} onChange={event => setWorkspaceStyle(event.target.value === "theme" ? null : event.target.value as typeof workspaceStyle)}>{["theme", "linen", "minimal", "quiet", "focus", "open"].map(item => <option key={item} value={item}>{item}</option>)}</select></label>
      <label>Save result<select value={saveMode} onChange={event => setSaveMode(event.target.value)}><option value="success">Success</option><option value="error">Failure and retry</option><option value="pending">Pending</option></select></label>
      <label>Scope suggestions<select value={catalog} onChange={event => setCatalog(event.target.value)}>{["ready", "loading", "error", "empty"].map(item => <option key={item} value={item}>{item}</option>)}</select></label>
    </div>
    <div className="secret-prototype-actions"><button onClick={() => setOpen(true)}>Open Create secret</button><button onClick={() => setStates(current => !current)}>Compare field states</button></div>
    {message ? <p role="status">{message}</p> : null}
    {states ? <StateSheet /> : <p>Choose an appearance, then open the dialog. More options includes metadata, storage and suggested or custom scopes.</p>}
    {open ? <CreateSecretDialog descriptors={{ ...descriptors, scopes: catalog === "empty" ? [] : descriptors.scopes }} scopeCatalogLoading={catalog === "loading"} scopeCatalogError={catalog === "error"}
      onRetryScopeCatalog={() => { setCatalog("loading"); window.setTimeout(() => setCatalog("ready"), 300); }}
      onCancel={() => setOpen(false)} onSave={async request => {
        if (saveMode === "pending") await new Promise<void>(resolve => window.addEventListener("prototype-finish-save", () => resolve(), { once: true }));
        if (saveMode === "error") throw new Error("The demonstration save failed. Your entries are retained; try again.");
        setMessage(`Created ${request.name}. This preview does not persist data.`);
      }} /> : null}
    {open && dialogHeader ? createPortal(<FieldStyleSwitch value={fieldStyle} onChange={setFieldStyle} />, dialogHeader) : null}
  </main>;
}

createRoot(document.getElementById("root")!).render(<ThemeProvider><Prototype /></ThemeProvider>);
